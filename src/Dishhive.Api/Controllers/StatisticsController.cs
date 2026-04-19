using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public StatisticsController(DishhiveDbContext db)
    {
        _db = db;
    }

    /// <summary>Overview stats: total recipes, family members, week plans covered.</summary>
    [HttpGet("overview")]
    public async Task<ActionResult<StatisticsOverviewDto>> GetOverview()
    {
        var recipeCount = await _db.Recipes.CountAsync();
        var familyCount = await _db.FamilyMembers.CountAsync(m => !m.IsGuest);
        var weekPlanCount = await _db.WeekPlans.CountAsync();
        var plannedMealCount = await _db.PlannedMeals.CountAsync();

        return Ok(new StatisticsOverviewDto(recipeCount, familyCount, weekPlanCount, plannedMealCount));
    }

    /// <summary>Top N most-planned recipes in the last N weeks.</summary>
    [HttpGet("top-recipes")]
    public async Task<ActionResult<List<TopRecipeDto>>> GetTopRecipes(
        [FromQuery] int top = 10,
        [FromQuery] int weeksBack = 12)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-weeksBack * 7));

        var topRecipes = await _db.PlannedMeals
            .Where(m => m.RecipeId.HasValue && m.WeekPlan.WeekStartDate >= since)
            .GroupBy(m => m.RecipeId!.Value)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(top)
            .Join(_db.Recipes, x => x.RecipeId, r => r.Id,
                (x, r) => new TopRecipeDto(r.Id, r.Title, r.PictureUrl, x.Count))
            .ToListAsync();

        return Ok(topRecipes);
    }

    /// <summary>How many times each day-of-week was planned in the last N weeks.</summary>
    [HttpGet("meal-frequency")]
    public async Task<ActionResult<List<DayFrequencyDto>>> GetMealFrequency([FromQuery] int weeksBack = 12)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-weeksBack * 7));

        var byDay = await _db.PlannedMeals
            .Where(m => m.WeekPlan.WeekStartDate >= since)
            .GroupBy(m => m.DayOfWeek)
            .Select(g => new DayFrequencyDto(g.Key.ToString(), g.Count()))
            .ToListAsync();

        return Ok(byDay);
    }

    /// <summary>Meals planned in the last N weeks, grouped by week.</summary>
    [HttpGet("recent-weeks")]
    public async Task<ActionResult<List<WeekSummaryDto>>> GetRecentWeeks([FromQuery] int weeksBack = 8)
    {
        var since = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-weeksBack * 7));

        var weeks = await _db.WeekPlans
            .Where(w => w.WeekStartDate >= since)
            .Include(w => w.Meals)
            .OrderByDescending(w => w.WeekStartDate)
            .Select(w => new WeekSummaryDto(
                w.WeekStartDate,
                w.Meals.Count,
                w.Meals.Count(m => m.RecipeId.HasValue),
                w.Meals.Count(m => m.IsFromFreezer)
            ))
            .ToListAsync();

        return Ok(weeks);
    }

    // ── Ratings ──────────────────────────────────────────────────────────────

    /// <summary>List ratings, optionally filtered by recipeId.</summary>
    [HttpGet("ratings")]
    public async Task<ActionResult<List<DishRatingDto>>> GetRatings([FromQuery] Guid? recipeId = null)
    {
        var query = _db.DishRatings.AsQueryable();
        if (recipeId.HasValue)
            query = query.Where(r => r.RecipeId == recipeId.Value);

        var ratings = await query
            .OrderByDescending(r => r.RatedOn)
            .Select(r => new DishRatingDto(
                r.Id, r.RecipeId, r.Stars, r.Comment, r.FamilyMemberId, r.RatedOn, r.CreatedAt))
            .ToListAsync();

        return Ok(ratings);
    }

    /// <summary>Add a star rating for a recipe.</summary>
    [HttpPost("ratings")]
    public async Task<ActionResult<DishRatingDto>> AddRating([FromBody] CreateDishRatingDto dto)
    {
        if (dto.Stars < 1 || dto.Stars > 5)
            return BadRequest("Stars must be between 1 and 5.");

        var recipeExists = await _db.Recipes.AnyAsync(r => r.Id == dto.RecipeId);
        if (!recipeExists)
            return NotFound($"Recipe {dto.RecipeId} not found.");

        if (dto.FamilyMemberId.HasValue)
        {
            var memberExists = await _db.FamilyMembers.AnyAsync(m => m.Id == dto.FamilyMemberId.Value);
            if (!memberExists)
                return NotFound($"Family member {dto.FamilyMemberId} not found.");
        }

        var rating = new DishRating
        {
            RecipeId = dto.RecipeId,
            Stars = dto.Stars,
            Comment = dto.Comment?.Trim(),
            FamilyMemberId = dto.FamilyMemberId,
            RatedOn = dto.RatedOn ?? DateOnly.FromDateTime(DateTime.UtcNow)
        };

        _db.DishRatings.Add(rating);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetRatings),
            new DishRatingDto(rating.Id, rating.RecipeId, rating.Stars, rating.Comment,
                              rating.FamilyMemberId, rating.RatedOn, rating.CreatedAt));
    }

    /// <summary>Delete a rating.</summary>
    [HttpDelete("ratings/{id:guid}")]
    public async Task<IActionResult> DeleteRating(Guid id)
    {
        var rating = await _db.DishRatings.FindAsync(id);
        if (rating == null)
            return NotFound();

        _db.DishRatings.Remove(rating);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record StatisticsOverviewDto(int RecipeCount, int FamilyMemberCount, int WeekPlanCount, int PlannedMealCount);
public record TopRecipeDto(Guid RecipeId, string Title, string? PictureUrl, int TimesPlanned);
public record DayFrequencyDto(string DayOfWeek, int Count);
public record WeekSummaryDto(DateOnly WeekStartDate, int TotalMeals, int MealsWithRecipe, int MealsFromFreezer);
public record DishRatingDto(Guid Id, Guid RecipeId, int Stars, string? Comment, Guid? FamilyMemberId, DateOnly RatedOn, DateTime CreatedAt);
public record CreateDishRatingDto(Guid RecipeId, int Stars, string? Comment, Guid? FamilyMemberId, DateOnly? RatedOn);
