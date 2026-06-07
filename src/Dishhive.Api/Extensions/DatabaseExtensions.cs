using Dishhive.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Extensions;

/// <summary>
/// Migration helper mirroring Freezy's pattern: applies pending EF migrations on
/// startup with exponential backoff so the API can wait for the database container.
/// </summary>
public static class DatabaseExtensions
{
    public static async Task MigrateDatabaseAsync(this WebApplication app, int maxRetries = 10, int maxWaitTimeSeconds = 60)
    {
        var logger = app.Services.GetRequiredService<ILogger<Program>>();
        var retryCount = 0;
        var waitTime = TimeSpan.FromSeconds(2);

        while (retryCount < maxRetries)
        {
            try
            {
                logger.LogInformation(
                    "Applying database migrations... (Attempt {Attempt}/{MaxRetries})",
                    retryCount + 1, maxRetries);

                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
                await db.Database.MigrateAsync();

                logger.LogInformation("Database migrations applied.");
                return;
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                var totalWaitTime = Enumerable.Range(0, retryCount)
                    .Sum(i => Math.Min(2 * Math.Pow(2, i), 30));

                if (totalWaitTime > maxWaitTimeSeconds)
                {
                    logger.LogError(ex,
                        "Database connection failed after {Attempts} attempts ({Total:F0}s). Aborting.",
                        retryCount, totalWaitTime);
                    throw;
                }

                logger.LogWarning(
                    "Database not ready (attempt {Attempt}): {Message}. Waiting {Wait}s.",
                    retryCount, ex.Message, waitTime.TotalSeconds);

                await Task.Delay(waitTime);
                waitTime = TimeSpan.FromSeconds(Math.Min(waitTime.TotalSeconds * 2, 30));
            }
        }
    }
}
