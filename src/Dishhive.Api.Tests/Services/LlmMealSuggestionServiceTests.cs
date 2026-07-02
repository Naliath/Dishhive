using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using FluentAssertions;
using Microsoft.Extensions.AI;
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

    private static LlmMealSuggestionService CreateService(
        FakeChatClient chatClient, IWebSearchClient? webSearch = null,
        AiResponseMode capabilityMode = AiResponseMode.PromptedJson)
        => new(chatClient, new RulesMealSuggestionService(), new AiOptions { Provider = "ollama", Model = "test" },
            webSearch ?? new NoOpWebSearchClient(), Substitute.For<IRecipeImportService>(), new WebSearchOptions(),
            new StubCapability(capabilityMode), NullLogger<LlmMealSuggestionService>.Instance);

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

        var prompt = LlmMealSuggestionService.BuildUserPrompt(request);

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
    public void ParsePayload_StripsThinkBlock_AndIgnoresBracesInside()
    {
        var payload = LlmMealSuggestionService.ParsePayload(
            "<think>I could use {curry} here</think>\n```json\n{\"suggestions\":[{\"date\":\"2026-06-15\",\"dishName\":\"Curry\"}]}\n```");

        payload!.Suggestions.Should().ContainSingle().Which.DishName.Should().Be("Curry");
    }

    [Fact]
    public void ParsePayload_AcceptsBareArray()
    {
        var payload = LlmMealSuggestionService.ParsePayload(
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
    public async Task Suggest_FreezerDishMatchingAvailableItem_IsLinkedAndCappedByQuantity()
    {
        // One lasagna in the freezer; the model proposes it on two days. Only the first
        // links to the freezer item (reserving the single unit); the second must not.
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

        suggestions.Should().HaveCount(2);
        suggestions.Should().ContainSingle(s => s.FreezyItemRef == "lasagna-1" && s.FreezyItemQuantity == 1);
        suggestions.Count(s => s.FreezyItemRef == "lasagna-1").Should().Be(1);
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
    public async Task Suggest_ExternalSourceUrl_IsMappedWithSourceName()
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

        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.SourceUrl.Should().Be("https://dagelijksekost.vrt.be/recepten/lasagne");
        suggestion.SourceName.Should().Be("Dagelijkse Kost");
        suggestion.RecipeId.Should().BeNull();
    }

    [Fact]
    public async Task Suggest_InvalidSourceUrl_IsIgnored()
    {
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Something","sourceUrl":"not-a-url"}]}""");

        var suggestions = await CreateService(chatClient).SuggestAsync(Request(daysToFill: [WeekStart]));

        var suggestion = suggestions.Should().ContainSingle().Subject;
        suggestion.SourceUrl.Should().BeNull();
        suggestion.DishName.Should().Be("Something");
    }

    [Fact]
    public async Task Suggest_PlainInstructionsWithoutSourceMention_DoesNotAttachExternalTools()
    {
        // Regression: instructions text alone ("3 days vegetarian") must never trigger the
        // agentic web-search path — only an explicit @[Source] reference may (SourceConstraints).
        // A prior version gated on "any non-empty instructions", so the model went searching
        // the web even when the planner never asked it to.
        var chatClient = new FakeChatClient("""{"suggestions":[]}""");

        await CreateService(chatClient, ConfiguredWebSearch()).SuggestAsync(
            Request(instructions: "3 days vegetarian, at least one fish dish"));

        chatClient.LastOptions!.Tools.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Suggest_SourceMentionPresent_AttachesExternalTools()
    {
        var chatClient = new FakeChatClient("""{"suggestions":[]}""");

        var request = Request() with
        {
            SourceConstraints = [new SourceConstraint { Name = "Dagelijkse Kost", Host = "dagelijksekost.vrt.be", Dates = [WeekStart] }]
        };

        await CreateService(chatClient, ConfiguredWebSearch()).SuggestAsync(request);

        chatClient.LastOptions!.Tools.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Suggest_LinkedRecipeWithAllergyIngredient_IsFlaggedNotDropped()
    {
        var recipeId = Guid.NewGuid();
        var chatClient = new FakeChatClient(
            """{"suggestions":[{"date":"2026-06-15","dishName":"Peanut stew","recipeTitle":"Peanut stew"}]}""");

        var request = Request(daysToFill: [WeekStart],
            recipes: [new RecipeOption { Id = recipeId, Title = "Peanut stew" }]) with
        {
            Members = [new MemberProfile { Name = "Kid", Allergies = ["peanut"] }],
            RecipeAllergens = new Dictionary<Guid, RecipeAllergenInfo>
            {
                [recipeId] = new() { Ingredients = ["peanut butter", "onion"] }
            }
        };

        var suggestions = await CreateService(chatClient).SuggestAsync(request);

        suggestions.Should().ContainSingle();
        suggestions[0].AllergyWarning.Should().NotBeNull().And.Subject.Should().Contain("peanut");
    }
}
