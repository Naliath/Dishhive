using Dishhive.Api.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Dishhive.Api.Tests;

/// <summary>
/// Base class for integration tests: provides factory, HTTP client and database context
/// with per-test isolation helpers
/// </summary>
public abstract class TestBase : IDisposable
{
    protected TestWebApplicationFactory Factory { get; }
    protected HttpClient Client { get; }
    protected IServiceScope Scope { get; }
    protected DishhiveDbContext DbContext { get; }

    protected TestBase()
    {
        Factory = new TestWebApplicationFactory();
        Client = Factory.CreateClient();
        Scope = Factory.Services.CreateScope();
        DbContext = Scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    }

    /// <summary>
    /// Removes all data from the database for test isolation
    /// </summary>
    protected void ClearDatabase()
    {
        DbContext.FamilyMemberFavorites.RemoveRange(DbContext.FamilyMemberFavorites);
        DbContext.PlannedMealAttendees.RemoveRange(DbContext.PlannedMealAttendees);
        DbContext.PlannedMeals.RemoveRange(DbContext.PlannedMeals);
        DbContext.RecipeIngredients.RemoveRange(DbContext.RecipeIngredients);
        DbContext.RecipeSteps.RemoveRange(DbContext.RecipeSteps);
        DbContext.Recipes.RemoveRange(DbContext.Recipes);
        DbContext.FamilyMembers.RemoveRange(DbContext.FamilyMembers);
        DbContext.UserSettings.RemoveRange(DbContext.UserSettings);
        DbContext.SaveChanges();
        DbContext.ChangeTracker.Clear();
    }

    /// <summary>
    /// Creates a fresh context to avoid EF Core change tracker cache issues when
    /// asserting on data modified through the HTTP client
    /// </summary>
    protected DishhiveDbContext CreateFreshContext()
    {
        var scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<DishhiveDbContext>();
    }

    public void Dispose()
    {
        Scope.Dispose();
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
