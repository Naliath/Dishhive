namespace Dishhive.Api.Models;

/// <summary>Shared persistence and wire-contract limits for domain-owned text.</summary>
public static class DomainLimits
{
    public const int RecipeTitle = 300;
    public const int DishName = RecipeTitle;
}
