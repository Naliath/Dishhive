using Dishhive.Api.Data;
using Dishhive.Api.Services.Suggestions;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Ai;

/// <summary>Recover interrupted validations and bound retention of diagnostic text.</summary>
public sealed class AiMetricsMaintenanceService(
    IServiceScopeFactory scopes, AiOptions options, ILogger<AiMetricsMaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not maintain AI diagnostics");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
        var interruptedBefore = DateTime.UtcNow.AddSeconds(-Math.Max(900, options.OperationTimeoutSeconds * 2));
        var textBefore = options.RetainDiagnosticText ? DateTime.UtcNow.AddHours(-24) : DateTime.MaxValue;
        var interrupted = context.AiTaskRuns.Where(run => run.CompletedAt < interruptedBefore
            && (run.Outcome == "responseReceived" || run.Outcome == "completed"));
        var diagnosticText = context.AiPlanningRuns.Where(run => run.StartedAt < textBefore
            && (run.Instructions != null || run.NormalizedIntentJson != null));
        var obsoleteReviews = await context.AiReviewEvents.OrderByDescending(review => review.OccurredAt)
            .ThenByDescending(review => review.Id).Skip(2500).Select(review => review.Id).ToListAsync(cancellationToken);
        if (obsoleteReviews.Count > 0)
        {
            var obsolete = context.AiReviewEvents.Where(review => obsoleteReviews.Contains(review.Id));
            if (context.Database.IsRelational()) await obsolete.ExecuteDeleteAsync(cancellationToken);
            else context.AiReviewEvents.RemoveRange(await obsolete.ToListAsync(cancellationToken));
        }
        if (context.Database.IsRelational())
        {
            await interrupted.ExecuteUpdateAsync(setters => setters.SetProperty(run => run.Outcome, "abandoned"), cancellationToken);
            await diagnosticText.ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Instructions, (string?)null)
                .SetProperty(run => run.NormalizedIntentJson, (string?)null), cancellationToken);
        }
        else
        {
            foreach (var run in await interrupted.ToListAsync(cancellationToken)) run.Outcome = "abandoned";
            foreach (var run in await diagnosticText.ToListAsync(cancellationToken))
            {
                run.Instructions = null;
                run.NormalizedIntentJson = null;
            }
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
