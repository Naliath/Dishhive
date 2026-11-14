using Dishhive.Api.Models;
using Dishhive.Api.Services.Ai;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using System.ClientModel;
using System.Net;
using System.Text;

namespace Dishhive.Api.Tests.Services;

public class AiExecutionSemanticsTests
{
    private sealed class Client : IChatClient
    {
        public int Calls;
        public Func<CancellationToken, Task<ChatResponse>> Respond = _ => Task.FromResult(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) { Interlocked.Increment(ref Calls); return Respond(cancellationToken); }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class Metrics : IAiTaskMetricsStore
    {
        public List<AiTaskRun> Runs = [];
        public Task SaveAsync(AiTaskRun run) { run.Id = Guid.NewGuid(); lock (Runs) Runs.Add(run); return Task.CompletedTask; }
        public Task UpdateOutcomeAsync(Guid id, string outcome, string? error = null)
        { lock (Runs) { var run = Runs.Single(run => run.Id == id); run.Outcome = outcome; run.Error = error; } return Task.CompletedTask; }
    }

    private static AiExecutionGate Gate(Client client, Metrics metrics, AiOptions? options = null) => new(client,
        options ?? new AiOptions { Provider = "lmstudio", Model = "test", MaxOperationAttempts = 1 },
        metrics, NullLogger<AiExecutionGate>.Instance);
    private static AiExecutionRequest Request(AiTaskKind task = AiTaskKind.RecipeFacts) =>
        new(task, [new(ChatRole.User, "input")], "test");

    [Fact]
    public async Task Execution_UnconfiguredNonPlanningTask_IsRejectedInsideTheGate()
    {
        var client = new Client();
        var gate = Gate(client, new Metrics(), new AiOptions());
        var execute = () => gate.ExecuteAsync(Request());
        await execute.Should().ThrowAsync<InvalidOperationException>().WithMessage("AI is not configured");
        client.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, true)]
    [InlineData(true, null)]
    public async Task Execution_RejectsPlanningWhenOnlyFormatPassed_LeavesFactsAvailable(bool? evaluation, bool? intent)
    {
        var client = new Client();
        var gate = Gate(client, new Metrics());
        gate.UpdateCapability(new AiModelTestResult { TestedAt = DateTimeOffset.UtcNow, Provider = "test", Model = "test",
            ResponseMode = AiResponseMode.JsonSchema, EvaluationPassed = evaluation, PlanningIntentPassed = intent });
        var execute = () => gate.ExecuteAsync(Request(AiTaskKind.WeekPlanning));
        await execute.Should().ThrowAsync<InvalidOperationException>();
        await using var facts = await gate.ExecuteAsync(Request());
        await facts.ReportOutcomeAsync(AiTaskOutcome.Success);
        client.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Execution_CountsEveryCallAgainstTheSharedOperationLimit()
    {
        var client = new Client();
        var gate = Gate(client, new Metrics());
        using var operation = gate.BeginOperation();
        await using var first = await gate.ExecuteAsync(Request());
        await first.ReportOutcomeAsync(AiTaskOutcome.Success);
        var second = () => gate.ExecuteAsync(Request());
        await second.Should().ThrowAsync<InvalidOperationException>();
        client.Calls.Should().Be(1);
    }

    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Calls;
        public Func<int, CancellationToken, Task<HttpResponseMessage>> Respond = (_, _) => Task.FromResult(Reply(HttpStatusCode.OK));
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Respond(Interlocked.Increment(ref Calls), cancellationToken);
    }

    private static HttpResponseMessage Reply(HttpStatusCode status, string retryAfter = "0")
    {
        var body = status == HttpStatusCode.OK
            ? """{"id":"test","object":"chat.completion","created":0,"model":"test","choices":[{"index":0,"message":{"role":"assistant","content":"{}"},"finish_reason":"stop"}]}"""
            : """{"error":{"message":"controlled provider failure","type":"test_error","code":"test_error"}}""";
        var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        return response;
    }

    private static AiOptions ProviderOptions(int maximum = 4, int retries = 1) => new()
    {
        Provider = "openai-compatible", Model = "test", BaseUrl = "https://provider.example/v1", ApiKey = "test-key",
        MaxOperationAttempts = maximum, MaxTransportRetries = retries
    };

    [Theory]
    [InlineData(429, "rateLimited")]
    [InlineData(503, "error")]
    public async Task RealSdk_RetriesAreIndividualBudgetedRecordedProviderCalls(int status, string outcome)
    {
        var handler = new ProviderHandler { Respond = (call, _) => Task.FromResult(Reply(call == 1 ? (HttpStatusCode)status : HttpStatusCode.OK)) };
        using var http = new HttpClient(handler);
        using var client = ChatClientFactory.Create(ProviderOptions(), http);
        var metrics = new Metrics();
        var gate = new AiExecutionGate(client, ProviderOptions(), metrics, NullLogger<AiExecutionGate>.Instance);
        var execution = await gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text));
        execution.Succeeded.Should().BeTrue();
        handler.Calls.Should().Be(2, "the SDK must not add hidden retries beneath the gate");
        metrics.Runs.Select(run => run.OperationAttempt).Should().Equal(1, 2);
        metrics.Runs.Select(run => run.Outcome).Should().Equal(outcome, "success");
        metrics.Runs.Select(run => run.OperationId).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData(400, 3, 4)]
    [InlineData(429, 0, 4)]
    [InlineData(429, 3, 1)]
    public async Task RealSdk_NonRetryableDisabledOrExhaustedBudget_NeverIssuesHiddenRequest(int status, int retries, int maximum)
    {
        var options = ProviderOptions(maximum, retries);
        var handler = new ProviderHandler { Respond = (_, _) => Task.FromResult(Reply((HttpStatusCode)status)) };
        using var http = new HttpClient(handler);
        using var client = ChatClientFactory.Create(options, http);
        var metrics = new Metrics();
        var gate = new AiExecutionGate(client, options, metrics, NullLogger<AiExecutionGate>.Instance);
        var execute = () => gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text));
        await execute.Should().ThrowAsync<Exception>();
        handler.Calls.Should().Be(1);
        metrics.Runs.Count(run => run.OperationAttempt > 0).Should().Be(1);
        if (maximum == 1) metrics.Runs.Last().Outcome.Should().Be("budgetExceeded");
    }

    [Fact]
    public async Task RealSdk_RetryAfterCannotOutliveTheOperationDeadlineOrSendEarly()
    {
        var handler = new ProviderHandler { Respond = (_, _) => Task.FromResult(Reply(HttpStatusCode.TooManyRequests, "120")) };
        using var http = new HttpClient(handler);
        using var client = ChatClientFactory.Create(ProviderOptions(), http);
        var metrics = new Metrics();
        var gate = new AiExecutionGate(client, ProviderOptions(), metrics, NullLogger<AiExecutionGate>.Instance);
        var execute = () => gate.ExecuteValidatedAsync(Request() with { DeadlineUtc = DateTime.UtcNow.AddMilliseconds(100) },
            response => AiValidation<string>.Valid(response.Text));
        await execute.Should().ThrowAsync<OperationCanceledException>();
        handler.Calls.Should().Be(1);
        metrics.Runs.Last().Outcome.Should().Be("timeout");
        // The short deadline can expire while the SDK processes the first 429,
        // or during the retry backoff. Both paths must stop after one wire call.
        if (metrics.Runs.Count == 1) metrics.Runs[0].OperationAttempt.Should().Be(1);
        else
        {
            metrics.Runs.Select(run => run.Outcome).Should().Equal("rateLimited", "timeout");
            metrics.Runs.Last().OperationAttempt.Should().Be(0);
        }
    }

    [Fact]
    public async Task RealSdk_BackoffReleasesAdmissionAndCallerCancellationIsDistinct()
    {
        var firstCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ProviderHandler { Respond = (call, _) =>
        {
            if (call == 1) firstCalled.TrySetResult();
            return Task.FromResult(Reply(call == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK, "120"));
        } };
        using var http = new HttpClient(handler);
        using var client = ChatClientFactory.Create(ProviderOptions(), http);
        var metrics = new Metrics();
        var gate = new AiExecutionGate(client, ProviderOptions(), metrics, NullLogger<AiExecutionGate>.Instance);
        using var cancellation = new CancellationTokenSource();
        var first = gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text), cancellation.Token);
        await firstCalled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        try
        {
            var second = await gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text))
                .WaitAsync(TimeSpan.FromSeconds(2));
            second.Succeeded.Should().BeTrue();
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Calls.Should().Be(2);
        metrics.Runs.Last().Outcome.Should().Be("cancelled");
        metrics.Runs.Last().OperationAttempt.Should().Be(0);
    }

    [Fact]
    public async Task NestedParallelWork_SharesAttemptLimitCorrelationAndDeadline()
    {
        var client = new Client { Respond = async token =>
        {
            await Task.Delay(10, token);
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"));
        } };
        var metrics = new Metrics();
        var gate = Gate(client, metrics, new AiOptions { Provider = "lmstudio", Model = "test",
            MaxOperationAttempts = 3, MaxConcurrentRequests = 5 });
        using var operation = gate.BeginOperation(operationId: "parent-plan");
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(async index =>
        {
            await Task.Yield();
            using var child = gate.BeginOperation(operationId: $"child-{index}");
            child.DeadlineUtc.Should().BeOnOrBefore(operation.DeadlineUtc);
            try
            {
                await gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text));
                return true;
            }
            catch (AiOperationBudgetExceededException) { return false; }
        }));
        results.Count(value => value).Should().Be(3);
        client.Calls.Should().Be(3);
        operation.Attempts.Should().Be(3);
        metrics.Runs.Select(run => run.OperationId).Distinct().Should().Equal("parent-plan");
        metrics.Runs.Where(run => run.OperationAttempt > 0).Select(run => run.OperationAttempt).Order().Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task IndependentCapabilityOperation_DoesNotSpendItsCallersAllowance()
    {
        var client = new Client();
        var metrics = new Metrics();
        var gate = Gate(client, metrics);
        using var operation = gate.BeginOperation(operationId: "plan");
        using (gate.BeginOperation(operationId: "capability", independent: true))
            await gate.ExecuteValidatedAsync(Request(AiTaskKind.CapabilityPlanning), response => AiValidation<string>.Valid(response.Text));
        await gate.ExecuteValidatedAsync(Request(), response => AiValidation<string>.Valid(response.Text));
        client.Calls.Should().Be(2);
        metrics.Runs.Select(run => run.OperationId).Should().Equal("capability", "plan");
        metrics.Runs.Select(run => run.OperationAttempt).Should().Equal(1, 1);
    }

    [Fact]
    public async Task ValidatorException_IsOneTerminalValidationFailure()
    {
        var metrics = new Metrics();
        var gate = Gate(new Client(), metrics);
        var execute = () => gate.ExecuteValidatedAsync<string>(Request(), _ => throw new InvalidDataException("raw untrusted content"));
        await execute.Should().ThrowAsync<InvalidDataException>();
        var run = metrics.Runs.Should().ContainSingle().Subject;
        run.Outcome.Should().Be("validationFailure");
        run.Error.Should().NotContain("raw untrusted content");
    }

    [Fact]
    public async Task ValidationIsInsideTheDeadlineAndCancellationBoundary()
    {
        var metrics = new Metrics();
        var gate = Gate(new Client(), metrics);
        var execute = () => gate.ExecuteValidatedAsync(Request() with { DeadlineUtc = DateTime.UtcNow.AddMilliseconds(15) }, response =>
        {
            Thread.Sleep(40);
            return AiValidation<string>.Valid(response.Text);
        });
        await execute.Should().ThrowAsync<OperationCanceledException>();
        metrics.Runs.Should().ContainSingle().Which.Outcome.Should().Be("timeout");
    }

    [Fact]
    public async Task OnlyTheExplicitIntentProbeCanBypassPlanningEligibility()
    {
        var client = new Client();
        var gate = Gate(client, new Metrics());
        await gate.ExecuteValidatedAsync(Request(AiTaskKind.PlanningIntent) with { CapabilityProbe = true },
            response => AiValidation<string>.Valid(response.Text));
        var bypass = () => gate.ExecuteAsync(Request(AiTaskKind.WeekPlanning) with { CapabilityProbe = true });
        await bypass.Should().ThrowAsync<InvalidOperationException>();
        var normalIntent = () => gate.ExecuteAsync(Request(AiTaskKind.PlanningIntent));
        await normalIntent.Should().ThrowAsync<InvalidOperationException>();
        client.Calls.Should().Be(1);
    }
}
