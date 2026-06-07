using Dishhive.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests;

/// <summary>
/// Integration test factory that swaps the real PostgreSQL <see cref="DishhiveDbContext"/>
/// for a uniquely named EF Core InMemory database per factory instance, mirroring Freezy's
/// approach so tests are isolated and parallel-safe.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static int _databaseCounter;
    private readonly string _databaseName;

    public TestWebApplicationFactory()
    {
        _databaseName = $"DishhiveTestDb_{Interlocked.Increment(ref _databaseCounter)}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.AddDbContext<DishhiveDbContext>(options =>
                options.UseInMemoryDatabase(_databaseName));
        });
    }
}
