using Dishhive.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests;

public abstract class TestBase : IDisposable
{
    protected readonly TestWebApplicationFactory Factory;
    protected readonly HttpClient Client;
    protected readonly IServiceScope Scope;
    protected readonly DishhiveDbContext DbContext;

    protected TestBase()
    {
        Factory = new TestWebApplicationFactory();
        Client = Factory.CreateClient();
        Scope = Factory.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    }

    /// <summary>Fresh DbContext in a new scope, useful for verifying writes without change-tracker interference.</summary>
    protected DishhiveDbContext CreateFreshContext()
    {
        var newScope = Factory.Services.CreateScope();
        return newScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        DbContext.Dispose();
        Scope.Dispose();
        Client.Dispose();
        Factory.Dispose();
    }
}
