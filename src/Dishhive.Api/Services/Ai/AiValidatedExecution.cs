using Microsoft.Extensions.AI;

namespace Dishhive.Api.Services.Ai;

public sealed record AiValidation<T>(T? Value, AiTaskOutcome Outcome, string? Error = null)
{
    public static AiValidation<T> Valid(T value) => new(value, AiTaskOutcome.Success);
    public static AiValidation<T> Invalid(AiTaskOutcome outcome, string error, T? value = default) => new(value, outcome, error);
    public static AiValidation<T> NoResult() => new(default, AiTaskOutcome.NoResult);
}

public sealed record AiValidatedExecution<T>(ChatResponse Response, T? Value, AiTaskOutcome Outcome, string? Error)
{
    public bool Succeeded => Outcome == AiTaskOutcome.Success;
}

/// <summary>Owns the response lifetime and exactly one terminal validation outcome.</summary>
public static class AiValidatedExecution
{
    public static async Task<AiValidatedExecution<T>> ExecuteValidatedAsync<T>(this IAiExecutionGate gate,
        AiExecutionRequest request, Func<ChatResponse, AiValidation<T>> validate,
        CancellationToken cancellationToken = default)
    {
        using var ownOperation = AiOperationBudget.Current is null ? gate.BeginOperation(cancellationToken, request.OperationId) : null;
        var operation = AiOperationBudget.Current!;
        var deadline = new[] { operation.DeadlineUtc, request.DeadlineUtc ?? DateTime.MaxValue,
            DateTime.UtcNow.AddSeconds(gate.Profile(request.Task, request.UseExtendedTimeout).TimeoutSeconds) }.Min();
        using var validationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operation.Token);
        validationTimeout.CancelAfter(TimeSpan.FromTicks(Math.Max(1, (deadline - DateTime.UtcNow).Ticks)));
        await using var execution = await gate.ExecuteAsync(request with { DeadlineUtc = deadline }, cancellationToken);
        AiValidation<T> result;
        try
        {
            validationTimeout.Token.ThrowIfCancellationRequested();
            result = validate(execution.Response);
            validationTimeout.Token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline) throw new OperationCanceledException(validationTimeout.Token);
            if (result.Outcome is not (AiTaskOutcome.Success or AiTaskOutcome.NoResult
                or AiTaskOutcome.ParseFailure or AiTaskOutcome.ValidationFailure))
                throw new InvalidOperationException("A response validator must return a semantic outcome");
        }
        catch (OperationCanceledException)
        {
            var outcome = operation.CallerCancelled || cancellationToken.IsCancellationRequested
                ? AiTaskOutcome.Cancelled : AiTaskOutcome.Timeout;
            await execution.ReportOutcomeAsync(outcome, "Response validation cancelled");
            throw;
        }
        catch (Exception ex)
        {
            await execution.ReportOutcomeAsync(AiTaskOutcome.ValidationFailure, "Response validator failed: " + ex.GetType().Name);
            throw;
        }
        var error = result.Error is { Length: > 1000 } ? result.Error[..1000] : result.Error;
        await execution.ReportOutcomeAsync(result.Outcome, error);
        return new(execution.Response, result.Value, result.Outcome, error);
    }
}
