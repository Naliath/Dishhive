using Dishhive.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests;

/// <summary>
/// Base class for integration tests providing common database setup/teardown.
/// </summary>
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

    /// <summary>
    /// Creates a fresh DbContext in a new scope. Use this when you need to verify
    /// data changes made by the API without EF Core's change tracker interference.
    /// Call within a using statement: using var freshContext = CreateFreshContext();
    /// </summary>
    protected DishhiveDbContext CreateFreshContext()
    {
        var newScope = Factory.Services.CreateScope();
        return newScope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    }

    /// <summary>
    /// Clear all data from the database between tests.
    /// </summary>
    protected void ClearDatabase()
    {
        DbContext.Recipes.RemoveRange(DbContext.Recipes);
        DbContext.FamilyMembers.RemoveRange(DbContext.FamilyMembers);
        DbContext.WeekPlans.RemoveRange(DbContext.WeekPlans);
        DbContext.UserSettings.RemoveRange(DbContext.UserSettings);
        DbContext.DishRatings.RemoveRange(DbContext.DishRatings);
        DbContext.SaveChanges();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            DbContext?.Dispose();
            Scope?.Dispose();
            Client?.Dispose();
            Factory?.Dispose();
        }
    }
}
