using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for the week planner. One planned meal per date + meal type slot;
/// at least one of recipe, dish name or vague instruction must be set.
/// Past rows double as the dish history (see docs/features/past-dishes-and-statistics.md).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlannedMealsController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly ILogger<PlannedMealsController> _logger;

    public PlannedMealsController(DishhiveDbContext context, ILogger<PlannedMealsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get planned meals in a date range (inclusive), e.g. one week
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PlannedMealDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<PlannedMealDto>>> GetMeals(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        if (from > to)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid range", Detail = "'from' must be before 'to'." });
        }

        var meals = await _context.PlannedMeals
            .AsNoTracking()
            .Include(m => m.Recipe)
            .Include(m => m.Attendees)
            .Where(m => m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.MealType)
            .ToListAsync();

        return Ok(meals.Select(ToDto));
    }

    /// <summary>
    /// Get a single planned meal by id
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PlannedMealDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlannedMealDto>> GetMeal(Guid id)
    {
        var meal = await _context.PlannedMeals
            .AsNoTracking()
            .Include(m => m.Recipe)
            .Include(m => m.Attendees)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (meal == null)
        {
            return NotFound();
        }

        return Ok(ToDto(meal));
    }

    /// <summary>
    /// Plan a meal in an empty slot
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlannedMealDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlannedMealDto>> CreateMeal(CreatePlannedMealDto dto)
    {
        var validationError = await ValidateContentAsync(dto);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        var slotTaken = await _context.PlannedMeals
            .AnyAsync(m => m.Date == dto.Date && m.MealType == dto.MealType);
        if (slotTaken)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Slot already planned",
                Detail = $"There is already a meal planned for {dto.Date:yyyy-MM-dd} ({dto.MealType})."
            });
        }

        var meal = new PlannedMeal { Date = dto.Date, MealType = dto.MealType };
        await ApplyDtoAsync(meal, dto);

        _context.PlannedMeals.Add(meal);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Planned meal {DishName} on {Date}", meal.DishName ?? meal.VagueInstruction, meal.Date);

        await _context.Entry(meal).Reference(m => m.Recipe).LoadAsync();
        return CreatedAtAction(nameof(GetMeal), new { id = meal.Id }, ToDto(meal));
    }

    /// <summary>
    /// Update a planned meal (replaces all fields including attendees)
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PlannedMealDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlannedMealDto>> UpdateMeal(Guid id, UpdatePlannedMealDto dto)
    {
        var meal = await _context.PlannedMeals
            .Include(m => m.Attendees)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (meal == null)
        {
            return NotFound();
        }

        var validationError = await ValidateContentAsync(dto);
        if (validationError != null)
        {
            return BadRequest(validationError);
        }

        var slotTaken = await _context.PlannedMeals
            .AnyAsync(m => m.Id != id && m.Date == dto.Date && m.MealType == dto.MealType);
        if (slotTaken)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Slot already planned",
                Detail = $"There is already a meal planned for {dto.Date:yyyy-MM-dd} ({dto.MealType})."
            });
        }

        meal.Date = dto.Date;
        meal.MealType = dto.MealType;
        meal.Attendees.Clear();
        await ApplyDtoAsync(meal, dto);

        await _context.SaveChangesAsync();

        await _context.Entry(meal).Reference(m => m.Recipe).LoadAsync();
        return Ok(ToDto(meal));
    }

    /// <summary>
    /// Remove a planned meal from the plan
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMeal(Guid id)
    {
        var meal = await _context.PlannedMeals.FindAsync(id);
        if (meal == null)
        {
            return NotFound();
        }

        _context.PlannedMeals.Remove(meal);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Validates the at-least-one-content rule and recipe existence
    /// </summary>
    private async Task<ProblemDetails?> ValidateContentAsync(CreatePlannedMealDto dto)
    {
        var hasContent = dto.RecipeId.HasValue
            || !string.IsNullOrWhiteSpace(dto.DishName)
            || !string.IsNullOrWhiteSpace(dto.VagueInstruction);

        if (!hasContent)
        {
            return new ProblemDetails
            {
                Title = "Empty meal plan",
                Detail = "Set a recipe, a dish name or a vague instruction."
            };
        }

        if (dto.RecipeId.HasValue
            && !await _context.Recipes.AnyAsync(r => r.Id == dto.RecipeId.Value))
        {
            return new ProblemDetails
            {
                Title = "Unknown recipe",
                Detail = $"Recipe '{dto.RecipeId}' does not exist."
            };
        }

        return null;
    }

    private async Task ApplyDtoAsync(PlannedMeal meal, CreatePlannedMealDto dto)
    {
        meal.RecipeId = dto.RecipeId;
        meal.VagueInstruction = NullIfEmpty(dto.VagueInstruction);
        meal.FreezyItemRef = NullIfEmpty(dto.FreezyItemRef);
        meal.Notes = NullIfEmpty(dto.Notes);

        // DishName is always denormalized from the recipe title when a recipe is linked,
        // so history survives recipe deletion or rename (see docs/features/week-planner.md)
        var dishName = NullIfEmpty(dto.DishName);
        if (dto.RecipeId.HasValue && dishName == null)
        {
            dishName = await _context.Recipes
                .Where(r => r.Id == dto.RecipeId.Value)
                .Select(r => r.Title)
                .FirstOrDefaultAsync();
        }
        meal.DishName = dishName;

        var memberIds = dto.FamilyMemberIds.Distinct().ToList();
        if (memberIds.Count > 0)
        {
            var existingIds = await _context.FamilyMembers
                .Where(m => memberIds.Contains(m.Id))
                .Select(m => m.Id)
                .ToListAsync();

            foreach (var memberId in existingIds)
            {
                meal.Attendees.Add(new PlannedMealAttendee { FamilyMemberId = memberId });
            }
        }
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static PlannedMealDto ToDto(PlannedMeal meal) => new()
    {
        Id = meal.Id,
        Date = meal.Date,
        MealType = meal.MealType,
        RecipeId = meal.RecipeId,
        RecipeTitle = meal.Recipe?.Title,
        DishName = meal.DishName,
        VagueInstruction = meal.VagueInstruction,
        FreezyItemRef = meal.FreezyItemRef,
        Notes = meal.Notes,
        AttendeeIds = meal.Attendees.Select(a => a.FamilyMemberId).ToList()
    };
}
