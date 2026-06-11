using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Dish history statistics computed over planned meals
/// (past planner rows are the history; see docs/features/past-dishes-and-statistics.md)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class StatisticsController : ControllerBase
{
    private readonly DishhiveDbContext _context;

    public StatisticsController(DishhiveDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Dish frequency statistics: how often each dish was planned and when it was last planned.
    /// Optional date range; defaults to all history up to today.
    /// </summary>
    [HttpGet("dishes")]
    [ProducesResponseType(typeof(DishStatisticsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<DishStatisticsDto>> GetDishStatistics(
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        var rangeEnd = to ?? DateOnly.FromDateTime(DateTime.Today);

        var query = _context.PlannedMeals
            .AsNoTracking()
            .Where(m => m.Date <= rangeEnd);

        if (from.HasValue)
        {
            query = query.Where(m => m.Date >= from.Value);
        }

        var dishes = await query
            .Where(m => m.DishName != null)
            .GroupBy(m => m.DishName!)
            .Select(g => new DishStatisticDto
            {
                DishName = g.Key,
                TimesPlanned = g.Count(),
                LastPlanned = g.Max(m => m.Date),
                TimesEaten = g.Count(m => m.Eaten == EatenStatus.Eaten)
            })
            .OrderByDescending(d => d.TimesPlanned)
            .ThenByDescending(d => d.LastPlanned)
            .ToListAsync();

        await ApplyRatingAggregatesAsync(query, dishes);

        var unspecified = await query.CountAsync(m => m.DishName == null);

        return Ok(new DishStatisticsDto { Dishes = dishes, UnspecifiedCount = unspecified });
    }

    /// <summary>
    /// Attendance and most-eaten dishes for one family member
    /// </summary>
    [HttpGet("members/{id:guid}")]
    [ProducesResponseType(typeof(MemberStatisticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MemberStatisticsDto>> GetMemberStatistics(Guid id)
    {
        var member = await _context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        if (member == null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        var attendedMeals = _context.PlannedMealAttendees
            .AsNoTracking()
            .Where(a => a.FamilyMemberId == id)
            .Select(a => a.PlannedMeal!)
            .Where(m => m.Date <= today);

        var mealsAttended = await attendedMeals.CountAsync();
        var mealsEaten = await attendedMeals.CountAsync(m => m.Eaten == EatenStatus.Eaten);

        var memberRatings = await _context.MealRatings
            .AsNoTracking()
            .Where(r => r.FamilyMemberId == id)
            .Select(r => r.Rating)
            .ToListAsync();

        var topDishes = await attendedMeals
            .Where(m => m.DishName != null)
            .GroupBy(m => m.DishName!)
            .Select(g => new DishStatisticDto
            {
                DishName = g.Key,
                TimesPlanned = g.Count(),
                LastPlanned = g.Max(m => m.Date),
                TimesEaten = g.Count(m => m.Eaten == EatenStatus.Eaten)
            })
            .OrderByDescending(d => d.TimesPlanned)
            .ThenByDescending(d => d.LastPlanned)
            .Take(10)
            .ToListAsync();

        await ApplyRatingAggregatesAsync(attendedMeals, topDishes);

        return Ok(new MemberStatisticsDto
        {
            MemberId = member.Id,
            Name = member.Name,
            MealsAttended = mealsAttended,
            MealsEaten = mealsEaten,
            AverageRatingGiven = memberRatings.Count > 0 ? Math.Round(memberRatings.Average(), 2) : null,
            TopDishes = topDishes
        });
    }

    /// <summary>
    /// Fills AverageRating/LovedCount on the dish statistics from a second grouped
    /// query over ratings, merged in memory (keeps both queries trivially translatable)
    /// </summary>
    private async Task ApplyRatingAggregatesAsync(IQueryable<PlannedMeal> meals, List<DishStatisticDto> dishes)
    {
        var ratingsByDish = await meals
            .Where(m => m.DishName != null)
            .SelectMany(m => m.Ratings.Select(r => new { DishName = m.DishName!, r.Rating }))
            .GroupBy(x => x.DishName)
            .Select(g => new
            {
                DishName = g.Key,
                Average = g.Average(x => (double)x.Rating),
                LovedCount = g.Count(x => x.Rating >= 4)
            })
            .ToDictionaryAsync(x => x.DishName);

        foreach (var dish in dishes)
        {
            if (ratingsByDish.TryGetValue(dish.DishName, out var agg))
            {
                dish.AverageRating = Math.Round(agg.Average, 2);
                dish.LovedCount = agg.LovedCount;
            }
        }
    }
}
