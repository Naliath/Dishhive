using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

public class LlmMealSuggestionServiceTests
{
    private static readonly DateOnly WeekStart = new(2026, 6, 15);

    /// <summary>Minimal IChatClient stub returning a canned (or failing) response</summary>
    private sealed class FakeChatClient : IChatClient
    {
        private readonly Func<ChatResponse> _respond;
        public int Calls { get; private set; }

        public FakeChatClient(string responseText)
            : this(() => new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))) { }

        public FakeChatClient(Func<ChatResponse> respond) => _respond = respond;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_respond());
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private static LlmMealSuggestionService CreateService(FakeChatClient chatClient)
        => new(chatClient, new RulesMealSuggestionService(), new AiOptions { Provider = "ollama", Model = "test" },
            NullLogger<LlmMealSuggestionService>.Instance);

    private static MealSuggestionRequest Request(
        IReadOnlyList<DateOnly>? daysToFill = null,
        IReadOnlyList<RecipeOption>? recipes = null,
        IReadOnlyList<FavoriteDish>? favorites = null) => new()
    {
        WeekStart = WeekStart,
        DaysToFill = daysToFill ?? [WeekStart, WeekStart.AddDays(1)],
        KnownRecipes = recipes ?? [],
        Favorites = favorites ?? []
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
}
