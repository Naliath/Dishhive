using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Planning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/week-plans")]
[Produces("application/json")]
public class WeekPlansController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public WeekPlansController(DishhiveDbContext db) => _db = db;

    /// <summary>
    /// Lists all week plans, or returns the single plan whose week contains the given date when
    /// <paramref name="weekStart"/> is provided. Auto-creates an empty plan with all 7×3
    /// (Breakfast/Lunch/Dinner) slots if none exists for that week.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<WeekPlanDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<WeekPlanDto>>> List([FromQuery] DateOnly? weekStart, CancellationToken ct)
    {
        if (weekStart is { } ws)
        {
            var monday = NormalizeToMonday(ws);
            var plan = await LoadPlanAsync(monday, ct) ?? await CreateEmptyPlanAsync(monday, ct);
            return Ok(new[] { await ToDtoAsync(plan, ct) });
        }

        var plans = await _db.WeekPlans
            .Include(p => p.Slots).ThenInclude(s => s.Attendees)
            .OrderByDescending(p => p.WeekStart)
            .ToListAsync(ct);

        var titles = await LoadRecipeTitlesAsync(plans.SelectMany(p => p.Slots), ct);
        return Ok(plans.Select(p => ToDto(p, titles)));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(WeekPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeekPlanDto>> Get(Guid id, CancellationToken ct)
    {
        var plan = await _db.WeekPlans
            .Include(p => p.Slots).ThenInclude(s => s.Attendees)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        return plan is null ? NotFound() : Ok(await ToDtoAsync(plan, ct));
    }

    [HttpPost]
    [ProducesResponseType(typeof(WeekPlanDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<WeekPlanDto>> Create(CreateWeekPlanDto dto, CancellationToken ct)
    {
        var monday = NormalizeToMonday(dto.WeekStart);
        var existing = await _db.WeekPlans.FirstOrDefaultAsync(p => p.WeekStart == monday, ct);
        if (existing is not null)
            return BadRequest(new { message = $"A week plan for {monday:yyyy-MM-dd} already exists." });

        var plan = await CreateEmptyPlanAsync(monday, ct);
        plan.Notes = dto.Notes;
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = plan.Id }, await ToDtoAsync(plan, ct));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(WeekPlanDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WeekPlanDto>> Update(Guid id, UpdateWeekPlanDto dto, CancellationToken ct)
    {
        var plan = await _db.WeekPlans.FindAsync(new object?[] { id }, ct);
        if (plan is null) return NotFound();

        plan.Notes = dto.Notes;
        await _db.SaveChangesAsync(ct);

        var loaded = await _db.WeekPlans
            .Include(p => p.Slots).ThenInclude(s => s.Attendees)
            .FirstAsync(p => p.Id == id, ct);
        return Ok(await ToDtoAsync(loaded, ct));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var plan = await _db.WeekPlans.FindAsync(new object?[] { id }, ct);
        if (plan is null) return NotFound();
        _db.WeekPlans.Remove(plan);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/slots/{slotId:guid}")]
    [ProducesResponseType(typeof(MealSlotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MealSlotDto>> UpdateSlot(Guid id, Guid slotId, UpdateMealSlotDto dto, CancellationToken ct)
    {
        var slot = await _db.MealSlots
            .Include(s => s.Attendees)
            .FirstOrDefaultAsync(s => s.Id == slotId && s.WeekPlanId == id, ct);
        if (slot is null) return NotFound();

        slot.RecipeId = dto.RecipeId;
        slot.VagueIntent = dto.VagueIntent;
        slot.IntentTag = dto.IntentTag;
        slot.FrozenItemRef = dto.FrozenItemRef;
        slot.Notes = dto.Notes;

        await _db.SaveChangesAsync(ct);

        var titles = await LoadRecipeTitlesAsync(new[] { slot }, ct);
        return Ok(ToDto(slot, titles));
    }

    [HttpPut("{id:guid}/slots/{slotId:guid}/attendees")]
    [ProducesResponseType(typeof(IReadOnlyList<MealSlotAttendeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MealSlotAttendeeDto>>> UpdateAttendees(
        Guid id, Guid slotId, UpdateAttendeesDto dto, CancellationToken ct)
    {
        var slot = await _db.MealSlots
            .Include(s => s.Attendees)
            .FirstOrDefaultAsync(s => s.Id == slotId && s.WeekPlanId == id, ct);
        if (slot is null) return NotFound();

        _db.MealSlotAttendees.RemoveRange(slot.Attendees);
        slot.Attendees.Clear();

        foreach (var memberId in dto.FamilyMemberIds.Distinct())
            slot.Attendees.Add(new MealSlotAttendee { FamilyMemberId = memberId });
        foreach (var guestId in dto.GuestIds.Distinct())
            slot.Attendees.Add(new MealSlotAttendee { GuestId = guestId });

        await _db.SaveChangesAsync(ct);

        return Ok(slot.Attendees.Select(a => new MealSlotAttendeeDto(a.Id, a.FamilyMemberId, a.GuestId)).ToList());
    }

    // ------------------------------------------------------------------------

    private static DateOnly NormalizeToMonday(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday-based
        return date.AddDays(-diff);
    }

    private async Task<WeekPlan?> LoadPlanAsync(DateOnly monday, CancellationToken ct) =>
        await _db.WeekPlans
            .Include(p => p.Slots).ThenInclude(s => s.Attendees)
            .FirstOrDefaultAsync(p => p.WeekStart == monday, ct);

    private async Task<WeekPlan> CreateEmptyPlanAsync(DateOnly monday, CancellationToken ct)
    {
        var plan = new WeekPlan { WeekStart = monday };
        var days = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var meals = new[] { MealType.Breakfast, MealType.Lunch, MealType.Dinner };
        foreach (var d in days)
            foreach (var m in meals)
                plan.Slots.Add(new MealSlot { DayOfWeek = d, MealType = m });

        _db.WeekPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return plan;
    }

    private async Task<Dictionary<Guid, string>> LoadRecipeTitlesAsync(IEnumerable<MealSlot> slots, CancellationToken ct)
    {
        var ids = slots.Where(s => s.RecipeId.HasValue).Select(s => s.RecipeId!.Value).Distinct().ToList();
        if (ids.Count == 0) return new();
        return await _db.Recipes
            .Where(r => ids.Contains(r.Id))
            .Select(r => new { r.Id, r.Title })
            .ToDictionaryAsync(x => x.Id, x => x.Title, ct);
    }

    private async Task<WeekPlanDto> ToDtoAsync(WeekPlan plan, CancellationToken ct)
    {
        var titles = await LoadRecipeTitlesAsync(plan.Slots, ct);
        return ToDto(plan, titles);
    }

    private static WeekPlanDto ToDto(WeekPlan plan, IReadOnlyDictionary<Guid, string> titles) => new(
        plan.Id, plan.WeekStart, plan.Notes, plan.CreatedAt, plan.UpdatedAt,
        plan.Slots
            .OrderBy(s => s.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)s.DayOfWeek)
            .ThenBy(s => (int)s.MealType)
            .Select(s => ToDto(s, titles))
            .ToList());

    private static MealSlotDto ToDto(MealSlot s, IReadOnlyDictionary<Guid, string> titles) => new(
        s.Id, s.WeekPlanId, s.DayOfWeek, s.MealType, s.RecipeId,
        s.RecipeId.HasValue && titles.TryGetValue(s.RecipeId.Value, out var t) ? t : null,
        s.VagueIntent, s.IntentTag, s.FrozenItemRef, s.Notes,
        s.Attendees.Select(a => new MealSlotAttendeeDto(a.Id, a.FamilyMemberId, a.GuestId)).ToList());
}
