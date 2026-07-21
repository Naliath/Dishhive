using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Facts;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

public class AiExecutionGateTests
{
    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? Options { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options = options;
            return Task.FromResult(new ChatResponse(
                new ChatMessage(ChatRole.Assistant, "{\"contains\":[]}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class CapturingMetricsStore : IAiTaskMetricsStore
    {
        public List<AiTaskRun> Runs { get; } = [];
        public Task SaveAsync(AiTaskRun run)
        {
            run.Id = Guid.NewGuid();
            Runs.Add(run);
            return Task.CompletedTask;
        }
        public Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null)
        {
            var run = Runs.Single(item => item.Id == runId);
            run.Outcome = outcome;
            run.Error = error;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Execute_FactsProfileAppliesDeterministicSettingsAndTelemetry()
    {
        var client = new CapturingChatClient();
        var metrics = new CapturingMetricsStore();
        var options = new AiOptions
        {
            Provider = "lmstudio",
            Model = "test",
            Temperature = 0.7,
            MaxOutputTokens = 12000,
            FactsMaxOutputTokens = 384,
            DisableThinking = true
        };
        var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);

        var result = await gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts,
            [new ChatMessage(ChatRole.User, "ingredients")],
            "facts-test",
            ResponseFormat: LlmRecipeFactsExtractor.JsonSchemaFormat));
        await result.ReportOutcomeAsync("success");

        client.Options!.Temperature.Should().Be(0);
        client.Options.MaxOutputTokens.Should().Be(384);
        client.Options.Reasoning.Should().NotBeNull();
        client.Options.ResponseFormat.Should().BeNull("schema mode has not been capability-verified yet");
        metrics.Runs.Should().ContainSingle();
        metrics.Runs[0].Task.Should().Be("recipe.facts");
        metrics.Runs[0].Outcome.Should().Be("success");
        metrics.Runs[0].ReasoningDisabled.Should().BeTrue();
    }

    [Fact]
    public async Task Execute_VerifiedSchemaCapabilityAppliesTaskSchema()
    {
        var client = new CapturingChatClient();
        var metrics = new CapturingMetricsStore();
        var options = new AiOptions { Provider = "lmstudio", Model = "test" };
        var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);
        gate.UpdateCapability(new AiModelTestResult
        {
            TestedAt = DateTimeOffset.UtcNow,
            Provider = options.Provider,
            Model = options.Model,
            ResponseMode = AiResponseMode.JsonSchema
        });

        await gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts,
            [new ChatMessage(ChatRole.User, "ingredients")],
            "facts-test",
            ResponseFormat: LlmRecipeFactsExtractor.JsonSchemaFormat));

        client.Options!.ResponseFormat.Should().NotBeNull();
        metrics.Runs[0].SchemaRequested.Should().BeTrue();
        metrics.Runs[0].SchemaApplied.Should().BeTrue();
    }

    [Fact]
    public void Profile_WeekPlanningSelectsTheCentralStandardOrExtendedTimeout()
    {
        var options = new AiOptions
        {
            Provider = "lmstudio",
            Model = "test",
            TimeoutSeconds = 45,
            AgentTimeoutSeconds = 360,
            MaxRetries = 2
        };
        var gate = new AiExecutionGate(
            new CapturingChatClient(), options, new CapturingMetricsStore(),
            NullLogger<AiExecutionGate>.Instance);

        gate.Profile(AiTaskKind.WeekPlanning).TimeoutSeconds.Should().Be(45);
        gate.Profile(AiTaskKind.WeekPlanning, useExtendedTimeout: true).TimeoutSeconds.Should().Be(360);
        gate.Profile(AiTaskKind.RecipeTranslation).MaxRetries.Should().Be(2);
    }
}
