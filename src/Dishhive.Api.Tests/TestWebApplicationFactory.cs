using Dishhive.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Dishhive.Api.Tests.Mocks;

namespace Dishhive.Api.Tests;

/// <summary>
/// Custom WebApplicationFactory for integration tests using EF Core InMemory database
/// </summary>
public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    private static int _databaseCounter = 0;
    private readonly string _databaseName;

    public TestWebApplicationFactory()
    {
        // Use unique database name for each factory instance to ensure test isolation
        _databaseName = $"TestDatabase_{Interlocked.Increment(ref _databaseCounter)}";
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

            // Manual image-URL tests use a public literal address so the SSRF guard
            // remains active while the actual response stays fully in-process.
            services.AddHttpClient("RecipeImages")
                .ConfigurePrimaryHttpMessageHandler(() => new MockHttpMessageHandler()
                    .RespondWith("https://1.1.1.1/", Convert.FromBase64String(
                        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="), "image/png"));
        });
    }
}
