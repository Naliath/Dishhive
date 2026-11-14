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
    [Theory]
    [InlineData(false, "timeout")]
    [InlineData(true, "cancelled")]
    public async Task ProviderCleanupError_AfterCancellationKeepsTheCancellationOutcome(bool callerCancels, string outcome)
    {
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(new BlockingChatClient { WrapCancellation = true },
            new AiOptions { Provider = "lmstudio", Model = "test", TimeoutSeconds = 30 }, metrics,
            NullLogger<AiExecutionGate>.Instance);
        using var cancel = new CancellationTokenSource();
        if (callerCancels) cancel.CancelAfter(200);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => gate.ExecuteAsync(new(
            AiTaskKind.RecipeFacts, [new(ChatRole.User, "data")], "facts",
            DeadlineUtc: callerCancels ? null : DateTime.UtcNow.AddMilliseconds(200)), cancel.Token));
        Assert.IsType<ObjectDisposedException>(error.InnerException);
        Assert.Equal(outcome, Assert.Single(metrics.Runs).Outcome);
        Assert.Equal(1, metrics.Runs[0].OperationAttempt);
    }
    [Fact]
    public async Task InputSnapshot_RecordsVersionsWithoutText_AndCapabilityProbeDoesNotInheritThem()
    {
        var client = new CapturingChatClient();
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(client, new AiOptions { Provider = "lmstudio", Model = "test" },
            metrics, NullLogger<AiExecutionGate>.Instance);
        using (AiInputSnapshotScope.ForRecipe(new Recipe { Id = Guid.NewGuid(), Title = "Private title", Revision = 7, FactsConfirmationVersion = 3 }))
        {
            await using var facts = await gate.ExecuteAsync(new(AiTaskKind.RecipeFacts, [new(ChatRole.User, "data")], "facts"));
            await facts.ReportOutcomeAsync(AiTaskOutcome.Success);
            await using var probe = await gate.ExecuteAsync(new(AiTaskKind.CapabilityPlanning, [new(ChatRole.User, "probe")], "probe", CapabilityProbe: true));
            await probe.ReportOutcomeAsync(AiTaskOutcome.Success);
        }
        Assert.Contains("\"revision\":7", metrics.Runs[0].InputRevisionsJson);
        Assert.Contains("\"confirmationVersion\":3", metrics.Runs[0].InputRevisionsJson);
        Assert.DoesNotContain("Private title", metrics.Runs[0].InputRevisionsJson);
        Assert.Equal(64, metrics.Runs[0].InputFingerprint!.Length);
        Assert.Null(metrics.Runs[1].InputRevisionsJson);
        Assert.Null(AiInputSnapshotScope.Current);
    }

    [Fact]
    public async Task ParallelInputScopes_KeepTheirOwnSnapshotsAndRestoreTheParent()
    {
        using var root = AiInputSnapshotScope.ForContent("root", "root", "private source");
        var snapshots = await Task.WhenAll(Enumerable.Range(0, 4).Select(async index =>
        {
            using var child = AiInputSnapshotScope.ForContent("child", index.ToString(), index);
            await Task.Yield();
            return child.RevisionsJson;
        }));
        for (var i = 0; i < snapshots.Length; i++) Assert.Contains($"\"id\":\"{i}\"", snapshots[i]);
        Assert.Same(root, AiInputSnapshotScope.Current);
        Assert.DoesNotContain("child", root.RevisionsJson);
    }
    private sealed class CapturingChatClient : IChatClient
    {
        public ChatOptions? Options { get; private set; }
        public int CallCount { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
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

    private sealed class BlockingChatClient : IChatClient
    {
        public bool WrapCancellation { get; init; }
        public async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (WrapCancellation) { throw new ObjectDisposedException("SDK response stream"); }
            throw new InvalidOperationException("The blocking client should always be cancelled.");
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
    public async Task FactsReasoningIsTaskSpecificAndItsActualSettingIsRecorded()
    {
        var client = new CapturingChatClient();
        var metrics = new CapturingMetricsStore();
        var options = new AiOptions { Provider = "lmstudio", Model = "test", DisableThinking = true,
            FactsEnableReasoning = true, FactsMaxOutputTokens = 2048 };
        var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);
        await using var facts = await gate.ExecuteAsync(new(AiTaskKind.RecipeFacts, [new(ChatRole.User, "ingredients")], "facts"));
        await facts.ReportOutcomeAsync(AiTaskOutcome.Success);
        Assert.Equal(ReasoningEffort.Low, client.Options!.Reasoning!.Effort);
        Assert.Equal(2048, client.Options.MaxOutputTokens);
        Assert.False(metrics.Runs[0].ReasoningDisabled);
        await using var translation = await gate.ExecuteAsync(new(AiTaskKind.RecipeTranslation, [new(ChatRole.User, "source")], "translation"));
        await translation.ReportOutcomeAsync(AiTaskOutcome.Success);
        Assert.Equal(ReasoningEffort.None, client.Options!.Reasoning!.Effort);
        Assert.True(metrics.Runs[1].ReasoningDisabled);
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
        await result.ReportOutcomeAsync(AiTaskOutcome.Success);

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

    [Fact]
    public async Task Result_AllowsExactlyOneTerminalOutcome()
    {
        var gate = new AiExecutionGate(
            new CapturingChatClient(),
            new AiOptions { Provider = "lmstudio", Model = "test" },
            new CapturingMetricsStore(),
            NullLogger<AiExecutionGate>.Instance);
        var result = await gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts, [new ChatMessage(ChatRole.User, "milk")], "facts-test"));

        await result.ReportOutcomeAsync(AiTaskOutcome.Success);
        var secondReport = () => result.ReportOutcomeAsync(AiTaskOutcome.ValidationFailure);

        await secondReport.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task InputOverTaskBudget_IsRejectedBeforeTransportAndRecorded()
    {
        var client = new CapturingChatClient();
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(client,
            new AiOptions
            {
                Provider = "lmstudio", Model = "test", FactsMaxPromptTokens = 1
            }, metrics, NullLogger<AiExecutionGate>.Instance);

        var execute = () => gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts, [new ChatMessage(ChatRole.User, "more than four characters")],
            "facts-test"));

        await execute.Should().ThrowAsync<AiInputBudgetExceededException>();
        client.CallCount.Should().Be(0);
        metrics.Runs.Should().ContainSingle().Which.Outcome.Should().Be("inputLimit");
    }

    [Fact]
    public async Task CallerCancellation_IsRecordedSeparatelyFromTimeout()
    {
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(new BlockingChatClient(),
            new AiOptions { Provider = "lmstudio", Model = "test", TimeoutSeconds = 30 },
            metrics, NullLogger<AiExecutionGate>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var execute = () => gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts, [new ChatMessage(ChatRole.User, "milk")], "facts-test"),
            cancellation.Token);

        await execute.Should().ThrowAsync<OperationCanceledException>();
        metrics.Runs.Should().ContainSingle().Which.Outcome.Should().Be("cancelled");
    }

    [Fact]
    public async Task TaskDeadline_IsRecordedAsTimeout()
    {
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(new BlockingChatClient(),
            new AiOptions { Provider = "lmstudio", Model = "test", TimeoutSeconds = 30 },
            metrics, NullLogger<AiExecutionGate>.Instance);

        var execute = () => gate.ExecuteAsync(new AiExecutionRequest(
            AiTaskKind.RecipeFacts, [new ChatMessage(ChatRole.User, "milk")], "facts-test",
            DeadlineUtc: DateTime.UtcNow.AddMilliseconds(20)));

        await execute.Should().ThrowAsync<OperationCanceledException>();
        metrics.Runs.Should().ContainSingle().Which.Outcome.Should().Be("timeout");
    }

    [Fact]
    public async Task AdmissionQueue_PrefersWaitingInteractiveWork()
    {
        var queue = new PriorityAdmissionQueue(1);
        using var active = await queue.EnterAsync(AiRequestPriority.Background, default);
        var background = queue.EnterAsync(AiRequestPriority.Background, default);
        var interactive = queue.EnterAsync(AiRequestPriority.Interactive, default);

        active.Dispose();
        using var interactiveLease = await interactive.WaitAsync(TimeSpan.FromSeconds(1));
        background.IsCompleted.Should().BeFalse();
        interactiveLease.Dispose();
        using var backgroundLease = await background.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task AdmissionQueue_BoundsWaitersAndReclaimsCancelledSlots()
    {
        var queue = new PriorityAdmissionQueue(1, 1);
        using var active = await queue.EnterAsync(AiRequestPriority.Background, default);
        using var cancellation = new CancellationTokenSource();
        var waiting = queue.EnterAsync(AiRequestPriority.Background, cancellation.Token);
        var overflow = () => queue.EnterAsync(AiRequestPriority.Interactive, default);
        await overflow.Should().ThrowAsync<InvalidOperationException>();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        var replacement = queue.EnterAsync(AiRequestPriority.Interactive, default);
        active.Dispose();
        using var admitted = await replacement.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ValidationScope_RecordsAbandonedWhenParsingThrows()
    {
        var metrics = new CapturingMetricsStore();
        var gate = new AiExecutionGate(new CapturingChatClient(),
            new AiOptions { Provider = "lmstudio", Model = "test" }, metrics, NullLogger<AiExecutionGate>.Instance);
        var validate = async () =>
        {
            await using var response = await gate.ExecuteAsync(new AiExecutionRequest(
                AiTaskKind.RecipeFacts, [new ChatMessage(ChatRole.User, "milk")], "test"));
            throw new InvalidOperationException("Interrupted validation");
        };
        await validate.Should().ThrowAsync<InvalidOperationException>();
        metrics.Runs.Should().ContainSingle().Which.Outcome.Should().Be("abandoned");
    }

    [Fact]
    public void OperationBudget_IsSharedAcrossChunksAndRetries()
    {
        using var operation = new AiOperationBudget(30, 2, default);
        operation.TakeAttempt();
        operation.TakeAttempt();
        var attempt = () => operation.TakeAttempt();
        attempt.Should().Throw<InvalidOperationException>();
    }
}
