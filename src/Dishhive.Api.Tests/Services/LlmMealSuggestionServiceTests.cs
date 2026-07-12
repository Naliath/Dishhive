using Dishhive.Api.Models;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Services;

public class LlmMealSuggestionServiceTests
{
    private static readonly DateOnly WeekStart = new(2026, 6, 15);

    /// <summary>Minimal IChatClient stub returning a canned (or failing) response</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly Func<ChatResponse> _respond;
        public int Calls { get; private set; }
        public List<ChatMessage> LastMessages { get; private set; } = [];
        public ChatOptions? LastOptions { get; private set; }

        public FakeChatClient(string responseText)
            : this(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))) { }

        public FakeChatClient(Func<ChatResponse> respond) => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMessages = messages.ToList();
            LastOptions = options;
            return Task.FromResult(_respond());
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    /// <summary>Prompt stub: no override by default (the shipped editable prompt applies)</summary>
    private sealed class StubPromptProvider(string? overrideText = null) : IAiPromptProvider
    {
        public Task<string?> GetOverrideAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(overrideText);
    }

    /// <summary>Capability stub: pretends the model test already ran with the given verdict,
    /// so unit tests never trigger real probe calls against the fake chat client</summary>
    private sealed class StubCapability(AiResponseMode mode) : IAiModelCapabilityService
    {
        public AiModelTestState State => AiModelTestState.Completed;
        public AiModelTestResult Result { get; } = new()
        {
            TestedAt = DateTimeOffset.UtcNow, Provider = "test", Model = "test", ResponseMode = mode
        };
        AiModelTestResult? IAiModelCapabilityService.Result => Result;
        public Task<AiModelTestResult> EnsureTestedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Result);
        public Task<AiModelTestResult> RetestAsync() => Task.FromResult(Result);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    private static LlmMealSuggestionService CreateService(
        FakeChatClient chatClient, IWebSearchClient? webSearch = null,
        AiResponseMode capabilityMode = AiResponseMode.PromptedJson,
        string? promptOverride = null,
        ILogger<LlmMealSuggestionService>? logger = null,
        IRecipeImportService? importService = null)
    {
        var search = webSearch ?? new NoOpWebSearchClient();
        var importer = importService ?? Substitute.For<IRecipeImportService>();
        return new LlmMealSuggestionService(
            chatClient,
            new RulesMealSuggestionService(),
            new AiOptions { Provider = "ollama", Model = "test" },
            new StubCapability(capabilityMode),
            new StubPromptProvider(promptOverride),
            new ExternalRecipeSessionFactory(search, importer, new WebSearchOptions()),
            new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance),
            logger ?? NullLogger<LlmMealSuggestionService>.Instance);
    }

    /// <summary>A web-search client that reports configured (unlike the NoOp default) so
    /// tests can isolate the SourceConstraints-gating behavior from configuration state</summary>
    private static IWebSearchClient ConfiguredWebSearch()
    {
        var client = Substitute.For<IWebSearchClient>();
        client.IsConfigured.Returns(true);
        return client;
    }

    private static MealSuggestionRequest Request(
        IReadOnlyList<DateOnly>? daysToFill = null,
        IReadOnlyList<RecipeOption>? recipes = null,
        IReadOnlyList<FavoriteDish>? favorites = null,
        string? instructions = null) => new()
    {
        WeekStart = WeekStart,
        DaysToFill = daysToFill ?? [WeekStart, WeekStart.AddDays(1)],
        KnownRecipes = recipes ?? [],
        Favorites = favorites ?? [],
        Instructions = instructions
    };

    [Fact]
    public async Task Suggest_ValidResponse_ReturnsParsedSuggestions()
    {
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Spaghetti","recipeTitle":null,"reason":"Family favorite"},
              {"date":"2026-06-16","dishName":"Fish curry","recipeTitle":null,"reason":"Variety"}
            ]}
            """);

        var suggestions = await CreateService(chatClient).SuggestAsync(Request());

        suggestions.Should().HaveCount(2);
        suggestions[0].DishName.Should().Be("Spaghetti");
        suggestions[0].Date.Should().Be(WeekStart);
        suggestions[1].Reason.Should().Be("Variety");
    }

    [Fact]
    public async Task Suggest_LogsRawAiOutputAtDebugLevel()
    {
        const string response =
            "{\"suggestions\":[{\"date\":\"2026-06-15\",\"dishName\":\"Debug dish\"}]}";
        var logger = new CapturingLogger<LlmMealSuggestionService>();

        await CreateService(new FakeChatClient(response), logger: logger)
            .SuggestAsync(Request(daysToFill: [WeekStart]));

        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug
            && entry.Message.Contains("Debug dish", StringComparison.Ordinal)
            && entry.Message.Contains("visible=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Suggest_UnparseableWhitespace_LogsEscapedOutputAndReasoning()
    {
        var responses = new Queue<ChatResponse>(
        [
            new ChatResponse(new ChatMessage(ChatRole.Assistant, new List<AIContent>
            {
                new TextReasoningContent("I kept reasoning until the output limit."),
                new TextContent(" \r\n\t ")
            })) { FinishReason = ChatFinishReason.Length },
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"suggestions":[{"date":"2026-06-15","dishName":"Recovered"}]}"""))
        ]);
        var logger = new CapturingLogger<LlmMealSuggestionService>();

        await CreateService(new FakeChatClient(() => responses.Dequeue()), logger: logger)
            .SuggestAsync(Request(daysToFill: [WeekStart]));

        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("\\r\\n\\t", StringComparison.Ordinal)
            && entry.Message.Contains("reasoningChars=40", StringComparison.Ordinal)
            && entry.Message.Contains("reasoning likely consumed the output budget", StringComparison.Ordinal));
        logger.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Debug
            && entry.Message.Contains("I kept reasoning until the output limit.", StringComparison.Ordinal)
            && entry.Message.Contains("TextReasoningContent", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Suggest_RecipeTitleMatch_FillsRecipeId()
    {
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Lasagne","recipeTitle":"Lasagne verde","reason":null}
            ]}
            """);

        var suggestions = await CreateService(chatClient).SuggestAsync(
            Request(recipes: [new RecipeOption { Id = recipeId, Title = "Lasagne verde" }]));

        suggestions.Should().ContainSingle();
        suggestions[0].RecipeId.Should().Be(recipeId);
    }

    [Fact]
    public async Task Suggest_DatesOutsideDaysToFill_AreDropped()
    {
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Keep me","recipeTitle":null,"reason":null},
              {"date":"2026-06-20","dishName":"Outside range","recipeTitle":null,"reason":null},
              {"date":"not-a-date","dishName":"Bad date","recipeTitle":null,"reason":null}
            ]}
            """);

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        suggestions.Should().ContainSingle(s => s.DishName == "Keep me");
    }

    [Fact]
    public async Task Suggest_MalformedJson_FallsBackToRules()
    {
        var chatClient = new FakeChatClient("I'd love to help! Here are some meals: spaghetti...");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Fallback dish" }]));

        suggestions.Should().ContainSingle(s => s.DishName == "Fallback dish");
    }

    [Fact]
    public async Task Suggest_ChatClientThrows_FallsBackToRules()
    {
        var chatClient = new FakeChatClient(() => throw new HttpRequestException("connection refused"));

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Fallback dish" }]));

        suggestions.Should().ContainSingle(s => s.DishName == "Fallback dish");
    }

    [Fact]
    public async Task Suggest_NoDaysToFill_ReturnsEmptyWithoutCallingModel()
    {
        var chatClient = new FakeChatClient("{}");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: []));

        suggestions.Should().BeEmpty();
        chatClient.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Suggest_ModelFailedCapabilityTest_UsesRulesWithoutCallingModel()
    {
        // A model the capability test proved unfit (no parseable JSON ever) is not
        // called at all — every such call would be a known-doomed wait
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Should never appear"}]}""");

        var suggestions = await CreateService(chatClient, capabilityMode: AiResponseMode.None)
            .SuggestAsync(Request(
                daysToFill: [WeekStart],
                favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Fallback dish" }]));

        chatClient.Calls.Should().Be(0);
        suggestions.Should().ContainSingle(s =>
            s.DishName == "Fallback dish" && s.Source == MealSuggestionSource.RulesFallback);
    }

    [Fact]
    public async Task Suggest_SchemaModeVerified_SetsJsonSchemaResponseFormat()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Spaghetti"}]}""");

        await CreateService(chatClient, capabilityMode: AiResponseMode.JsonSchema)
            .SuggestAsync(Request(daysToFill: [WeekStart]));

        chatClient.LastOptions!.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>()
            .Which.Schema.Should().NotBeNull();
    }

    [Fact]
    public async Task Suggest_PromptedMode_LeavesResponseFormatUnset()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Spaghetti"}]}""");

        await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        chatClient.LastOptions!.ResponseFormat.Should().BeNull();
    }

    [Fact]
    public async Task Suggest_DisableThinking_UsesProviderReasoningControl()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Spaghetti"}]}""");

        await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        chatClient.LastOptions!.Reasoning.Should().NotBeNull();
        chatClient.LastOptions.Reasoning!.Effort.Should().Be(ReasoningEffort.None);
        chatClient.LastOptions.Reasoning.Output.Should().Be(ReasoningOutput.None);
    }

    [Fact]
    public async Task Suggest_ExplicitChicken_RemainsSharedAndGetsDietReviewWarning()
    {
        var omnivoreId = Guid.NewGuid();
        var vegetarianId = Guid.NewGuid();
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"chicken-monday","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":null,"count":1,"distinct":true,"dishRequest":"chicken","requiredClasses":["Poultry"],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":true}]}""")),
            new(new ChatMessage(ChatRole.Assistant,
                """{"suggestions":[{"date":"2026-06-15","dishName":"Chicken curry","recipeTitle":"Chicken curry","attendeeIds":[],"constraintIds":["chicken-monday"]}]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());
        var recipeId = Guid.NewGuid();
        var request = Request(daysToFill: [WeekStart]) with
        {
            Members =
            [
                new MemberProfile { Id = omnivoreId, Name = "Alex" },
                new MemberProfile
                {
                    Id = vegetarianId,
                    Name = "Naomi",
                    Diets = [new DietaryTagProfile { Name = "Vegetarian", ExcludedClasses = [IngredientClass.Poultry] }]
                }
            ],
            KnownRecipes = [new RecipeOption { Id = recipeId, Title = "Chicken curry", FactsAssessed = true, ContainsClasses = [IngredientClass.Poultry] }],
            Instructions = "Chicken on Monday"
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        chatClient.Calls.Should().Be(2);
        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.DishName.Should().Be("Chicken curry");
        suggestion.AttendeeIds.Should().BeEquivalentTo(new[] { omnivoreId, vegetarianId });
        suggestion.DietWarning.Should().Contain("Poultry").And.Contain("Naomi");
    }

    [Fact]
    public async Task Suggest_ModelDisplacesExplicitFridayChicken_RestoresKnownChickenRecipe()
    {
        var friday = WeekStart.AddDays(4);
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                $$"""{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"friday-chicken","dates":["{{friday:yyyy-MM-dd}}"],"mealType":"dinner","course":"main","sourceHost":null,"count":1,"distinct":true,"dishRequest":"chicken","requiredClasses":["Poultry"],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":true}]}""")),
            new(new ChatMessage(ChatRole.Assistant,
                $$"""{"suggestions":[{"date":"{{friday:yyyy-MM-dd}}","dishName":"Vegetable curry","attendeeIds":[]}]}""")),
            new(new ChatMessage(ChatRole.Assistant,
                $$"""{"suggestions":[{"date":"{{friday:yyyy-MM-dd}}","dishName":"Vegetable curry","attendeeIds":[]}]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());
        var request = Request(daysToFill: [friday]) with
        {
            Instructions = "Get one with chicken for Friday.",
            KnownRecipes =
            [
                new RecipeOption { Id = Guid.NewGuid(), Title = "Chicken curry", FactsAssessed = true, ContainsClasses = [IngredientClass.Poultry] }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        chatClient.Calls.Should().BeGreaterThan(1);
        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.Date.Should().Be(friday);
        suggestion.DishName.Should().Be("Chicken curry");
        suggestion.AttendeeIds.Should().HaveCount(request.Members.Count);
    }

    [Fact]
    public async Task Suggest_CustomEditablePrompt_ReplacesDefaultButKeepsProtectedRules()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Stoofvlees"}]}""");
        const string custom = "You are a Flemish chef planning hearty weekday dinners.";

        await CreateService(chatClient, promptOverride: custom)
            .SuggestAsync(Request(daysToFill: [WeekStart]));

        var systemText = chatClient.LastMessages[0].Text!;
        systemText.Should().Contain(custom);
        systemText.Should().NotContain("You are a meal planner for a family household");
        // The machinery is not editable: JSON contract and allergy rule always appended
        systemText.Should().Contain("Reply with ONLY a JSON object");
        systemText.Should().Contain("NEVER suggest dishes that conflict");
    }

    [Fact]
    public async Task Suggest_Instructions_AreIncludedInThePrompt()
    {
        var chatClient = new FakeChatClient("""{"suggestions":[]}""");

        await CreateService(chatClient).SuggestAsync(
            Request(instructions: "3 days vegetarian, at least one fish dish"));

        var userPrompt = chatClient.LastMessages.Single(m => m.Role == ChatRole.User).Text;
        userPrompt.Should().Contain("3 days vegetarian, at least one fish dish");
    }

    [Fact]
    public async Task Suggest_MultipleDishesOnOneDay_AreAllKept()
    {
        // Two small leftovers proposed for the same dinner (see freezer rule in the prompt)
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Leftover chili","recipeTitle":null,"reason":"Small portion"},
              {"date":"2026-06-15","dishName":"Leftover soup","recipeTitle":null,"reason":"Completes the dinner"}
            ]}
            """);

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        suggestions.Should().HaveCount(2);
        suggestions.Select(s => s.Date).Should().AllBeEquivalentTo(WeekStart);
    }

    [Fact]
    public void BuildUserPrompt_CollectionConstraints_GetTheirOwnBlock()
    {
        var request = Request() with
        {
            CollectionConstraints =
            [
                new CollectionConstraint
                {
                    Name = "Easy Weekday Dishes",
                    RecipeTitles = ["Wrap", "Pasta pesto"],
                    Dates = [WeekStart.AddDays(4)]
                },
                new CollectionConstraint
                {
                    Name = "Comfort Food",
                    RecipeTitles = ["Stew"],
                    Dates = [] // referenced from the global instructions
                }
            ]
        };

        var prompt = MealSuggestionPromptBuilder.BuildUserPrompt(request);

        prompt.Should().Contain("Referenced collections:");
        prompt.Should().Contain("\"Easy Weekday Dishes\" (for 2026-06-19): \"Wrap\", \"Pasta pesto\"");
        prompt.Should().Contain("\"Comfort Food\" (general instructions): \"Stew\"");
    }

    [Fact]
    public async Task Suggest_OffListPickOnConstrainedDay_IsKept()
    {
        // Enforcement is soft: an off-list dish is logged but never dropped —
        // the review dialog lets the user discard it, an empty day would be worse
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Not in collection","recipeTitle":null,"reason":null}
            ]}
            """);

        var request = Request(daysToFill: [WeekStart]) with
        {
            CollectionConstraints =
            [
                new CollectionConstraint { Name = "X", RecipeTitles = ["Wrap"], Dates = [WeekStart] }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle().Which.DishName.Should().Be("Not in collection");
    }

    [Fact]
    public async Task Suggest_DuplicateDishOnSameDay_IsDeduplicated()
    {
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Spaghetti","recipeTitle":null,"reason":null},
              {"date":"2026-06-15","dishName":"spaghetti","recipeTitle":null,"reason":null}
            ]}
            """);

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        suggestions.Should().ContainSingle();
    }

    [Fact]
    public async Task Suggest_RepeatedDishAcrossDays_IsRemovedAndMissingDayBackfilled()
    {
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Same curry"},
              {"date":"2026-06-16","dishName":"Same curry"}
            ]}
            """)),
            new(new ChatMessage(ChatRole.Assistant, """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Same curry"},
              {"date":"2026-06-16","dishName":"Same curry"}
            ]}
            """))]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Tacos" }]));

        chatClient.Calls.Should().Be(2);
        suggestions.Select(item => item.DishName).Should().BeEquivalentTo("Same curry", "Tacos");
        suggestions.Select(item => item.DishName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Suggest_ExplicitRepeatInstruction_AllowsDishOnMultipleDays()
    {
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":true,"constraints":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Same curry"},
              {"date":"2026-06-16","dishName":"Same curry"}
            ]}
            """))]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            instructions: "Serve the same curry every day"));

        chatClient.Calls.Should().Be(2);
        suggestions.Should().HaveCount(2).And.OnlyContain(item => item.DishName == "Same curry");
    }

    [Fact]
    public void ParsePayload_StripsThinkBlock_AndIgnoresBracesInside()
    {
        var payload = MealSuggestionResponseContract.Parse(
            "<think>I could use {curry} here</think>\n```json\n{\"suggestions\":[{\"date\":\"2026-06-15\",\"dishName\":\"Curry\"}]}\n```");

        payload!.Suggestions.Should().ContainSingle().Which.DishName.Should().Be("Curry");
    }

    [Fact]
    public void ParsePayload_AcceptsBareArray()
    {
        var payload = MealSuggestionResponseContract.Parse(
            """[{"date":"2026-06-15","dishName":"Soup"}]""");

        payload!.Suggestions.Should().ContainSingle().Which.DishName.Should().Be("Soup");
    }

    [Fact]
    public async Task Suggest_UnparseableThenValid_RetriesOnceAndSucceeds()
    {
        var responses = new Queue<ChatResponse>(
        [
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "sorry, here goes nothing")),
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"suggestions":[{"date":"2026-06-15","dishName":"Second try"}]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        chatClient.Calls.Should().Be(2);
        suggestions.Should().ContainSingle().Which.DishName.Should().Be("Second try");
    }

    [Fact]
    public async Task Suggest_TruncatedResponse_TriggersRetry()
    {
        var responses = new Queue<ChatResponse>(
        [
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"suggestions\":[{\"date")) { FinishReason = ChatFinishReason.Length },
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"suggestions":[{"date":"2026-06-15","dishName":"Recovered"}]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        chatClient.Calls.Should().Be(2);
        suggestions.Should().ContainSingle().Which.DishName.Should().Be("Recovered");
    }

    [Fact]
    public async Task Suggest_PartialFill_BackfillsMissingDaysFromRules()
    {
        // Model answers for the first day only; the second is filled from the rules,
        // and that backfilled row is marked as fallback-sourced
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"AI dish"}]}""");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Fallback dish" }]));

        suggestions.Should().HaveCount(2);
        suggestions.Should().ContainSingle(s =>
            s.Date == WeekStart && s.DishName == "AI dish" && s.Source == MealSuggestionSource.Ai);
        suggestions.Should().ContainSingle(s =>
            s.Date == WeekStart.AddDays(1) && s.Source == MealSuggestionSource.RulesFallback);
    }

    [Fact]
    public async Task Suggest_BackfillDoesNotDuplicateAiPickedDish()
    {
        // The model fills day one with the household's only favorite; the rules backfill
        // for day two must not re-suggest that same dish just because it's the only
        // candidate — it should come up empty rather than duplicate it within the week.
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Only favorite"}]}""");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Only favorite" }]));

        suggestions.Should().ContainSingle();
        suggestions[0].Date.Should().Be(WeekStart);
        suggestions[0].DishName.Should().Be("Only favorite");
    }

    [Fact]
    public async Task Suggest_RepeatedFreezerDishWithoutExplicitRepeat_IsKeptOnce()
    {
        // One lasagna in the freezer; an unsolicited duplicate on another day is removed.
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Frozen lasagna"},
              {"date":"2026-06-16","dishName":"Frozen lasagna"}
            ]}
            """);

        var request = Request(daysToFill: [WeekStart, WeekStart.AddDays(1)]) with
        {
            AvailableFrozenItems =
            [
                new FrozenItem { Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 1 }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle(s =>
            s.FreezyItemRef == "lasagna-1" && s.FreezyItemQuantity == 1);
    }

    [Fact]
    public async Task Suggest_FreezerItemId_LinksExactlyAndOverridesDishNameToTheItemsRealName()
    {
        // The model paraphrases the item in dishName instead of reproducing it verbatim
        // (the whole point of the id — it no longer has to), but supplies the exact id.
        // Linking must succeed off the id alone, and the app's real name replaces the
        // model's paraphrase so it matches what Freezy (and the planner) actually track.
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"leftover lasagna, serves 2","freezerItemId":"lasagna-1"}]}""");

        var request = Request(daysToFill: [WeekStart]) with
        {
            AvailableFrozenItems =
            [
                new FrozenItem { Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 1 }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].FreezyItemRef.Should().Be("lasagna-1");
        suggestions[0].FreezyItemQuantity.Should().Be(1);
        suggestions[0].DishName.Should().Be("Frozen lasagna");
    }

    [Fact]
    public async Task Suggest_MultipleFreezerItemIdsForOneDinner_LinkIndependently()
    {
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","mealType":"dinner","course":"main","dishName":"stew","freezerItemId":"stew-1"},
              {"date":"2026-06-15","mealType":"dinner","course":"side","dishName":"bread","freezerItemId":"bread-1"}
            ]}
            """);

        var request = Request(daysToFill: [WeekStart]) with
        {
            AvailableFrozenItems =
            [
                new FrozenItem { Id = "stew-1", Name = "Beef stew", Quantity = 1 },
                new FrozenItem { Id = "bread-1", Name = "Garlic bread", Quantity = 1 }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().HaveCount(2);
        suggestions.Should().ContainSingle(s =>
            s.Date == WeekStart && s.Course == Course.Main
            && s.DishName == "Beef stew" && s.FreezyItemRef == "stew-1"
            && s.FreezyItemQuantity == 1);
        suggestions.Should().ContainSingle(s =>
            s.Date == WeekStart && s.Course == Course.Side
            && s.DishName == "Garlic bread" && s.FreezyItemRef == "bread-1"
            && s.FreezyItemQuantity == 1);
    }

    [Fact]
    public async Task Suggest_ExplicitRepeatedFreezerItem_IsCappedWithoutDroppingTheSecondDish()
    {
        // Same id proposed for two days but only one unit available: the second day
        // must lose the freezer link (so stock isn't double-reserved) while remaining
        // a valid suggestion rather than disappearing.
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":true,"constraints":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"lasagna","freezerItemId":"lasagna-1"},
              {"date":"2026-06-16","dishName":"lasagna again","freezerItemId":"lasagna-1"}
            ]}
            """))]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var request = Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            instructions: "Serve the same frozen lasagna both days") with
        {
            AvailableFrozenItems =
            [
                new FrozenItem { Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 1 }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().HaveCount(2);
        suggestions.Should().ContainSingle(s => s.FreezyItemRef == "lasagna-1" && s.FreezyItemQuantity == 1);
        suggestions.Should().ContainSingle(s => s.FreezyItemRef == null && s.FreezyItemQuantity == 0);
    }

    [Fact]
    public async Task Suggest_UnknownFreezerItemId_IsIgnored_DishKeptUnlinked()
    {
        // A hallucinated or stale id (e.g. the item got reserved elsewhere between
        // prompt build and reply) must not crash or silently vanish the dish — it just
        // stays unlinked, with the model's own dishName since there's no item to
        // resolve a real name from.
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Mystery freezer meal","freezerItemId":"does-not-exist"}]}""");

        var request = Request(daysToFill: [WeekStart]) with
        {
            AvailableFrozenItems =
            [
                new FrozenItem { Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 1 }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].FreezyItemRef.Should().BeNull();
        suggestions[0].DishName.Should().Be("Mystery freezer meal");
    }

    [Fact]
    public async Task Suggest_FreezerStockReservedByAi_IsNotReusedByBackfill()
    {
        // The model fills one day with the only lasagna; the unfilled day is backfilled
        // from the rules, which must NOT slot the same (now reserved) lasagna again.
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Frozen lasagna"}]}""");

        var request = Request(
            daysToFill: [WeekStart, WeekStart.AddDays(1)],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Backup dish" }]) with
        {
            AvailableFrozenItems =
            [
                new FrozenItem
                {
                    Id = "lasagna-1", Name = "Frozen lasagna", Quantity = 1,
                    ExpirationDate = WeekStart.AddDays(3).ToDateTime(TimeOnly.MinValue)
                }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().HaveCount(2);
        suggestions.Count(s => s.FreezyItemRef == "lasagna-1").Should().Be(1);
        suggestions.Should().ContainSingle(s => s.Date == WeekStart.AddDays(1) && s.DishName == "Backup dish");
    }

    [Fact]
    public async Task Suggest_LegacyModelSourceUrl_OnSourceConstrainedDay_IsRejected()
    {
        var chatClient = new FakeChatClient(
            """
            {"suggestions":[
              {"date":"2026-06-15","dishName":"Vegetarische lasagne","recipeTitle":null,
               "sourceUrl":"https://dagelijksekost.vrt.be/recepten/lasagne","reason":"From the site"}
            ]}
            """);

        var request = Request(daysToFill: [WeekStart]) with
        {
            SourceConstraints =
            [
                new SourceConstraint { Name = "Dagelijkse Kost", Host = "dagelijksekost.vrt.be", Dates = [WeekStart] }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().BeEmpty();
    }

    [Fact]
    public async Task PostProcess_ExternalCandidateId_ResolvesVerifiedSource()
    {
        const string url = "https://dagelijksekost.vrt.be/recepten/vegetarische-lasagne";
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(url, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecipePreview(
                true,
                new ImportedRecipe
                {
                    Title = "Vegetarische lasagne",
                    SourceUrl = url,
                    IngredientLines = ["1 onion"]
                },
                null,
                null)));
        var webSearch = ConfiguredWebSearch();
        webSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>(
                [new WebSearchResult("Vegetarische lasagne", url, null)]));
        var tools = new ExternalRecipeTools(
            webSearch, importService, 5, "dagelijksekost.vrt.be",
            ["dagelijksekost.vrt.be"], "test", NullLogger.Instance);
        var candidateId = (await tools.SearchRecipesAsync("lasagne", null)).Single().CandidateId;
        await tools.GetRecipeAsync(candidateId);

        var request = Request(daysToFill: [WeekStart]) with
        {
            Members =
            [
                new MemberProfile
                {
                    Name = "Alex",
                    Allergies = [new DietaryTagProfile { Name = "onion" }]
                }
            ],
            SourceConstraints =
            [
                new SourceConstraint
                {
                    Name = "Dagelijkse Kost",
                    Host = "dagelijksekost.vrt.be",
                    Dates = [WeekStart]
                }
            ]
        };
        var payload = MealSuggestionResponseContract.Parse(
            $$"""{"suggestions":[{"date":"2026-06-15","dishName":"Vegetarische lasagne","externalCandidateId":"{{candidateId}}"}]}""")!;

        var suggestions = new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance)
            .Process(payload, request, tools);

        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.SourceUrl.Should().Be(url);
        suggestion.SourceName.Should().Be("Dagelijkse Kost");
        suggestion.ExternalIngredients.Should().ContainSingle("1 onion");
    }

    [Fact]
    public async Task ExternalResearch_CollectionPage_IsRejectedAsRecipeCandidate()
    {
        const string url = "https://www.laurasbakery.nl/category/zoet-bakken/zonder-oven/";
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(url, Arg.Any<CancellationToken>())
            .Returns(new RecipePreview(
                true,
                new ImportedRecipe { Title = "106+ recepten zonder oven", SourceUrl = url },
                null,
                null));
        var webSearch = ConfiguredWebSearch();
        webSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>(
                [new WebSearchResult("Desserts", url, null)]));
        var tools = new ExternalRecipeTools(
            webSearch, importService, 5, "laurasbakery.nl",
            ["laurasbakery.nl"], "test", NullLogger.Instance);
        var candidateId = (await tools.SearchRecipesAsync("dessert", null)).Single().CandidateId;

        var result = await tools.GetRecipeAsync(candidateId);

        result.Error.Should().Contain("structurally verified");
        tools.TryResolveCandidate(candidateId, out _).Should().BeFalse();
    }

    [Fact]
    public void PostProcess_ExplicitDessertAndAttendees_ArePreserved()
    {
        var alex = Guid.NewGuid();
        var naomi = Guid.NewGuid();
        var request = Request(daysToFill: [WeekStart]) with
        {
            Members =
            [
                new MemberProfile { Id = alex, Name = "Alex" },
                new MemberProfile { Id = naomi, Name = "Naomi" }
            ]
        };
        var payload = MealSuggestionResponseContract.Parse(
            $$"""{"suggestions":[{"date":"2026-06-15","mealType":"dinner","course":"dessert","attendeeIds":["{{alex}}"],"dishName":"Fruit tart"}]}""")!;

        var suggestion = new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance)
            .Process(payload, request, null)
            .Should().ContainSingle().Subject;

        suggestion.MealType.Should().Be(MealType.Dinner);
        suggestion.Course.Should().Be(Course.Dessert);
        suggestion.AttendeeIds.Should().Equal(alex);
    }

    [Fact]
    public async Task Suggest_ResearchIntentPass_ResolvesCandidateIdWithoutTrustingModelUrl()
    {
        const string url = "https://dagelijksekost.vrt.be/gerechten/vegetarische-lasagne";
        var responses = new Queue<ChatResponse>(
        [
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"source-main","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":"dagelijksekost.vrt.be","count":1,"distinct":true,"searchQuery":"vegetarische lasagne","requiredClasses":[],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""")),
            new ChatResponse(new ChatMessage(ChatRole.Assistant,
                """{"suggestions":[{"date":"2026-06-15","dishName":"model title","recipeTitle":null,"externalCandidateId":"c1","reason":"verified","constraintIds":["source-main"]}]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());
        var webSearch = ConfiguredWebSearch();
        webSearch.SearchAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<WebSearchResult>>(
                [new WebSearchResult("Vegetarische lasagne", url, null)]));
        var importService = Substitute.For<IRecipeImportService>();
        importService.PreviewAsync(url, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new RecipePreview(
                true,
                new ImportedRecipe
                {
                    Title = "Vegetarische lasagne",
                    SourceUrl = url,
                    IngredientLines = ["1 onion"]
                },
                null,
                null)));
        var request = Request(daysToFill: [WeekStart]) with
        {
            Members =
            [
                new MemberProfile
                {
                    Name = "Alex",
                    Allergies = [new DietaryTagProfile { Name = "onion" }]
                }
            ],
            SourceConstraints =
            [
                new SourceConstraint
                {
                    Name = "Dagelijkse Kost",
                    Host = "dagelijksekost.vrt.be",
                    Dates = [WeekStart]
                }
            ]
        };

        var logger = new CapturingLogger<LlmMealSuggestionService>();
        var suggestions = await CreateService(
            chatClient,
            webSearch,
            logger: logger,
            importService: importService).SuggestAsync(request);

        var suggestion = suggestions.Should().ContainSingle(
            because: string.Join(" | ", logger.Entries.Select(entry => entry.Message))).Subject;
        suggestion.DishName.Should().Be("Vegetarische lasagne");
        suggestion.SourceUrl.Should().Be(url);
        suggestion.SourceName.Should().Be("Dagelijkse Kost");
        suggestion.AllergyWarning.Should().Contain("not been verified");
        chatClient.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Suggest_InvalidLegacySourceUrl_IsNeverTrustedAsImportSource()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Something","sourceUrl":"not-a-url"}]}""");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.SourceUrl.Should().BeNull();
    }

    [Fact]
    public async Task Suggest_PlainInstructionsWithoutSourceMention_DoesNotAttachExternalTools()
    {
        // Regression: instructions text alone ("3 days vegetarian") must never trigger the
        // agentic web-search path — only an explicit @[Source] reference may (SourceConstraints).
        // A prior version gated on "any non-empty instructions", so the model went searching
        // the web even when the planner never asked it to.
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"source-main","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":"dagelijksekost.vrt.be","count":1,"distinct":true,"searchQuery":"recipe","requiredClasses":[],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        await CreateService(chatClient, ConfiguredWebSearch()).SuggestAsync(
            Request(instructions: "3 days vegetarian, at least one fish dish"));

        chatClient.LastOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Suggest_SourceMentionPresent_UsesSeparateResearchIntentPass()
    {
        var responses = new Queue<ChatResponse>([
            new(new ChatMessage(ChatRole.Assistant,
                """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"source-main","dates":["2026-06-15"],"mealType":"dinner","course":"main","sourceHost":"dagelijksekost.vrt.be","count":1,"distinct":true,"searchQuery":"recipe","requiredClasses":[],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}""")),
            new(new ChatMessage(ChatRole.Assistant, """{"suggestions":[]}"""))
        ]);
        var chatClient = new FakeChatClient(() => responses.Dequeue());

        var request = Request() with
        {
            SourceConstraints = [new SourceConstraint { Name = "Dagelijkse Kost", Host = "dagelijksekost.vrt.be", Dates = [WeekStart] }]
        };

        await CreateService(chatClient, ConfiguredWebSearch()).SuggestAsync(request);

        chatClient.Calls.Should().BeGreaterThan(1);
        chatClient.LastOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Suggest_UnassessedRecipeWithAllergy_IsFlaggedWithoutLanguageHeuristic()
    {
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Peanut stew","recipeTitle":"Peanut stew"}]}""");

        var request = Request(daysToFill: [WeekStart],
            recipes: [new RecipeOption { Id = recipeId, Title = "Peanut stew" }]) with
        {
            Members = [new MemberProfile { Name = "Kid", Allergies = [new DietaryTagProfile { Name = "peanut" }] }],
            RecipeAllergens = new Dictionary<Guid, RecipeAllergenInfo>
            {
                [recipeId] = new() { Ingredients = ["peanut butter", "onion"] }
            }
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].AllergyWarning.Should().NotBeNull().And.Subject.Should().Contain("not been verified");
    }

    [Fact]
    public async Task Suggest_AssessedRecipe_ConflictsAreFlaggedByExactClassMatch()
    {
        // Exact facts beat the substring heuristic: no ingredient name contains the
        // literal tag "Noten" ("hazelnootpasta" doesn't), so the legacy heuristic
        // would stay silent — the assessed TreeNuts fact still hits
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Choco cake","recipeTitle":"Choco cake"}]}""");

        var request = Request(daysToFill: [WeekStart],
            recipes:
            [
                new RecipeOption
                {
                    Id = recipeId, Title = "Choco cake",
                    FactsAssessed = true,
                    ContainsClasses = [IngredientClass.TreeNuts, IngredientClass.Milk]
                }
            ]) with
        {
            Members =
            [
                new MemberProfile
                {
                    Name = "Kid",
                    Allergies = [new DietaryTagProfile { Name = "Noten", ExcludedClasses = [IngredientClass.TreeNuts] }]
                }
            ],
            // No literal ingredient match — proves the exact tier fired, not the heuristic
            RecipeAllergens = new Dictionary<Guid, RecipeAllergenInfo>
            {
                [recipeId] = new() { Ingredients = ["hazelnootpasta", "bloem"] }
            }
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].AllergyWarning.Should().Contain("TreeNuts").And.Contain("Noten");
    }

    [Fact]
    public async Task Suggest_AssessedRecipe_DietConflictGetsDietWarningNotAllergyWarning()
    {
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Beef stew","recipeTitle":"Beef stew"}]}""");

        var request = Request(daysToFill: [WeekStart],
            recipes:
            [
                new RecipeOption
                {
                    Id = recipeId, Title = "Beef stew",
                    FactsAssessed = true,
                    ContainsClasses = [IngredientClass.RedMeat]
                }
            ]) with
        {
            Members =
            [
                new MemberProfile
                {
                    Name = "Anna",
                    Diets = [new DietaryTagProfile { Name = "Vegetarisch", ExcludedClasses = [IngredientClass.RedMeat, IngredientClass.Fish] }]
                }
            ]
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].DietWarning.Should().Contain("RedMeat").And.Contain("Vegetarisch");
        suggestions[0].AllergyWarning.Should().BeNull();
    }

    [Fact]
    public async Task Suggest_FullRulesFallback_StillFlagsAllergyConflicts()
    {
        // Regression for the coverage hole: the exception path used to return raw
        // rules results without ever running the allergy net
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(() => throw new HttpRequestException("connection refused"));

        var request = Request(daysToFill: [WeekStart],
            recipes: [new RecipeOption { Id = recipeId, Title = "Peanut stew" }],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Peanut stew" }]) with
        {
            Members = [new MemberProfile { Name = "Kid", Allergies = [new DietaryTagProfile { Name = "peanut" }] }],
            RecipeAllergens = new Dictionary<Guid, RecipeAllergenInfo>
            {
                [recipeId] = new() { Ingredients = ["peanut butter"] }
            }
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.Source.Should().Be(MealSuggestionSource.RulesFallback);
        suggestion.AllergyWarning.Should().NotBeNull();
    }

    [Fact]
    public async Task Suggest_RulesBackfilledDays_AreAlsoFlagged()
    {
        // Model answers one of two days; the second is backfilled from rules and must
        // pass through the same constraint net as the AI rows
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Safe soup"}]}""");

        var request = Request(daysToFill: [WeekStart, WeekStart.AddDays(1)],
            recipes: [new RecipeOption { Id = recipeId, Title = "Peanut stew" }],
            favorites: [new FavoriteDish { MemberName = "Anna", DishName = "Peanut stew" }]) with
        {
            Members = [new MemberProfile { Name = "Kid", Allergies = [new DietaryTagProfile { Name = "peanut" }] }],
            RecipeAllergens = new Dictionary<Guid, RecipeAllergenInfo>
            {
                [recipeId] = new() { Ingredients = ["peanut butter"] }
            }
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().HaveCount(2);
        var backfilled = suggestions.Single(s => s.Source == MealSuggestionSource.RulesFallback);
        backfilled.AllergyWarning.Should().NotBeNull();
    }

    [Fact]
    public void BuildUserPrompt_AllergyExcludedRecipes_AreOmittedFromKnownRecipesBlock()
    {
        var excludedId = Guid.NewGuid();
        var request = Request(recipes:
        [
            new RecipeOption { Id = excludedId, Title = "Peanut stew" },
            new RecipeOption { Id = Guid.NewGuid(), Title = "Safe soup" }
        ]) with
        {
            AllergyExcludedRecipeIds = new HashSet<Guid> { excludedId }
        };

        var prompt = MealSuggestionPromptBuilder.BuildUserPrompt(request);

        prompt.Should().NotContain("Peanut stew");
        prompt.Should().Contain("Safe soup");
    }

    [Fact]
    public void BuildUserPrompt_AssessedRecipes_CarryContainsAnnotations()
    {
        var request = Request(recipes:
        [
            new RecipeOption
            {
                Id = Guid.NewGuid(), Title = "Choco cake",
                FactsAssessed = true,
                ContainsClasses = [IngredientClass.Milk, IngredientClass.Gluten]
            },
            new RecipeOption
            {
                Id = Guid.NewGuid(), Title = "Fruit salad",
                FactsAssessed = true, ContainsClasses = []
            },
            new RecipeOption { Id = Guid.NewGuid(), Title = "Mystery dish" }
        ]);

        var prompt = MealSuggestionPromptBuilder.BuildUserPrompt(request);

        prompt.Should().Contain("\"Choco cake\" [contains: Milk, Gluten]");
        prompt.Should().Contain("\"Fruit salad\" [contains: none]");
        prompt.Should().Contain("\"Mystery dish\"").And.NotContain("\"Mystery dish\" [contains");
    }

    [Fact]
    public void BuildUserPrompt_MemberTags_CarryExcludesAnnotations()
    {
        var request = Request() with
        {
            Members =
            [
                new MemberProfile
                {
                    Name = "Anna",
                    Allergies = [new DietaryTagProfile { Name = "Noten", ExcludedClasses = [IngredientClass.TreeNuts] }],
                    Diets = [new DietaryTagProfile { Name = "Koosjer" }] // no classes: name only
                }
            ]
        };

        var prompt = MealSuggestionPromptBuilder.BuildUserPrompt(request);

        prompt.Should().Contain("Noten [excludes: TreeNuts]");
        prompt.Should().Contain("Koosjer").And.NotContain("Koosjer [excludes");
    }
}
