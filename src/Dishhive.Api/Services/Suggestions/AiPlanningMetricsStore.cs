using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Suggestions;

public sealed class AiPlanningMetricsStore(
    DishhiveDbContext context,
    ILogger<AiPlanningMetricsStore> logger)
{
    private const int RetainedRuns = 200;

    public async Task SaveAsync(AiPlanningRun run)
    {
        try
        {
            context.AiPlanningRuns.Add(run);
            await context.SaveChangesAsync();

            var obsoleteIds = await context.AiPlanningRuns
                .AsNoTracking()
                .OrderByDescending(item => item.StartedAt)
                .Skip(RetainedRuns)
                .Select(item => item.Id)
                .ToListAsync();
            if (obsoleteIds.Count > 0)
            {
                await context.AiPlanningRuns
                    .Where(item => obsoleteIds.Contains(item.Id))
                    .ExecuteDeleteAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not persist AI planning metrics for request {RequestId}", run.RequestId);
        }
    }
}
