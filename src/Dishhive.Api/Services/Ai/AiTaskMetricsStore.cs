using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Ai;

public interface IAiTaskMetricsStore
{
    Task SaveAsync(AiTaskRun run);
    Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null);
}

public sealed class AiTaskMetricsStore(
    IServiceScopeFactory scopeFactory,
    ILogger<AiTaskMetricsStore> logger) : IAiTaskMetricsStore
{
    private const int RetainedRunsPerTask = 250;

    public async Task SaveAsync(AiTaskRun run)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            context.AiTaskRuns.Add(run);
            await context.SaveChangesAsync();

            var obsoleteIds = await context.AiTaskRuns
                .AsNoTracking()
                .Where(item => item.Task == run.Task)
                .OrderByDescending(item => item.StartedAt)
                .Skip(RetainedRunsPerTask)
                .Select(item => item.Id)
                .ToListAsync();
            if (obsoleteIds.Count > 0)
            {
                var obsoleteRuns = await context.AiTaskRuns
                    .Where(item => obsoleteIds.Contains(item.Id))
                    .ToListAsync();
                context.AiTaskRuns.RemoveRange(obsoleteRuns);
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist AI task metrics for {Task}/{OperationId}",
                run.Task, run.OperationId);
        }
    }

    public async Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
            var run = await context.AiTaskRuns.SingleOrDefaultAsync(item => item.Id == runId);
            if (run is null)
            {
                logger.LogWarning("Could not find AI task run {RunId} to update its outcome", runId);
                return;
            }

            run.Outcome = outcome;
            run.Error = error;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not update AI task outcome for {RunId}", runId);
        }
    }
}

public sealed class NoOpAiTaskMetricsStore : IAiTaskMetricsStore
{
    public Task SaveAsync(AiTaskRun run) => Task.CompletedTask;
    public Task UpdateOutcomeAsync(Guid runId, string outcome, string? error = null) => Task.CompletedTask;
}
