using Dishhive.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory for integration tests using EF Core InMemory database.
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static int _databaseCounter = 0;
    private readonly string _databaseName;

    public TestWebApplicationFactory()
    {
        // Use unique database name per factory instance for test isolation
        _databaseName = $"DishhiveTestDatabase_{Interlocked.Increment(ref _databaseCounter)}";
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            // Program.cs will not register PostgreSQL in Testing environment,
            // so we only need to add the InMemory DbContext here
            services.AddDbContext<DishhiveDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName);
            });

            // Disable Freezy integration in tests — avoids dependency on external service
            services.Configure<Dishhive.Api.Services.FreezyIntegrationOptions>(opts =>
            {
                opts.Enabled = false;
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
