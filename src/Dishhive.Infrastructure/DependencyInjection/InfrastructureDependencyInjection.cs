namespace Dishhive.Infrastructure.DependencyInjection;

using Dishhive.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension to register all infrastructure layer services.
/// </summary>
public static class InfrastructureDependencyInjection
{
    public static IServiceCollection AddInfrastructureLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<DishhiveDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Port=5433;Database=dishhive;Username=dishhive;Password=DishhiveP@ss2026"));

        // Register repositories
        // services.AddScoped<IRecipeRepository, RecipeRepository>();

        return services;
    }
}
