using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Services.Import;

public interface IRecipeExchangeService
{
    /// <summary>Exports the whole recipe library as a schema.org Recipe JSON document</summary>
    Task<string> ExportAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports recipes from a schema.org Recipe JSON file (a single Recipe object, an
    /// array, or an @graph document — including Dishhive's own export).
    /// </summary>
    /// <exception cref="JsonException">The file is not valid JSON</exception>
    /// <exception cref="RecipeExtractionFailedException">The JSON contains no Recipe objects</exception>
    Task<RecipeFileImportResultDto> ImportAsync(Stream json, CancellationToken cancellationToken = default);
}

/// <summary>
/// Recipe library exchange in schema.org Recipe JSON — the format the import pipeline
/// already speaks and the one other recipe managers (Mealie, Tandoor, Nextcloud Cookbook)
/// understand. Exports are self-contained: locally stored images are embedded as data URIs.
/// Dishhive's organization tags travel in a "dishhive:tags" extension property that other
/// tools simply ignore. See docs/features/recipe-import-export.md.
/// </summary>
public partial class RecipeExchangeService : IRecipeExchangeService
{
    /// <summary>Provider key recorded on recipes created by file import</summary>
    public const string FileProviderKey = "file-import";

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        // keep accented ingredient text human-readable in the exported file
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [GeneratedRegex(@"^data:(?<type>image/[a-zA-Z0-9.+-]+);base64,(?<payload>.+)$", RegexOptions.Singleline)]
    private static partial Regex ImageDataUriRegex();

    private readonly HttpClient _httpClient;
    private readonly DishhiveDbContext _context;
    private readonly ILogger<RecipeExchangeService> _logger;

    public RecipeExchangeService(
        HttpClient httpClient,
        DishhiveDbContext context,
        ILogger<RecipeExchangeService> logger)
    {
        _httpClient = httpClient;
        _context = context;
        _logger = logger;
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var recipes = await _context.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
            .OrderBy(r => r.Title)
            .ToListAsync(cancellationToken);

        var graph = new JsonArray();
        foreach (var recipe in recipes)
        {
            graph.Add(ToSchemaOrg(recipe));
        }

        var document = new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@graph"] = graph
        };

        return document.ToJsonString(ExportOptions);
    }

    public async Task<RecipeFileImportResultDto> ImportAsync(Stream json, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: cancellationToken);
        var nodes = SchemaOrgRecipeExtractor.FindRecipeNodes(document.RootElement);
        if (nodes.Count == 0)
        {
            throw new RecipeExtractionFailedException("The file contains no schema.org Recipe objects.");
        }

        // identity snapshots for duplicate detection, kept in sync as we create recipes
        var existing = await _context.Recipes
            .Select(r => new { r.Id, r.Title, r.SourceUrl })
            .ToListAsync(cancellationToken);
        var knownTitles = existing.Select(r => r.Title.Trim().ToLowerInvariant()).ToHashSet();
        var knownUrls = existing
            .Where(r => r.SourceUrl != null)
            .ToDictionary(r => r.SourceUrl!, r => r.Id);
        var allTags = await _context.RecipeTags.ToListAsync(cancellationToken);

        var result = new RecipeFileImportResultDto();

        foreach (var node in nodes)
        {
            var imported = SchemaOrgRecipeExtractor.MapRecipeNode(node);
            if (imported == null)
            {
                result.SkippedRecipes.Add(new RecipeFileImportSkippedDto
                {
                    Title = "(unnamed)",
                    Reason = "The recipe has no name."
                });
                continue;
            }

            // only an absolute http(s) URL counts as identity — schema.org @id is often
            // just a local fragment like "#recipe"
            var sourceUrl = IsHttpUrl(imported.SourceUrl) ? imported.SourceUrl : null;

            Recipe recipe;
            if (sourceUrl != null && knownUrls.TryGetValue(sourceUrl, out var existingId))
            {
                // same source page: update, consistent with re-importing a URL
                recipe = await _context.Recipes
                    .Include(r => r.Ingredients)
                    .Include(r => r.Steps)
                    .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
                    .FirstAsync(r => r.Id == existingId, cancellationToken);
                _context.RecipeIngredients.RemoveRange(recipe.Ingredients);
                _context.RecipeSteps.RemoveRange(recipe.Steps);
                recipe.Ingredients.Clear();
                recipe.Steps.Clear();
                result.Updated++;
            }
            else if (knownTitles.Contains(imported.Title.Trim().ToLowerInvariant()))
            {
                // same title without a shared source URL: keep the library's version
                result.SkippedRecipes.Add(new RecipeFileImportSkippedDto
                {
                    Title = imported.Title,
                    Reason = "A recipe with this title is already in the library."
                });
                continue;
            }
            else
            {
                recipe = new Recipe();
                _context.Recipes.Add(recipe);
                knownTitles.Add(imported.Title.Trim().ToLowerInvariant());
                result.Created++;
            }

            RecipeImportService.ApplyImportedRecipe(recipe, imported, sourceUrl, FileProviderKey);
            ApplyImage(recipe, imported.ImageUrl);
            if (recipe.ImageData == null)
            {
                await RecipeImageDownloader.TryDownloadAsync(_httpClient, recipe, _logger, cancellationToken);
            }
            SyncTags(recipe, ReadDishhiveTags(node), allTags);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Recipe file import: {Created} created, {Updated} updated, {Skipped} skipped",
            result.Created, result.Updated, result.Skipped);
        return result;
    }

    private static JsonObject ToSchemaOrg(Recipe recipe)
    {
        var node = new JsonObject
        {
            ["@type"] = "Recipe",
            ["name"] = recipe.Title
        };

        if (recipe.Description != null) { node["description"] = recipe.Description; }
        node["recipeYield"] = recipe.Servings;
        if (recipe.PrepTimeMinutes is int prep) { node["prepTime"] = $"PT{prep}M"; }
        if (recipe.CookTimeMinutes is int cook) { node["cookTime"] = $"PT{cook}M"; }
        if (recipe.TotalTimeMinutes is int total) { node["totalTime"] = $"PT{total}M"; }
        if (recipe.Category != null) { node["recipeCategory"] = recipe.Category; }
        if (recipe.Keywords != null) { node["keywords"] = recipe.Keywords; }
        if (recipe.SourceUrl != null) { node["url"] = recipe.SourceUrl; }
        if (recipe.VideoUrl != null) { node["video"] = recipe.VideoUrl; }

        // self-contained export: embed locally stored image bytes as a data URI
        if (recipe.ImageData != null)
        {
            node["image"] = $"data:{recipe.ImageContentType ?? "image/jpeg"};base64,{Convert.ToBase64String(recipe.ImageData)}";
        }
        else if (recipe.ImageUrl != null)
        {
            node["image"] = recipe.ImageUrl;
        }

        var ingredients = new JsonArray();
        foreach (var ingredient in recipe.Ingredients.OrderBy(i => i.SortOrder))
        {
            ingredients.Add((JsonNode)IngredientLine(ingredient));
        }
        node["recipeIngredient"] = ingredients;

        var instructions = new JsonArray();
        foreach (var step in recipe.Steps.OrderBy(s => s.StepNumber))
        {
            instructions.Add(new JsonObject
            {
                ["@type"] = "HowToStep",
                ["text"] = step.Instruction
            });
        }
        node["recipeInstructions"] = instructions;

        node["dateCreated"] = recipe.CreatedAt.ToString("o", CultureInfo.InvariantCulture);
        node["dateModified"] = recipe.UpdatedAt.ToString("o", CultureInfo.InvariantCulture);

        var tags = recipe.Tags
            .Where(a => a.RecipeTag != null)
            .Select(a => a.RecipeTag!.Name)
            .OrderBy(n => n)
            .ToList();
        if (tags.Count > 0)
        {
            var tagArray = new JsonArray();
            foreach (var tag in tags)
            {
                tagArray.Add((JsonNode)tag);
            }
            // Dishhive extension, ignored by other tools; restored on import
            node["dishhive:tags"] = tagArray;
        }

        return node;
    }

    /// <summary>
    /// The verbatim source line is the canonical interchange form; composed from the
    /// structured values when a manually entered ingredient has no original text.
    /// </summary>
    private static string IngredientLine(RecipeIngredient ingredient)
    {
        if (!string.IsNullOrWhiteSpace(ingredient.OriginalText))
        {
            return ingredient.OriginalText;
        }

        var quantity = ingredient.Quantity?.ToString("0.###", CultureInfo.InvariantCulture);
        return string.Join(' ', new[] { quantity, ingredient.Unit, ingredient.Name }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    /// <summary>Decodes a data-URI image into local bytes; remote URLs stay on ImageUrl</summary>
    private void ApplyImage(Recipe recipe, string? image)
    {
        if (image == null || !image.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // data URIs never belong in ImageUrl (column max 1000 chars)
        recipe.ImageUrl = null;

        var match = ImageDataUriRegex().Match(image);
        if (!match.Success)
        {
            _logger.LogWarning("Recipe {Title} has an unparseable image data URI; importing without image", recipe.Title);
            return;
        }

        try
        {
            var bytes = Convert.FromBase64String(match.Groups["payload"].Value);
            if (bytes.Length == 0 || bytes.Length > RecipeImageDownloader.MaxImageBytes)
            {
                return;
            }
            recipe.ImageData = bytes;
            recipe.ImageContentType = match.Groups["type"].Value;
        }
        catch (FormatException)
        {
            _logger.LogWarning("Recipe {Title} has invalid base64 image data; importing without image", recipe.Title);
        }
    }

    private static List<string> ReadDishhiveTags(JsonElement node)
    {
        if (!node.TryGetProperty("dishhive:tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return tags.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!.Trim())
            .Where(t => t.Length > 0 && t.Length <= 50)
            .ToList();
    }

    /// <summary>Links tags by name, creating missing ones (reused case-insensitively)</summary>
    private void SyncTags(Recipe recipe, List<string> tagNames, List<RecipeTag> allTags)
    {
        foreach (var name in tagNames.DistinctBy(n => n.ToLowerInvariant()))
        {
            var alreadyLinked = recipe.Tags.Any(a =>
                a.RecipeTag != null && string.Equals(a.RecipeTag.Name, name, StringComparison.OrdinalIgnoreCase));
            if (alreadyLinked)
            {
                continue;
            }

            var tag = allTags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag == null)
            {
                tag = new RecipeTag { Name = name };
                _context.RecipeTags.Add(tag);
                allTags.Add(tag);
            }

            recipe.Tags.Add(new RecipeTagAssignment { Recipe = recipe, RecipeTag = tag });
        }
    }

    private static bool IsHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
