namespace Dishhive.Application.DependencyInjection;

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using FluentValidation;

/// <summary>
/// Extension to register all application layer services.
/// </summary>
public static class ApplicationDependencyInjection
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        // Register validators from this assembly
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        return services;
    }
}
