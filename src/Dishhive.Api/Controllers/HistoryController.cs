using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.History;
using Dishhive.Api.Services.History;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/history")]
[Produces("application/json")]
public class HistoryController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IHistoryMaterializationService _materializer;

    public HistoryController(DishhiveDbContext db, IHistoryMaterializationService materializer)
    {
        _db = db;
        _materializer = materializer;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DishHistoryEntryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DishHistoryEntryDto>>> Get([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        await _materializer.MaterializeAsync(ct);

        IQueryable<DishHistoryEntry> q = _db.DishHistory;
        if (from is { } f) q = q.Where(h => h.Date >= f);
        if (to is { } t) q = q.Where(h => h.Date <= t);

        var entries = await q.OrderByDescending(h => h.Date).ThenBy(h => h.MealType).ToListAsync(ct);
        return Ok(entries.Select(e => e.ToDto()));
    }
}

[ApiController]
[Route("api/family-members/{memberId:guid}/favorites")]
[Produces("application/json")]
public class FavoritesController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public FavoritesController(DishhiveDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DishFavoriteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<DishFavoriteDto>>> Get(Guid memberId, CancellationToken ct)
    {
        var memberExists = await _db.FamilyMembers.AnyAsync(m => m.Id == memberId, ct);
        if (!memberExists) return NotFound();
        var favorites = await _db.DishFavorites.Where(f => f.FamilyMemberId == memberId).ToListAsync(ct);
        return Ok(favorites.Select(f => f.ToDto()));
    }

    [HttpPost]
    [ProducesResponseType(typeof(DishFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DishFavoriteDto>> Add(Guid memberId, CreateFavoriteDto dto, CancellationToken ct)
    {
        var memberExists = await _db.FamilyMembers.AnyAsync(m => m.Id == memberId, ct);
        if (!memberExists) return NotFound();

        var fav = new DishFavorite
        {
            FamilyMemberId = memberId,
            RecipeId = dto.RecipeId,
            DishLabel = dto.DishLabel,
        };
        _db.DishFavorites.Add(fav);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { memberId }, fav.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid memberId, Guid id, CancellationToken ct)
    {
        var fav = await _db.DishFavorites.FirstOrDefaultAsync(f => f.Id == id && f.FamilyMemberId == memberId, ct);
        if (fav is null) return NotFound();
        _db.DishFavorites.Remove(fav);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/statistics")]
[Produces("application/json")]
public class StatisticsController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IHistoryMaterializationService _materializer;

    public StatisticsController(DishhiveDbContext db, IHistoryMaterializationService materializer)
    {
        _db = db;
        _materializer = materializer;
    }

    /// <summary>
    /// Returns how often each dish was planned in the given window. Groups by recipe id when present,
    /// otherwise by case-insensitive dish label.
    /// </summary>
    [HttpGet("dish-frequency")]
    [ProducesResponseType(typeof(IEnumerable<DishFrequencyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DishFrequencyDto>>> DishFrequency(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int top = 50,
        CancellationToken ct = default)
    {
        await _materializer.MaterializeAsync(ct);

        IQueryable<DishHistoryEntry> q = _db.DishHistory;
        if (from is { } f) q = q.Where(h => h.Date >= f);
        if (to is { } t) q = q.Where(h => h.Date <= t);

        var entries = await q.ToListAsync(ct);

        var grouped = entries
            .GroupBy(h => h.RecipeId.HasValue ? h.RecipeId.Value.ToString() : h.DishLabel.ToLowerInvariant())
            .Select(g =>
            {
                var representative = g.OrderByDescending(h => h.Date).First();
                return new DishFrequencyDto(
                    DishLabel: representative.DishLabel,
                    RecipeId: representative.RecipeId,
                    TimesPlanned: g.Count(),
                    LastPlanned: g.Max(h => h.Date));
            })
            .OrderByDescending(x => x.TimesPlanned)
            .ThenByDescending(x => x.LastPlanned)
            .Take(Math.Clamp(top, 1, 1000))
            .ToList();

        return Ok(grouped);
    }
}
