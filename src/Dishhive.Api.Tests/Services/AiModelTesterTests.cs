using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net;
using System.Text;

namespace Dishhive.Api.Tests.Services;

public class AiModelTesterTests
{
    private const string ModelName = "test-model";

    /// <summary>Chat stub whose reply can depend on the options (e.g. reject json_schema)</summary>
    private sealed class FakeChatClient(
        Func<ChatOptions?, ChatResponse> respond,
        bool supportsTools = true) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (messages.Any(message => message.Text.Contains(
                    "language-neutral JSON intent", StringComparison.Ordinal)))
            {
                var text = supportsTools
                    ? """{"version":1,"allowRepeatedDishes":false,"constraints":[{"id":"desserts","dates":["2099-01-05","2099-01-11"],"mealType":"dinner","course":"dessert","sourceHost":"recipes.example","count":2,"distinct":true,"searchQuery":"dessert","requiredClasses":[],"excludedClasses":[],"allAttendees":true,"overrideDietPreferences":false}]}"""
                    : "{}";
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, text)));
            }

            return Task.FromResult(respond(options));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }

    private static IHttpClientFactory HttpFactory(string modelsJson)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(new FakeHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(modelsJson, Encoding.UTF8, "application/json")
            })));
        return factory;
    }

    private static AiModelTester CreateTester(
        Func<ChatOptions?, ChatResponse> respond,
        string? modelsJson = null,
        bool supportsTools = true)
        => new(
            new FakeChatClient(respond, supportsTools),
            new AiOptions
            {
                Provider = "lmstudio", Model = ModelName,
                TimeoutSeconds = 10, MaxPromptTokens = 800
            },
            HttpFactory(modelsJson ?? $$"""{"data":[{"id":"{{ModelName}}"}]}"""),
            NullLogger<AiModelTester>.Instance);

    /// <summary>Runs the tester under the default composed production prompt</summary>
    private static Task<AiModelTestResult> RunAsync(AiModelTester tester)
        => tester.RunAsync(MealSuggestionPromptBuilder.ComposeSystemPrompt(null));

    /// <summary>A reply satisfying every evaluation constraint of the fixture:
    /// Wednesday from the collection, Chicken curry on Thursday, ≥2 vegetarian days.</summary>
    private static string CorrectReply()
    {
        var fixture = AiModelTester.CreateFixture(DateOnly.FromDateTime(DateTime.Today));
        var days = fixture.Request.DaysToFill;
        return $$"""
            {"suggestions":[
              {"date":"{{days[0]:yyyy-MM-dd}}","dishName":"Vegetable curry","recipeTitle":"Vegetable curry","externalCandidateId":null,"reason":"veg"},
              {"date":"{{days[1]:yyyy-MM-dd}}","dishName":"Mushroom risotto","recipeTitle":"Mushroom risotto","externalCandidateId":null,"reason":"veg"},
              {"date":"{{days[2]:yyyy-MM-dd}}","dishName":"Pasta pesto","recipeTitle":"Pasta pesto","externalCandidateId":null,"reason":"collection"},
              {"date":"{{days[3]:yyyy-MM-dd}}","dishName":"Chicken curry","recipeTitle":"Chicken curry","externalCandidateId":null,"reason":"as instructed"},
              {"date":"{{days[4]:yyyy-MM-dd}}","dishName":"Beef stew","recipeTitle":"Beef stew","externalCandidateId":null,"reason":"variety"}
            ]}
            """;
    }

    [Fact]
    public async Task Run_SchemaCapableModelWithCorrectPicks_Passes()
    {
        var tester = CreateTester(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, CorrectReply())));

        var result = await RunAsync(tester);

        result.Verdict.Should().Be("passed");
        result.ResponseMode.Should().Be(AiResponseMode.JsonSchema); // first attempt already parsed
        result.EvaluationPassed.Should().BeTrue();
        result.ModelListed.Should().BeTrue();
        result.Checks.Should().OnlyContain(c => c.Passed);
    }

    [Fact]
    public async Task Run_SchemaRejected_FallsBackToPromptedJson()
    {
        // Provider rejects response_format (LM Studio-style); prompted JSON works
        var tester = CreateTester(options => options?.ResponseFormat != null
            ? throw new InvalidOperationException("response_format not supported")
            : new ChatResponse(new ChatMessage(ChatRole.Assistant, CorrectReply())));

        var result = await RunAsync(tester);

        result.ResponseMode.Should().Be(AiResponseMode.PromptedJson);
        result.Verdict.Should().Be("passed");
        result.EvaluationPassed.Should().BeTrue();
    }

    [Fact]
    public async Task Run_NoParseableJson_FailsAndSkipsEvaluation()
    {
        var tester = CreateTester(_ => new ChatResponse(
            new ChatMessage(ChatRole.Assistant, "I'm sorry, I can only answer in prose.")));

        var result = await RunAsync(tester);

        result.Verdict.Should().Be("failed");
        result.ResponseMode.Should().Be(AiResponseMode.None);
        result.Viable.Should().BeFalse();
        result.EvaluationPassed.Should().BeNull();
    }

    [Fact]
    public async Task Run_WrongPicks_ReportsFailedEvaluationChecks()
    {
        // Valid JSON but the instructions were ignored: Thursday isn't Chicken curry,
        // Wednesday isn't from the collection, only one vegetarian day
        var fixture = AiModelTester.CreateFixture(DateOnly.FromDateTime(DateTime.Today));
        var days = fixture.Request.DaysToFill;
        var reply = $$"""
            {"suggestions":[
              {"date":"{{days[0]:yyyy-MM-dd}}","dishName":"Beef stew","recipeTitle":null,"externalCandidateId":null,"reason":""},
              {"date":"{{days[1]:yyyy-MM-dd}}","dishName":"Pork schnitzel","recipeTitle":null,"externalCandidateId":null,"reason":""},
              {"date":"{{days[2]:yyyy-MM-dd}}","dishName":"Salmon teriyaki","recipeTitle":null,"externalCandidateId":null,"reason":""},
              {"date":"{{days[3]:yyyy-MM-dd}}","dishName":"Falafel wraps","recipeTitle":null,"externalCandidateId":null,"reason":""},
              {"date":"{{days[4]:yyyy-MM-dd}}","dishName":"Meatball spaghetti","recipeTitle":null,"externalCandidateId":null,"reason":""}
            ]}
            """;
        var tester = CreateTester(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

        var result = await RunAsync(tester);

        result.Viable.Should().BeTrue();
        result.EvaluationPassed.Should().BeFalse();
        result.Verdict.Should().Be("warnings");
        result.Checks.Should().Contain(c => c.Name == "Collection day" && !c.Passed);
        result.Checks.Should().Contain(c => c.Name == "Specific dish" && !c.Passed);
        result.Checks.Should().Contain(c => c.Name == "Vegetarian days" && !c.Passed);
        result.Checks.Should().Contain(c => c.Name == "All days filled" && c.Passed);
    }

    [Fact]
    public async Task Run_ModelCannotInterpretResearchIntent_ReportsExternalDiscoveryWarning()
    {
        var tester = CreateTester(
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, CorrectReply())),
            supportsTools: false);

        var result = await RunAsync(tester);

        result.Viable.Should().BeTrue();
        result.ToolCallingPassed.Should().BeFalse();
        result.Verdict.Should().Be("warnings");
        result.Checks.Should().Contain(c => c.Name == "Multilingual planning instructions" && !c.Passed);
    }

    [Fact]
    public async Task Run_ConfiguredModelMissingFromEndpoint_Warns()
    {
        var tester = CreateTester(
            _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, CorrectReply())),
            modelsJson: """{"data":[{"id":"some-other-model"}]}""");

        var result = await RunAsync(tester);

        result.ModelListed.Should().BeFalse();
        result.Verdict.Should().Be("warnings"); // works, but probably not the model the user thinks
        result.Checks.Should().Contain(c => c.Name == "Model available" && !c.Passed);
    }

    [Fact]
    public void CreateFixture_PadsPromptToTheTokenBudget()
    {
        // The fixture must exercise the real context size: the built prompt should sit
        // at (not far under) the configured budget, and the real recipes must survive
        var fixture = AiModelTester.CreateFixture(new DateOnly(2026, 7, 2));
        var prompt = MealSuggestionPromptBuilder.BuildUserPrompt(fixture.Request, maxPromptTokens: 800);

        prompt.Length.Should().BeGreaterThan(800 * 4 - 400); // within a line of the char budget
        prompt.Should().Contain("Pasta pesto").And.Contain("Chicken curry");
        prompt.Should().Contain("Quick Pasta");
    }
}
