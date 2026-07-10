using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;

namespace Dishhive.Api.Services.Collections;

/// <summary>
/// Shared projection of recipes to the slim list DTO, used by the recipe library
/// list and the collection member list so both render identically.
/// </summary>
public static class RecipeListMapping
{
    public static IQueryable<RecipeListItemDto> Project(
        IQueryable<Recipe> query, IQueryable<CookbookEntry> cookbookEntries)
    {
        return query.Select(r => new RecipeListItemDto
        {
            Id = r.Id,
            Title = r.Title,
            Servings = r.Servings,
            TotalTimeMinutes = r.TotalTimeMinutes,
            Category = r.Category,
            // Remote URLs are references only. Rendering never reaches outside
            // Dishhive, even when an older import failed to download its image.
            ImageUrl = null,
            HasLocalImage = r.ImageData != null,
            SourceProvider = r.SourceProvider,
            Tags = r.Tags.Select(a => a.RecipeTag!.Name).OrderBy(n => n).ToList(),
            CookbookIds = cookbookEntries
                .Where(entry => entry.RecipeId == r.Id)
                .Select(entry => entry.CookbookId)
                .ToList()
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
