using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Services.Suggestions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for the week planner. A day can hold any number of dishes
/// (e.g. lunch plus a dinner with appetizer and dessert);
/// at least one of recipe, dish name or vague instruction must be set.
/// Past rows double as the dish history (see docs/features/past-dishes-and-statistics.md).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PlannedMealsController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly IMealSuggestionService _suggestionService;
    private readonly MealSuggestionRequestBuilder _suggestionRequestBuilder;
    private readonly ILogger<PlannedMealsController> _logger;

    public PlannedMealsController(
        DishhiveDbContext context,
        IMealSuggestionService suggestionService,
        MealSuggestionRequestBuilder suggestionRequestBuilder,
        ILogger<PlannedMealsController> logger)
    {
        _context = context;
        _suggestionService = suggestionService;
        _suggestionRequestBuilder = suggestionRequestBuilder;
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
            .Include(m => m.Ratings)
            .Where(m => m.Date >= from && m.Date <= to)
            .OrderBy(m => m.Date)
            .ThenBy(m => m.MealType)
            .ThenBy(m => m.Course)
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
            .Include(m => m.Ratings)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (meal == null)
        {
            return NotFound();
        }

        return Ok(ToDto(meal));
    }

    /// <summary>
    /// Plan a dish; a day can hold any number of dishes
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

        var meal = new PlannedMeal { Date = dto.Date, MealType = dto.MealType, Course = dto.Course };
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

        meal.Date = dto.Date;
        meal.MealType = dto.MealType;
        meal.Course = dto.Course;
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
        // Dependents loaded so cascade also applies on providers without FK
        // enforcement (EF InMemory in tests); Postgres cascades via FK anyway
        var meal = await _context.PlannedMeals
            .Include(m => m.Attendees)
            .Include(m => m.Ratings)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (meal == null)
        {
            return NotFound();
        }

        _context.PlannedMeals.Remove(meal);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Whether AI week-plan suggestions are available (drives the planner UI)
    /// </summary>
    [HttpGet("suggestions/status")]
    [ProducesResponseType(typeof(SuggestionStatusDto), StatusCodes.Status200OK)]
    public ActionResult<SuggestionStatusDto> GetSuggestionStatus()
    {
        return Ok(new SuggestionStatusDto { Enabled = _suggestionService.IsEnabled });
    }

    /// <summary>
    /// Propose dinners for the unplanned days of a week. Proposals only — nothing
    /// is persisted; accepted suggestions are created via POST /api/plannedmeals.
    /// Returns enabled=false with an empty list when AI is not configured.
    /// </summary>
    [HttpPost("suggestions")]
    [ProducesResponseType(typeof(MealSuggestionsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MealSuggestionsDto>> SuggestWeek(
        SuggestWeekRequestDto dto, CancellationToken cancellationToken)
    {
        if (!_suggestionService.IsEnabled)
        {
            return Ok(new MealSuggestionsDto { Enabled = false });
        }

        var request = await _suggestionRequestBuilder.BuildAsync(
            dto.WeekStart, dto.AttendeeIds, cancellationToken);
        var suggestions = await _suggestionService.SuggestAsync(request, cancellationToken);

        var recipeTitles = request.KnownRecipes.ToDictionary(r => r.Id, r => r.Title);

        return Ok(new MealSuggestionsDto
        {
            Enabled = true,
            Suggestions = suggestions.Select(s => new MealSuggestionDto
            {
                Date = s.Date,
                RecipeId = s.RecipeId,
                RecipeTitle = s.RecipeId.HasValue ? recipeTitles.GetValueOrDefault(s.RecipeId.Value) : null,
                DishName = s.DishName ?? string.Empty,
                Reason = s.Reason
            }).ToList()
        });
    }

    /// <summary>
    /// Mark a past meal as eaten or skipped; a null status clears the mark
    /// </summary>
    [HttpPut("{id:guid}/eaten")]
    [ProducesResponseType(typeof(PlannedMealDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlannedMealDto>> SetEaten(Guid id, SetEatenDto dto)
    {
        var meal = await _context.PlannedMeals
            .Include(m => m.Recipe)
            .Include(m => m.Attendees)
            .Include(m => m.Ratings)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (meal == null)
        {
            return NotFound();
        }

        if (meal.Date > DateOnly.FromDateTime(DateTime.Today))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Meal is in the future",
                Detail = "A meal can only be marked eaten or skipped on or after its planned date."
            });
        }

        meal.Eaten = dto.Status;
        await _context.SaveChangesAsync();

        return Ok(ToDto(meal));
    }

    /// <summary>
    /// Set a family member's 1–5 rating for a past meal; re-rating overwrites
    /// </summary>
    [HttpPut("{id:guid}/ratings/{memberId:guid}")]
    [ProducesResponseType(typeof(PlannedMealDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlannedMealDto>> SetRating(Guid id, Guid memberId, SetRatingDto dto)
    {
        var meal = await _context.PlannedMeals
            .Include(m => m.Recipe)
            .Include(m => m.Attendees)
            .Include(m => m.Ratings)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (meal == null)
        {
            return NotFound();
        }

        if (meal.Date > DateOnly.FromDateTime(DateTime.Today))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Meal is in the future",
                Detail = "A meal can only be rated on or after its planned date."
            });
        }

        // The rater must exist but need not be an attendee (someone may have joined unplanned)
        if (!await _context.FamilyMembers.AnyAsync(m => m.Id == memberId))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Unknown family member",
                Detail = $"Family member '{memberId}' does not exist."
            });
        }

        var rating = meal.Ratings.FirstOrDefault(r => r.FamilyMemberId == memberId);
        if (rating == null)
        {
            meal.Ratings.Add(new MealRating { FamilyMemberId = memberId, Rating = dto.Rating });
        }
        else
        {
            rating.Rating = dto.Rating;
        }

        await _context.SaveChangesAsync();

        return Ok(ToDto(meal));
    }

    /// <summary>
    /// Remove a family member's rating from a meal
    /// </summary>
    [HttpDelete("{id:guid}/ratings/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteRating(Guid id, Guid memberId)
    {
        var rating = await _context.MealRatings
            .FirstOrDefaultAsync(r => r.PlannedMealId == id && r.FamilyMemberId == memberId);

        if (rating == null)
        {
            return NotFound();
        }

        _context.MealRatings.Remove(rating);
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
        Course = meal.Course,
        RecipeId = meal.RecipeId,
        RecipeTitle = meal.Recipe?.Title,
        DishName = meal.DishName,
        VagueInstruction = meal.VagueInstruction,
        FreezyItemRef = meal.FreezyItemRef,
        Notes = meal.Notes,
        Eaten = meal.Eaten,
        AttendeeIds = meal.Attendees.Select(a => a.FamilyMemberId).ToList(),
        Ratings = meal.Ratings
            .Select(r => new MealRatingDto { FamilyMemberId = r.FamilyMemberId, Rating = r.Rating })
            .ToList()
    };
}
