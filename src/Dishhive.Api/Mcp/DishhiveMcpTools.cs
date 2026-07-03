using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;

namespace Dishhive.Api.Mcp;

/// <summary>
/// Read-only MCP tools exposing the household's planning data to local AI clients
/// (LM Studio etc.) via the streamable-HTTP endpoint at /mcp (see Program.cs and
/// docs/features/mcp-server.md). Deliberately designed for small local models:
/// dates travel as "yyyy-MM-dd" strings with clear error messages, member and
/// recipe names are resolved server-side (ids mean nothing to an LLM), and
/// payloads stay compact. One instance is created per invocation from request DI,
/// so the scoped DbContext injects like in a controller.
/// </summary>
[McpServerToolType]
public class DishhiveMcpTools
{
    private const int SearchResultCap = 25;

    private readonly DishhiveDbContext _context;

    public DishhiveMcpTools(DishhiveDbContext context)
    {
        _context = context;
    }

    [McpServerTool(Name = "get_week_plan", ReadOnly = true, Idempotent = true)]
    [Description("Gets the planned meals for a week (Monday through Sunday). " +
        "Returns every planned meal with its dish, meal type, attendees and eaten status.")]
    public async Task<object> GetWeekPlan(
        [Description("Any date inside the wanted week as yyyy-MM-dd; defaults to the current week. " +
            "The week always starts on Monday.")] string? date = null,
        CancellationToken cancellationToken = default)
    {
        DateOnly reference;
        if (date is null)
        {
            reference = DateOnly.FromDateTime(DateTime.Today);
        }
        else if (!TryParseDate(date, out reference))
        {
            return InvalidDate(date);
        }

        // Normalize to the week's Monday, mirroring the planner UI
        var weekStart = reference.AddDays(-(((int)reference.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7));
        var weekEnd = weekStart.AddDays(6);

        var meals = await LoadMealsAsync(weekStart, weekEnd, cancellationToken);
        var memberNames = await MemberNamesAsync(cancellationToken);

        return new
        {
            weekStart = weekStart.ToString("yyyy-MM-dd"),
            weekEnd = weekEnd.ToString("yyyy-MM-dd"),
            meals = meals.Select(m => new
            {
                date = m.Date.ToString("yyyy-MM-dd"),
                dayOfWeek = m.Date.DayOfWeek.ToString(),
                mealType = m.MealType.ToString(),
                course = m.Course.ToString(),
                dish = m.DishName,
                vagueInstruction = m.VagueInstruction,
                notes = m.Notes,
                attendees = AttendeeNames(m, memberNames),
                eaten = EatenLabel(m.Eaten),
                fromFreezer = m.FreezyItemRef != null,
                // For chaining into get_recipe
                recipeId = m.RecipeId?.ToString()
            }).ToList()
        };
    }

    [McpServerTool(Name = "get_eaten_on_date", ReadOnly = true, Idempotent = true)]
    [Description("Gets what was planned and eaten on one specific date, including whether each " +
        "meal was actually eaten or skipped and how each family member rated it (1-5 stars).")]
    public async Task<object> GetEatenOnDate(
        [Description("The date as yyyy-MM-dd")] string date,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseDate(date, out var day))
        {
            return InvalidDate(date);
        }

        var meals = await LoadMealsAsync(day, day, cancellationToken);
        if (meals.Count == 0)
        {
            return $"Nothing was planned on {day:yyyy-MM-dd}.";
        }

        var memberNames = await MemberNamesAsync(cancellationToken);

        return new
        {
            date = day.ToString("yyyy-MM-dd"),
            dayOfWeek = day.DayOfWeek.ToString(),
            meals = meals.Select(m => new
            {
                mealType = m.MealType.ToString(),
                course = m.Course.ToString(),
                dish = m.DishName,
                vagueInstruction = m.VagueInstruction,
                eaten = EatenLabel(m.Eaten),
                attendees = AttendeeNames(m, memberNames),
                ratings = m.Ratings
                    .OrderBy(r => memberNames.GetValueOrDefault(r.FamilyMemberId, ""))
                    .Select(r => new
                    {
                        member = memberNames.GetValueOrDefault(r.FamilyMemberId, "unknown member"),
                        rating = r.Rating
                    })
                    .ToList(),
                notes = m.Notes,
                recipeId = m.RecipeId?.ToString()
            }).ToList()
        };
    }

    [McpServerTool(Name = "search_recipes", ReadOnly = true, Idempotent = true)]
    [Description("Searches the household's stored recipes. All filters are optional and combine; " +
        "without any filter the first recipes by title are returned. " +
        "Use get_recipe with a result's id to read the full ingredients and steps.")]
    public async Task<object> SearchRecipes(
        [Description("Text matched case-insensitively against recipe titles and keywords")] string? query = null,
        [Description("Exact category name (e.g. \"Hoofdgerecht\")")] string? category = null,
        [Description("Comma-separated tag names; a recipe must carry all of them")] string? tags = null,
        CancellationToken cancellationToken = default)
    {
        // Same filter semantics as GET /api/recipes (RecipesController.GetRecipes)
        var recipes = _context.Recipes.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLower();
            recipes = recipes.Where(r =>
                r.Title.ToLower().Contains(term) ||
                (r.Keywords != null && r.Keywords.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var wanted = category.Trim().ToLower();
            recipes = recipes.Where(r => r.Category != null && r.Category.ToLower() == wanted);
        }

        foreach (var tag in (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var wanted = tag.ToLower();
            recipes = recipes.Where(r => r.Tags.Any(a => a.RecipeTag!.Name.ToLower() == wanted));
        }

        var totalMatches = await recipes.CountAsync(cancellationToken);
        var results = await recipes
            .OrderBy(r => r.Title)
            .Take(SearchResultCap)
            .Select(r => new
            {
                id = r.Id.ToString(),
                title = r.Title,
                category = r.Category,
                servings = r.Servings,
                totalTimeMinutes = r.TotalTimeMinutes,
                tags = r.Tags.Where(a => a.RecipeTag != null).Select(a => a.RecipeTag!.Name).ToList(),
                // Dietary facts make allergy questions answerable from search hits
                factsAssessed = r.DietaryFactsStatus != DietaryFactsStatus.Unassessed,
                contains = r.DietaryFacts.Select(f => f.IngredientClass.ToString()).ToList()
            })
            .ToListAsync(cancellationToken);

        return new
        {
            totalMatches,
            returned = results.Count,
            note = totalMatches > results.Count
                ? $"Showing the first {results.Count} of {totalMatches} matches (ordered by title); narrow the search to see the rest."
                : null,
            recipes = results
        };
    }

    [McpServerTool(Name = "get_recipe", ReadOnly = true, Idempotent = true)]
    [Description("Gets one stored recipe in full: description, ingredients, preparation steps, " +
        "times and dietary facts. Use search_recipes first to find the recipe id.")]
    public async Task<object> GetRecipe(
        [Description("The recipe id from a search_recipes result")] string recipeId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(recipeId, out var id))
        {
            return $"\"{recipeId}\" is not a valid recipe id; use the id field from a search_recipes result.";
        }

        var recipe = await _context.Recipes
            .AsNoTracking()
            .Include(r => r.Ingredients)
            .Include(r => r.Steps)
            .Include(r => r.Tags).ThenInclude(a => a.RecipeTag)
            .Include(r => r.DietaryFacts)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recipe == null)
        {
            return $"No recipe with id {id} exists; use search_recipes to find valid ids.";
        }

        return new
        {
            id = recipe.Id.ToString(),
            title = recipe.Title,
            description = recipe.Description,
            servings = recipe.Servings,
            prepTimeMinutes = recipe.PrepTimeMinutes,
            cookTimeMinutes = recipe.CookTimeMinutes,
            totalTimeMinutes = recipe.TotalTimeMinutes,
            category = recipe.Category,
            tags = recipe.Tags.Where(a => a.RecipeTag != null).Select(a => a.RecipeTag!.Name).OrderBy(n => n).ToList(),
            // The verbatim source line reads best ("2 el olijfolie"), composed as fallback
            ingredients = recipe.Ingredients
                .OrderBy(i => i.SortOrder)
                .Select(i => string.IsNullOrWhiteSpace(i.OriginalText)
                    ? string.Join(' ', new[] { i.Quantity?.ToString("0.###", CultureInfo.InvariantCulture), i.Unit, i.Name }
                        .Where(part => !string.IsNullOrWhiteSpace(part)))
                    : i.OriginalText)
                .ToList(),
            steps = recipe.Steps.OrderBy(s => s.StepNumber).Select(s => s.Instruction).ToList(),
            dietaryFacts = new
            {
                assessed = recipe.DietaryFactsStatus != DietaryFactsStatus.Unassessed,
                status = recipe.DietaryFactsStatus.ToString(),
                contains = IngredientClasses.ToNames(recipe.DietaryFacts.Select(f => f.IngredientClass))
            },
            sourceUrl = recipe.SourceUrl
        };
    }

    private async Task<List<PlannedMeal>> LoadMealsAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        // Same query shape as GET /api/plannedmeals (PlannedMealsController.GetMeals)
        return await _context.PlannedMeals
            .AsNoTracking()
            .Include(m => m.Attendees)
            .Include(m => m.Ratings)
            .Where(m => m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.MealType)
            .ThenBy(m => m.Course)
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> MemberNamesAsync(CancellationToken cancellationToken)
    {
        return await _context.FamilyMembers
            .AsNoTracking()
            .ToDictionaryAsync(m => m.Id, m => m.Name, cancellationToken);
    }

    private static List<string> AttendeeNames(PlannedMeal meal, Dictionary<Guid, string> memberNames) => meal.Attendees
        .Select(a => memberNames.GetValueOrDefault(a.FamilyMemberId, "unknown member"))
        .OrderBy(n => n)
        .ToList();

    /// <summary>Eaten is nullable — "not marked" is a real state, distinct from skipped</summary>
    private static string EatenLabel(EatenStatus? status) => status switch
    {
        EatenStatus.Eaten => "eaten",
        EatenStatus.Skipped => "skipped",
        _ => "not marked"
    };

    private static bool TryParseDate(string value, out DateOnly date) =>
        DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private static string InvalidDate(string value) =>
        $"\"{value}\" is not a valid date; use the yyyy-MM-dd format (e.g. 2026-07-03).";
}
