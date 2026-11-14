using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dishhive.Api.Tests.Crosscheck;

public class AiBudgetCrossTests
{
    [Fact]
    public async Task TranslationChunks_MustFitTheSharedExecutionGate()
    {
        var options = new AiOptions { Provider = "ollama", Model = "test" };
        var recipe = new Recipe
        {
            Title = "A long but valid recipe",
            Steps = Enumerable.Range(1, 16).Select(i => new RecipeStep
                { StepNumber = i, Instruction = new string('x', 1900) }).ToList()
        };
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(),
            Arg.Any<CancellationToken>()).Returns(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));
        var gate = new AiExecutionGate(client, options, new NoOpAiTaskMetricsStore(),
            NullLogger<AiExecutionGate>.Instance);
        gate.UpdateCapability(new AiModelTestResult { TestedAt = DateTimeOffset.UtcNow,
            Provider = "test", Model = "test", ResponseMode = AiResponseMode.JsonSchema });
        foreach (var chunk in LlmRecipeLocalizationService.BuildChunks(recipe, options.TranslationMaxPromptTokens))
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, LlmRecipeLocalizationService.SystemPrompt("Dutch")),
                new(ChatRole.User, AiJsonContract.Serialize(chunk))
            };
            for (var retry = 0; retry < options.MaxRetries; retry++)
                messages.Add(new(ChatRole.User, new string('c', 256)));
            await using var result = await gate.ExecuteAsync(new AiExecutionRequest(AiTaskKind.RecipeTranslation,
                messages, "test", ResponseFormat: LlmRecipeLocalizationService.JsonSchemaFormat));
            await result.ReportOutcomeAsync(AiTaskOutcome.Success);
        }
    }
}
