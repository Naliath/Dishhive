namespace Dishhive.Api.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISHHIVE_TEST_POSTGRES")))
            Skip = "Set DISHHIVE_TEST_POSTGRES to run PostgreSQL integration tests.";
    }
}
