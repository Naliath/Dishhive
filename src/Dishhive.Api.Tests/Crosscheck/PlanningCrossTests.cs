using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Freezy;
using Dishhive.Api.Services.Import;
using Dishhive.Api.Services.Suggestions;
using Dishhive.Api.Services.WebSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Crosscheck;

public class PlanningCrossTests
{
    private static readonly DateOnly Day = new(2026, 10, 5);

    internal static LlmMealSuggestionService FailingPlanner()
    {
        var options = new AiOptions { Provider = "ollama", Model = "test" };
        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ChatResponse>>(_ => throw new HttpRequestException("simulated provider outage"));
        var capability = Substitute.For<IAiModelCapabilityService>();
        capability.EnsureTestedAsync(Arg.Any<CancellationToken>()).Returns(new AiModelTestResult
        {
            Provider = "test", Model = "test", ResponseMode = AiResponseMode.PromptedJson,
            EvaluationPassed = true, PlanningIntentPassed = true,
            TestedAt = DateTimeOffset.UtcNow
        });
        return new LlmMealSuggestionService(
            new AiExecutionGate(chat, options, new NoOpAiTaskMetricsStore(), NullLogger<AiExecutionGate>.Instance),
            new RulesMealSuggestionService(), options, capability, new Prompt(),
            new ExternalRecipeSessionFactory(new NoOpWebSearchClient(),
                Substitute.For<IRecipeImportService>(), new WebSearchOptions()),
            new MealSuggestionPostProcessor(NullLogger<MealSuggestionPostProcessor>.Instance),
            NullLogger<LlmMealSuggestionService>.Instance);
    }

    private sealed class Prompt : IAiPromptProvider
    {
        public Task<string?> GetOverrideAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task IntentProviderFailure_MustNotReturnCompleteFreezerPlanForLocalOnlyRequest()
    {
        var planner = FailingPlanner();
        var suggestions = await planner.SuggestAsync(new MealSuggestionRequest
        {
            WeekStart = Day, DaysToFill = [Day],
            Instructions = "Use only recipes already saved in Dishhive. Do not use freezer or free-text dishes.",
            AvailableFrozenItems = [new FrozenItem
            {
                Id = "freezer", Name = "Freezer soup", Quantity = 1,
                ExpirationDate = Day.ToDateTime(TimeOnly.MinValue)
            }]
        });
        Assert.True(suggestions.Count == 0 && planner.LastResult!.Status == PlanCompletionStatus.Unable,
            $"Actual: {planner.LastResult!.Status}; {string.Join(',', suggestions.Select(s => s.DishName))}");
    }

    [Fact]
    public void FinalValidation_MustRejectClaimOnWrongMealAndCourse()
    {
        var intent = new PlanningIntent { Constraints = [new PlanningConstraint
        {
            Id = "monday-dinner", Dates = [Day], MealType = MealType.Dinner, Course = Course.Main
        }] };
        var final = PlanningIntentValidator.ValidateFinal([new MealSuggestion
        {
            Date = Day, DishName = "Pudding", MealType = MealType.Lunch, Course = Course.Dessert,
            ConstraintIds = ["monday-dinner"]
        }], intent, new MealSuggestionRequest { DaysToFill = [Day] });
        Assert.True(final.Suggestions.Count == 0 && final.UnmetRequirements.Count > 0,
            $"Actual: {final.Suggestions.Count} accepted; {final.UnmetRequirements.Count} unmet requirements");
    }

    [Fact]
    public void FinalValidation_DessertAloneDoesNotFillRequestedDinnerMain()
    {
        var final = PlanningIntentValidator.ValidateFinal([new MealSuggestion
        {
            Date = Day, DishName = "Pudding", Course = Course.Dessert
        }], new PlanningIntent(), new MealSuggestionRequest { DaysToFill = [Day] });
        Assert.NotEmpty(final.UnmetRequirements);
    }
}
