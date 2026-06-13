using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services.Collections;

/// <summary>
/// Shared projection of recipes to the slim list DTO, used by the recipe library
/// list and the collection member list so both render identically.
/// </summary>
public static class RecipeListMapping
{
    public static IQueryable<RecipeListItemDto> Project(IQueryable<Recipe> query)
    {
        return query.Select(r => new RecipeListItemDto
        {
            Id = r.Id,
            Title = r.Title,
            Servings = r.Servings,
            TotalTimeMinutes = r.TotalTimeMinutes,
            Category = r.Category,
            ImageUrl = r.ImageData != null ? null : r.ImageUrl,
            HasLocalImage = r.ImageData != null,
            SourceProvider = r.SourceProvider,
            Tags = r.Tags.Select(a => a.RecipeTag!.Name).OrderBy(n => n).ToList()
        });
    }

    /// <summary>Points locally stored images at the image endpoint</summary>
    public static void ResolveLocalImageUrls(IEnumerable<RecipeListItemDto> recipes)
    {
        foreach (var recipe in recipes.Where(r => r.HasLocalImage))
        {
            recipe.ImageUrl = $"/api/recipes/{recipe.Id}/image";
        }
    }
}
