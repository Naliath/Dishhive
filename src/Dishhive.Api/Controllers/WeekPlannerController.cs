using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WeekPlannerController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IWeekPlanSuggestionProvider _suggestionProvider;

    public WeekPlannerController(DishhiveDbContext db, IWeekPlanSuggestionProvider suggestionProvider)
    {
        _db = db;
        _suggestionProvider = suggestionProvider;
    }

    /// <summary>Get the week plan for a given week. Creates an empty plan if none exists.</summary>
    [HttpGet("{weekStartDate}")]
    public async Task<ActionResult<WeekPlannerDtos.WeekPlanDto>> GetByWeek(DateOnly weekStartDate)
    {
        var plan = await LoadPlanWithMealsAsync(weekStartDate);

        if (plan == null)
            return NotFound();

        return Ok(await MapToDtoAsync(plan));
    }

    /// <summary>Get or create the week plan for a given week.</summary>
    [HttpPost]
    public async Task<ActionResult<WeekPlannerDtos.WeekPlanDto>> GetOrCreate([FromBody] WeekPlannerDtos.CreateWeekPlanDto dto)
    {
        var existing = await LoadPlanWithMealsAsync(dto.WeekStartDate);
        if (existing != null)
            return Ok(await MapToDtoAsync(existing));

        var plan = new WeekPlan { WeekStartDate = dto.WeekStartDate };
        _db.WeekPlans.Add(plan);
        await _db.SaveChangesAsync();

        plan.Meals = [];
        return CreatedAtAction(nameof(GetByWeek), new { weekStartDate = plan.WeekStartDate }, await MapToDtoAsync(plan));
    }

    /// <summary>Add or replace a meal slot for a day within a week plan.</summary>
    [HttpPost("{weekPlanId:guid}/meals")]
    public async Task<ActionResult<WeekPlannerDtos.PlannedMealDto>> UpsertMeal(
        Guid weekPlanId,
        [FromBody] WeekPlannerDtos.UpsertPlannedMealDto dto)
    {
        var plan = await _db.WeekPlans
            .Include(w => w.Meals)
            .FirstOrDefaultAsync(w => w.Id == weekPlanId);

        if (plan == null)
            return NotFound();

        // Find existing meal for this day+type slot
        var existing = plan.Meals.FirstOrDefault(m =>
            m.DayOfWeek == dto.DayOfWeek &&
            m.MealType == Enum.Parse<MealType>(dto.MealType, ignoreCase: true));

        if (existing != null)
        {
            // Update existing slot
            existing.RecipeId = dto.RecipeId;
            existing.VagueInstruction = dto.VagueInstruction;
            existing.IsFromFreezer = dto.IsFromFreezer;
            existing.FreezerItemId = dto.FreezerItemId;
            existing.Notes = dto.Notes;
            existing.AttendeeIds = dto.AttendeeIds ?? [];
        }
        else
        {
            existing = new PlannedMeal
            {
                WeekPlanId = weekPlanId,
                DayOfWeek = dto.DayOfWeek,
                MealType = Enum.Parse<MealType>(dto.MealType, ignoreCase: true),
                RecipeId = dto.RecipeId,
                VagueInstruction = dto.VagueInstruction,
                IsFromFreezer = dto.IsFromFreezer,
                FreezerItemId = dto.FreezerItemId,
                Notes = dto.Notes,
                AttendeeIds = dto.AttendeeIds ?? []
            };
            plan.Meals.Add(existing);
        }

        await _db.SaveChangesAsync();

        // Load recipe title for response
        string? recipeTitle = null;
        if (existing.RecipeId.HasValue)
            recipeTitle = await _db.Recipes
                .Where(r => r.Id == existing.RecipeId.Value)
                .Select(r => r.Title)
                .FirstOrDefaultAsync();

        return Ok(MapMealToDto(existing, recipeTitle));
    }

    /// <summary>Remove a meal from a week plan.</summary>
    [HttpDelete("{weekPlanId:guid}/meals/{mealId:guid}")]
    public async Task<IActionResult> DeleteMeal(Guid weekPlanId, Guid mealId)
    {
        var meal = await _db.PlannedMeals
            .FirstOrDefaultAsync(m => m.Id == mealId && m.WeekPlanId == weekPlanId);

        if (meal == null)
            return NotFound();

        _db.PlannedMeals.Remove(meal);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Get the shopping list for a week — aggregated ingredients across all planned recipes.</summary>
    [HttpGet("{weekStartDate}/shopping-list")]
    public async Task<ActionResult<List<ShoppingListItemDto>>> GetShoppingList(DateOnly weekStartDate)
    {
        var plan = await _db.WeekPlans
            .Include(w => w.Meals)
            .FirstOrDefaultAsync(w => w.WeekStartDate == weekStartDate);

        if (plan == null)
            return NotFound();

        var recipeIds = plan.Meals
            .Where(m => m.RecipeId.HasValue)
            .Select(m => m.RecipeId!.Value)
            .Distinct()
            .ToList();

        var ingredients = await _db.RecipeIngredients
            .Where(i => recipeIds.Contains(i.RecipeId))
            .Include(i => i.Recipe)
            .ToListAsync();

        // Group by ingredient name (case-insensitive) and aggregate quantities
        var grouped = ingredients
            .GroupBy(i => i.Name.Trim().ToLowerInvariant())
            .Select(g =>
            {
                var first = g.First();
                // Simple aggregation: sum quantities if same unit, otherwise list them separately
                var byUnit = g.GroupBy(i => (i.Unit ?? i.OriginalUnit ?? "").Trim().ToLowerInvariant())
                    .Select(ug => new
                    {
                        Unit = ug.First().Unit ?? ug.First().OriginalUnit,
                        TotalQty = ug.Sum(i => i.Quantity ?? i.OriginalQuantity)
                    }).ToList();

                return new ShoppingListItemDto(
                    Name: first.Name,
                    Amounts: byUnit.Select(u =>
                        u.TotalQty.HasValue && u.Unit != null
                            ? $"{u.TotalQty:0.##} {u.Unit}"
                            : u.TotalQty.HasValue
                                ? $"{u.TotalQty:0.##}"
                                : u.Unit ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList(),
                    RecipeNames: g.Select(i => i.Recipe.Title).Distinct().ToList()
                );
            })
            .OrderBy(i => i.Name)
            .ToList();

        return Ok(grouped);
    }

    private async Task<WeekPlan?> LoadPlanWithMealsAsync(DateOnly weekStartDate) =>
        await _db.WeekPlans
            .Include(w => w.Meals)
            .FirstOrDefaultAsync(w => w.WeekStartDate == weekStartDate);

    /// <summary>Get meal suggestions for a week from the configured provider (stub returns empty).</summary>
    [HttpGet("{weekStartDate}/suggest")]
    public async Task<ActionResult<object>> Suggest(DateOnly weekStartDate, CancellationToken ct)
    {
        if (!_suggestionProvider.IsAvailable)
            return StatusCode(StatusCodes.Status501NotImplemented,
                new { message = "Meal suggestion provider is not configured.", provider = _suggestionProvider.ProviderName });

        var suggestions = await _suggestionProvider.SuggestMealsAsync(weekStartDate, ct);
        return Ok(suggestions);
    }

    private async Task<WeekPlannerDtos.WeekPlanDto> MapToDtoAsync(WeekPlan plan)
    {
        var recipeIds = plan.Meals
            .Where(m => m.RecipeId.HasValue)
            .Select(m => m.RecipeId!.Value)
            .Distinct()
            .ToList();

        var recipeTitles = recipeIds.Any()
            ? await _db.Recipes
                .Where(r => recipeIds.Contains(r.Id))
                .Select(r => new { r.Id, r.Title })
                .ToDictionaryAsync(r => r.Id, r => r.Title)
            : new Dictionary<Guid, string>();

        return new WeekPlannerDtos.WeekPlanDto(
            plan.Id,
            plan.WeekStartDate,
            plan.Meals
                .OrderBy(m => m.DayOfWeek)
                .ThenBy(m => m.MealType)
                .Select(m => MapMealToDto(m, m.RecipeId.HasValue && recipeTitles.TryGetValue(m.RecipeId.Value, out var t) ? t : null))
                .ToList(),
            plan.CreatedAt,
            plan.UpdatedAt
        );
    }

    private static WeekPlannerDtos.PlannedMealDto MapMealToDto(PlannedMeal m, string? recipeTitle) => new(
        m.Id,
        m.DayOfWeek.ToString(),
        m.MealType.ToString(),
        m.RecipeId,
        recipeTitle,
        m.VagueInstruction,
        m.IsFromFreezer,
        m.FreezerItemId,
        m.Notes,
        m.AttendeeIds,
        m.CreatedAt,
        m.UpdatedAt
    );
}

public record ShoppingListItemDto(
    string Name,
    List<string> Amounts,
    List<string> RecipeNames
);
