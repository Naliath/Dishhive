using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.ClientModel;
using System.Globalization;

namespace Dishhive.Api.Services.Ai;

public enum AiResponseFormatPolicy
{
    None,
    WhenSupported,
    Force
}

public enum AiTaskOutcome
{
    Success,
    NoResult,
    ParseFailure,
    ValidationFailure,
    Timeout,
    Cancelled,
    Error,
    InputLimit,
    Abandoned,
    RateLimited,
    BudgetExceeded
}

public enum AiRequestPriority { Interactive, Background }

public sealed record AiExecutionRequest(
    AiTaskKind Task,
    IReadOnlyList<ChatMessage> Messages,
    string ContractVersion,
    string? OperationId = null,
    int Attempt = 1,
    ChatResponseFormat? ResponseFormat = null,
    AiResponseFormatPolicy ResponseFormatPolicy = AiResponseFormatPolicy.WhenSupported,
    bool UseExtendedTimeout = false,
    DateTime? DeadlineUtc = null,
    AiRequestPriority Priority = AiRequestPriority.Interactive,
    bool CapabilityProbe = false);

public sealed record AiGateDecision(bool Allowed, AiResponseMode ResponseMode, long WaitMs, string? Reason);

public sealed class AiExecutionResult(
    ChatResponse response,
    Guid runId,
    IAiTaskMetricsStore metricsStore) : IAsyncDisposable
{
    private int _reported;
    public ChatResponse Response { get; } = response;
    public Guid RunId { get; } = runId;

    public Task ReportOutcomeAsync(AiTaskOutcome outcome, string? error = null)
    {
        if (Interlocked.Exchange(ref _reported, 1) != 0)
            throw new InvalidOperationException("An AI execution outcome may only be reported once.");
        return metricsStore.UpdateOutcomeAsync(
            RunId, OutcomeName(outcome), error is null ? null : Limit(error, 1000));
    }

    public ValueTask DisposeAsync() => Interlocked.Exchange(ref _reported, 1) == 0
        ? new ValueTask(metricsStore.UpdateOutcomeAsync(RunId, "abandoned", "Response validation did not complete"))
        : ValueTask.CompletedTask;

    public static string OutcomeName(AiTaskOutcome outcome) => outcome switch
    {
        AiTaskOutcome.Success => "success",
        AiTaskOutcome.NoResult => "noResult",
        AiTaskOutcome.ParseFailure => "parseFailure",
        AiTaskOutcome.ValidationFailure => "validationFailure",
        AiTaskOutcome.Timeout => "timeout",
        AiTaskOutcome.Cancelled => "cancelled",
        AiTaskOutcome.Error => "error",
        AiTaskOutcome.InputLimit => "inputLimit",
        AiTaskOutcome.RateLimited => "rateLimited",
        AiTaskOutcome.BudgetExceeded => "budgetExceeded",
        _ => "abandoned"
    };

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];
}

public interface IAiExecutionGate
{
    AiOperationBudget BeginOperation(CancellationToken cancellationToken = default, string? operationId = null,
        bool independent = false) => new(300, 32, cancellationToken, operationId, independent);
    AiTaskProfile Profile(AiTaskKind task, bool useExtendedTimeout = false);
    AiResponseMode VerifiedResponseMode { get; }
    void UpdateCapability(AiModelTestResult result);
    Task<AiGateDecision> EnsureAvailableAsync(
        AiTaskKind task,
        IAiModelCapabilityService? capability = null,
        CancellationToken cancellationToken = default);
    Task<AiExecutionResult> ExecuteAsync(
        AiExecutionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Single boundary for every Dishhive model call. It owns task settings, reasoning controls,
/// verified response-format selection, timeouts, diagnostics, and durable per-call metrics.
/// </summary>
public sealed class AiExecutionGate(
    IChatClient chatClient,
    AiOptions options,
    IAiTaskMetricsStore metricsStore,
    ILogger<AiExecutionGate> logger) : IAiExecutionGate
{
    private int _verifiedResponseMode = (int)AiResponseMode.PromptedJson;
    private int _planningEligibility;
    private readonly PriorityAdmissionQueue _admission = new(options.MaxConcurrentRequests, options.MaxQueuedRequests);

    public AiOperationBudget BeginOperation(CancellationToken cancellationToken = default, string? operationId = null,
        bool independent = false) =>
        new(options.OperationTimeoutSeconds, options.MaxOperationAttempts, cancellationToken, operationId, independent);

    public AiResponseMode VerifiedResponseMode => (AiResponseMode)Volatile.Read(ref _verifiedResponseMode);

    public AiTaskProfile Profile(AiTaskKind task, bool useExtendedTimeout = false) =>
        AiTaskProfiles.Resolve(task, options, useExtendedTimeout);

    public void UpdateCapability(AiModelTestResult result)
    {
        Volatile.Write(ref _verifiedResponseMode, (int)result.ResponseMode);
        Volatile.Write(ref _planningEligibility, result.PlanningEligible ? 1 : -1);
    }

    public async Task<AiGateDecision> EnsureAvailableAsync(
        AiTaskKind task,
        IAiModelCapabilityService? capability = null,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            return new(false, AiResponseMode.None, 0, "AI is not configured");
        }

        var profile = Profile(task);
        if (!profile.RequiresPlanningCapability)
        {
            return new(true, VerifiedResponseMode, 0, null);
        }
        if (capability is null)
        {
            throw new InvalidOperationException($"{profile.Name} requires the planning capability service.");
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await capability.EnsureTestedAsync(cancellationToken);
        UpdateCapability(result);
        return new(result.PlanningEligible, result.ResponseMode, stopwatch.ElapsedMilliseconds,
            result.PlanningEligible ? null : "Planning quality has not passed the capability checks");
    }

    public async Task<AiExecutionResult> ExecuteAsync(
        AiExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured) throw new InvalidOperationException("AI is not configured");
        var profile = Profile(request.Task, request.UseExtendedTimeout);
        if (request.CapabilityProbe && request.Task is not (AiTaskKind.CapabilityPlanning or AiTaskKind.PlanningIntent or AiTaskKind.CapabilityVision))
            throw new InvalidOperationException("Only capability tasks may bypass planning eligibility");
        if (profile.RequiresPlanningCapability && !request.CapabilityProbe && Volatile.Read(ref _planningEligibility) != 1)
            throw new InvalidOperationException($"{profile.Name} requires passing planning quality evidence");
        if (request.ResponseFormatPolicy == AiResponseFormatPolicy.Force
            && request.Task != AiTaskKind.CapabilityPlanning && !request.CapabilityProbe)
            throw new InvalidOperationException("Forced response formats are reserved for capability probes");

        using var ownOperation = AiOperationBudget.Current is null
            ? BeginOperation(cancellationToken, request.OperationId) : null;
        var operation = AiOperationBudget.Current!;
        var operationId = Limit(operation.OperationId.Trim(), 64);
        var applySchema = request.ResponseFormat is not null
            && request.ResponseFormatPolicy != AiResponseFormatPolicy.None
            && (request.ResponseFormatPolicy == AiResponseFormatPolicy.Force || VerifiedResponseMode == AiResponseMode.JsonSchema);
        var responseMode = applySchema ? "jsonSchema" : "promptedJson";
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
            Reasoning = profile.EnableReasoning
                ? new ReasoningOptions { Effort = ReasoningEffort.Low, Output = ReasoningOutput.None }
                : options.DisableThinking
                ? new ReasoningOptions { Effort = ReasoningEffort.None, Output = ReasoningOutput.None } : null,
            ResponseFormat = applySchema ? request.ResponseFormat : null
        };
        var promptCharacters = request.Messages.Sum(message => (long)(message.Text?.Length ?? 0));
        var promptHash = PromptHash(request.Messages);
        var deadline = new[] { operation.DeadlineUtc, request.DeadlineUtc ?? DateTime.MaxValue,
            DateTime.UtcNow.AddSeconds(profile.TimeoutSeconds) }.Min();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        timeout.CancelAfter(TimeSpan.FromTicks(Math.Max(1, (deadline - DateTime.UtcNow).Ticks)));

        for (var transportAttempt = 0; ; transportAttempt++)
        {
            var startedAt = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var queueStopwatch = Stopwatch.StartNew();
            var ordinal = 0;
            IDisposable? admission = null;
            TimeSpan? retryDelay = null;
            try
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadline) throw new OperationCanceledException(timeout.Token);
                var inputCharacters = AiTokenBudget.InputCharacters(request.Messages, request.ResponseFormat);
                var inputLimit = Math.Min(profile.MaxInputTokens, options.ContextWindowTokens - profile.MaxOutputTokens);
                if (inputCharacters > (long)inputLimit * AiTokenBudget.ConservativeCharactersPerToken)
                    throw new AiInputBudgetExceededException(profile.Name, inputLimit);
                admission = await _admission.EnterAsync(request.Priority, timeout.Token);
                queueStopwatch.Stop();
                if (DateTime.UtcNow >= deadline) throw new OperationCanceledException(timeout.Token);
                ordinal = operation.TakeAttempt();
                var response = await chatClient.GetResponseAsync(request.Messages, chatOptions, timeout.Token);
                stopwatch.Stop();
                var run = CreateRun("responseReceived", null, response);
                await metricsStore.SaveAsync(run);
                LogCompletion(profile.Name, operationId, response, stopwatch.ElapsedMilliseconds);
                return new AiExecutionResult(response, run.Id, metricsStore);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                queueStopwatch.Stop();
                // SDK cleanup can replace cancellation with a disposed response-stream
                // exception. Preserve the operation's cancellation semantics and do
                // not retry a request whose deadline has already elapsed.
                if (ex is not (OperationCanceledException or AiInputBudgetExceededException or AiOperationBudgetExceededException)
                    && (timeout.IsCancellationRequested || DateTime.UtcNow >= deadline))
                {
                    await metricsStore.SaveAsync(CreateRun(AiExecutionResult.OutcomeName(CancellationOutcome()), "Cancelled during provider response handling"));
                    throw new OperationCanceledException("AI operation was cancelled during provider response handling", ex, timeout.Token);
                }
                var outcome = ex switch
                {
                    AiInputBudgetExceededException => AiTaskOutcome.InputLimit,
                    AiOperationBudgetExceededException => AiTaskOutcome.BudgetExceeded,
                    OperationCanceledException => CancellationOutcome(),
                    _ when Status(ex) == 429 => AiTaskOutcome.RateLimited,
                    _ => AiTaskOutcome.Error
                };
                await metricsStore.SaveAsync(CreateRun(AiExecutionResult.OutcomeName(outcome), ex.Message));
                if (ordinal == 0 || transportAttempt >= options.MaxTransportRetries || !IsTransient(ex)) throw;
                retryDelay = RetryDelay(ex, transportAttempt);
                logger.LogInformation("[{OperationId}] Retrying {Task} after provider attempt {Attempt}; delay={DelayMs}ms, status={Status}",
                    operationId, profile.Name, ordinal, retryDelay.Value.TotalMilliseconds, Status(ex));
            }
            finally { admission?.Dispose(); }

            // Backoff does not occupy the model admission slot and never resets the deadline.
            try
            {
                var remaining = TimeSpan.FromTicks(Math.Max(1, (deadline - DateTime.UtcNow).Ticks));
                var exhaustsDeadline = retryDelay!.Value >= remaining;
                await Task.Delay(exhaustsDeadline ? remaining : retryDelay.Value, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                // Task.Delay may round down by a fraction of a millisecond. A
                // provider backoff that cannot fit must never admit an early retry
                // in that gap merely because the deadline timer has not fired yet.
                if (exhaustsDeadline || DateTime.UtcNow >= deadline) throw new OperationCanceledException(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                ordinal = 0;
                await metricsStore.SaveAsync(CreateRun(AiExecutionResult.OutcomeName(CancellationOutcome()), "Cancelled during provider retry backoff"));
                throw;
            }

            AiTaskRun CreateRun(string outcome, string? error, ChatResponse? response = null) => new()
            {
                Id = Guid.NewGuid(), OperationId = operationId, Task = profile.Name,
                Provider = Limit(options.Provider, 50), Model = Limit(options.Model, 200),
                ConfigFingerprint = Limit(options.ExecutionFingerprint, 500), ContractVersion = Limit(request.ContractVersion, 80),
                StartedAt = startedAt, CompletedAt = DateTime.UtcNow, Outcome = outcome,
                ResponseMode = responseMode, FinishReason = response?.FinishReason?.ToString(),
                Error = error is null ? null : Limit(error, 1000), Attempt = request.Attempt, OperationAttempt = ordinal,
                MaxOutputTokens = profile.MaxOutputTokens, Temperature = profile.Temperature,
                ReasoningDisabled = !profile.EnableReasoning && options.DisableThinking, SchemaRequested = request.ResponseFormat is not null,
                SchemaApplied = applySchema, PromptCharacters = promptCharacters, InputBudgetTokens = profile.MaxInputTokens,
                QueueWaitMs = queueStopwatch.ElapsedMilliseconds, PromptHash = promptHash,
                InputRevisionsJson = request.CapabilityProbe ? null : AiInputSnapshotScope.Current?.RevisionsJson,
                InputFingerprint = request.CapabilityProbe ? null : AiInputSnapshotScope.Current?.Fingerprint,
                OutputCharacters = response?.Text?.Length ?? 0, InputTokens = response?.Usage?.InputTokenCount ?? 0,
                OutputTokens = response?.Usage?.OutputTokenCount ?? 0, ReasoningTokens = response?.Usage?.ReasoningTokenCount ?? 0,
                TotalTokens = response?.Usage?.TotalTokenCount ?? 0, DurationMs = stopwatch.ElapsedMilliseconds
            };
        }

        AiTaskOutcome CancellationOutcome() => operation.CallerCancelled
            || (cancellationToken.IsCancellationRequested && operation.DeadlineUtc > DateTime.UtcNow)
                ? AiTaskOutcome.Cancelled : AiTaskOutcome.Timeout;
    }

    private static int? Status(Exception error) => error switch
    {
        ClientResultException client => client.Status,
        HttpRequestException http => (int?)http.StatusCode,
        _ => null
    };

    internal static string PromptHash(IReadOnlyList<ChatMessage> messages)
    {
        var text = string.Join("\0", messages.Select(message => message.Text ?? string.Empty));
        foreach (var data in messages.SelectMany(message => message.Contents).OfType<DataContent>())
            text += "\0" + data.MediaType + ":" + Convert.ToHexString(SHA256.HashData(data.Data.Span));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool IsTransient(Exception error) => error is HttpRequestException { StatusCode: null }
        || Status(error) is 0 or 408 or 429 or 500 or 502 or 503 or 504;

    private static TimeSpan RetryDelay(Exception error, int attempt)
    {
        if (error is ClientResultException client && client.GetRawResponse() is { } response
            && response.Headers.TryGetValue("Retry-After", out var value))
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                && double.IsFinite(seconds) && seconds >= 0)
                return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromDays(1).TotalSeconds));
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
                return TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks));
        }
        return TimeSpan.FromMilliseconds(250 * (1 << attempt));
    }

    private void LogCompletion(string task, string operationId, ChatResponse response, long elapsedMs)
    {
        var usage = response.Usage;
        logger.LogInformation(
            "[{OperationId}] AI task {Task} completed in {ElapsedMs}ms: input={Input}, output={Output}, reasoning={Reasoning}, total={Total}; finish={Finish}",
            operationId, task, elapsedMs, usage?.InputTokenCount, usage?.OutputTokenCount,
            usage?.ReasoningTokenCount, usage?.TotalTokenCount, response.FinishReason);
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var reasoning = string.Join("\n\n", response.Messages
                .SelectMany(message => message.Contents)
                .OfType<TextReasoningContent>()
                .Select(content => content.Text)
                .Where(text => !string.IsNullOrEmpty(text)));
            logger.LogDebug(
                "[{OperationId}] AI task {Task} response: visible={Visible}; reasoning={Reasoning}; contentTypes={ContentTypes}",
                operationId, task, FormatForLog(response.Text, 4000), FormatForLog(reasoning, 4000),
                string.Join(",", response.Messages.SelectMany(message => message.Contents)
                    .Select(content => content.GetType().Name).Distinct()));
        }
    }

    internal static string FormatForLog(string? value, int maxChars)
    {
        if (value is null) return "<null>";
        var text = value.Length <= maxChars ? value : value[..maxChars] + "...";
        return JsonSerializer.Serialize(text);
    }

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];
}

public sealed class AiInputBudgetExceededException(string task, int maxInputTokens)
    : InvalidOperationException($"AI task {task} exceeds its {maxInputTokens}-token input budget.");
