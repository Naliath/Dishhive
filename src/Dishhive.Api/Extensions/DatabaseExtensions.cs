using Dishhive.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Extensions;

public static class DatabaseExtensions
{
    /// <summary>
    /// Applies database migrations with retry logic to handle startup scenarios
    /// where the database server might not be immediately available.
    /// </summary>
    /// <param name="app">The web application</param>
    /// <param name="maxRetries">Maximum number of retry attempts (default: 10)</param>
    /// <param name="maxWaitTimeSeconds">Maximum total wait time in seconds (default: 60)</param>
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
                    "Attempting to connect to database and apply migrations... (Attempt {Attempt}/{MaxRetries})",
                    retryCount + 1, maxRetries);

                using (var scope = app.Services.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
                    await db.Database.MigrateAsync();
                }

                logger.LogInformation("Database connection successful and migrations applied.");
                break;
            }
            catch (Exception ex) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                var totalWaitTime = Enumerable.Range(0, retryCount)
                    .Sum(i => Math.Min(2 * Math.Pow(2, i), 30));

                if (totalWaitTime > maxWaitTimeSeconds)
                {
                    logger.LogError(ex,
                        "Failed to connect to database after {RetryCount} attempts and {TotalWaitTime:F0} seconds. " +
                        "Maximum wait time of {MaxWaitTime} seconds exceeded.",
                        retryCount, totalWaitTime, maxWaitTimeSeconds);
                    throw;
                }

                logger.LogWarning(
                    "Database connection failed (Attempt {Attempt}/{MaxRetries}): {Message}. Retrying in {WaitTime} seconds...",
                    retryCount, maxRetries, ex.Message, waitTime.TotalSeconds);

                await Task.Delay(waitTime);

                // Exponential backoff with max 30 seconds per retry
                waitTime = TimeSpan.FromSeconds(Math.Min(waitTime.TotalSeconds * 2, 30));
            }
        }
    }
}
