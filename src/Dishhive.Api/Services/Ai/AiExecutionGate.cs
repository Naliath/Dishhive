using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using Microsoft.Extensions.AI;
using System.Diagnostics;
using System.Text.Json;

namespace Dishhive.Api.Services.Ai;

public enum AiResponseFormatPolicy
{
    None,
    WhenSupported,
    Force
}

public sealed record AiExecutionRequest(
    AiTaskKind Task,
    IReadOnlyList<ChatMessage> Messages,
    string ContractVersion,
    string? OperationId = null,
    int Attempt = 1,
    ChatResponseFormat? ResponseFormat = null,
    AiResponseFormatPolicy ResponseFormatPolicy = AiResponseFormatPolicy.WhenSupported,
    bool UseExtendedTimeout = false);

public sealed record AiGateDecision(bool Allowed, AiResponseMode ResponseMode, long WaitMs, string? Reason);

public sealed class AiExecutionResult(
    ChatResponse response,
    Guid runId,
    IAiTaskMetricsStore metricsStore)
{
    public ChatResponse Response { get; } = response;
    public Guid RunId { get; } = runId;

    public Task ReportOutcomeAsync(string outcome, string? error = null) =>
        metricsStore.UpdateOutcomeAsync(
            RunId, Limit(outcome, 30), error is null ? null : Limit(error, 1000));

    private static string Limit(string value, int length) =>
        value.Length <= length ? value : value[..length];
}

public interface IAiExecutionGate
{
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

    public AiResponseMode VerifiedResponseMode => (AiResponseMode)Volatile.Read(ref _verifiedResponseMode);

    public AiTaskProfile Profile(AiTaskKind task, bool useExtendedTimeout = false) =>
        AiTaskProfiles.Resolve(task, options, useExtendedTimeout);

    public void UpdateCapability(AiModelTestResult result) =>
        Volatile.Write(ref _verifiedResponseMode, (int)result.ResponseMode);

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
        return new(result.Viable, result.ResponseMode, stopwatch.ElapsedMilliseconds,
            result.Viable ? null : result.Verdict);
    }

    public async Task<AiExecutionResult> ExecuteAsync(
        AiExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = Profile(request.Task, request.UseExtendedTimeout);
        var operationId = string.IsNullOrWhiteSpace(request.OperationId)
            ? Guid.NewGuid().ToString("N")[..12]
            : request.OperationId.Trim();
        operationId = Limit(operationId, 64);
        var applySchema = request.ResponseFormat is not null
            && request.ResponseFormatPolicy != AiResponseFormatPolicy.None
            && (request.ResponseFormatPolicy == AiResponseFormatPolicy.Force
                || VerifiedResponseMode == AiResponseMode.JsonSchema);
        var responseMode = applySchema ? "jsonSchema" : "promptedJson";
        var chatOptions = new ChatOptions
        {
            MaxOutputTokens = profile.MaxOutputTokens,
            Temperature = profile.Temperature,
            Reasoning = options.DisableThinking
                ? new ReasoningOptions { Effort = ReasoningEffort.None, Output = ReasoningOutput.None }
                : null,
            ResponseFormat = applySchema ? request.ResponseFormat : null
        };
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(profile.TimeoutSeconds));

        try
        {
            var response = await chatClient.GetResponseAsync(
                request.Messages, chatOptions, cancellationToken: timeout.Token);
            stopwatch.Stop();
            var usage = response.Usage;
            var run = new AiTaskRun
            {
                OperationId = operationId,
                Task = profile.Name,
                Provider = Limit(options.Provider, 50),
                Model = Limit(options.Model, 200),
                ConfigFingerprint = Limit(options.ExecutionFingerprint, 500),
                ContractVersion = Limit(request.ContractVersion, 80),
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                Outcome = "completed",
                ResponseMode = responseMode,
                FinishReason = response.FinishReason?.ToString(),
                Attempt = request.Attempt,
                MaxOutputTokens = profile.MaxOutputTokens,
                Temperature = profile.Temperature,
                ReasoningDisabled = options.DisableThinking,
                SchemaRequested = request.ResponseFormat is not null,
                SchemaApplied = applySchema,
                PromptCharacters = request.Messages.Sum(message => (long)(message.Text?.Length ?? 0)),
                OutputCharacters = response.Text?.Length ?? 0,
                InputTokens = usage?.InputTokenCount ?? 0,
                OutputTokens = usage?.OutputTokenCount ?? 0,
                ReasoningTokens = usage?.ReasoningTokenCount ?? 0,
                TotalTokens = usage?.TotalTokenCount ?? 0,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
            await metricsStore.SaveAsync(run);
            LogCompletion(profile.Name, operationId, response, stopwatch.ElapsedMilliseconds);
            return new AiExecutionResult(response, run.Id, metricsStore);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await SaveFailureAsync("timeout", "The AI task timed out");
            throw;
        }
        catch (OperationCanceledException)
        {
            await SaveFailureAsync("cancelled", "Cancelled by caller");
            throw;
        }
        catch (Exception ex)
        {
            await SaveFailureAsync("error", ex.Message);
            throw;
        }

        async Task SaveFailureAsync(string outcome, string error)
        {
            stopwatch.Stop();
            await metricsStore.SaveAsync(new AiTaskRun
            {
                OperationId = operationId,
                Task = profile.Name,
                Provider = Limit(options.Provider, 50),
                Model = Limit(options.Model, 200),
                ConfigFingerprint = Limit(options.ExecutionFingerprint, 500),
                ContractVersion = Limit(request.ContractVersion, 80),
                StartedAt = startedAt,
                CompletedAt = DateTime.UtcNow,
                Outcome = outcome,
                ResponseMode = responseMode,
                Error = error.Length > 1000 ? error[..1000] : error,
                Attempt = request.Attempt,
                MaxOutputTokens = profile.MaxOutputTokens,
                Temperature = profile.Temperature,
                ReasoningDisabled = options.DisableThinking,
                SchemaRequested = request.ResponseFormat is not null,
                SchemaApplied = applySchema,
                PromptCharacters = request.Messages.Sum(message => (long)(message.Text?.Length ?? 0)),
                DurationMs = stopwatch.ElapsedMilliseconds
            });
        }
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
