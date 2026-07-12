using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Services.Suggestions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dishhive.Api.Tests.Services;

public class AiPlanningMetricsStoreTests
{
    [Fact]
    public async Task Save_PersistsTechnicalRunMetrics()
    {
        await using var context = new DishhiveDbContext(new DbContextOptionsBuilder<DishhiveDbContext>()
            .UseInMemoryDatabase($"AiMetrics_{Guid.NewGuid()}").Options);
        var run = new AiPlanningRun
        {
            RequestId = "abcd1234",
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            Outcome = "success",
            SearchCount = 2,
            RecipeResolutionCount = 6,
            TotalDurationMs = 1234
        };

        await new AiPlanningMetricsStore(context, NullLogger<AiPlanningMetricsStore>.Instance).SaveAsync(run);

        var stored = await context.AiPlanningRuns.SingleAsync();
        stored.SearchCount.Should().Be(2);
        stored.RecipeResolutionCount.Should().Be(6);
        stored.TotalDurationMs.Should().Be(1234);
    }
}
