using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public FamilyController(DishhiveDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<FamilyDtos.FamilyMemberSummaryDto>>> GetAll(
        [FromQuery] bool includeGuests = true)
    {
        var query = _db.FamilyMembers.AsQueryable();
        if (!includeGuests)
            query = query.Where(m => !m.IsGuest);

        var members = await query
            .OrderBy(m => m.Name)
            .Select(m => new FamilyDtos.FamilyMemberSummaryDto(m.Id, m.Name, m.IsGuest, m.GuestUntil))
            .ToListAsync();

        return Ok(members);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<FamilyDtos.FamilyMemberDto>> GetById(Guid id)
    {
        var member = await _db.FamilyMembers
            .Include(m => m.Preferences)
            .Include(m => m.FavoriteDishes)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (member == null)
            return NotFound();

        return Ok(MapToDto(member));
    }

    [HttpPost]
    public async Task<ActionResult<FamilyDtos.FamilyMemberDto>> Create([FromBody] FamilyDtos.CreateFamilyMemberDto dto)
    {
        var member = new FamilyMember
        {
            Name = dto.Name,
            IsGuest = dto.IsGuest,
            GuestFrom = dto.GuestFrom,
            GuestUntil = dto.GuestUntil
        };

        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = member.Id }, MapToDto(member));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] FamilyDtos.UpdateFamilyMemberDto dto)
    {
        var member = await _db.FamilyMembers.FindAsync(id);
        if (member == null)
            return NotFound();

        member.Name = dto.Name;
        member.IsGuest = dto.IsGuest;
        member.GuestFrom = dto.GuestFrom;
        member.GuestUntil = dto.GuestUntil;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await _db.FamilyMembers.FindAsync(id);
        if (member == null)
            return NotFound();

        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Preferences ---

    [HttpGet("{id:guid}/preferences")]
    public async Task<ActionResult<IEnumerable<FamilyDtos.MemberPreferenceDto>>> GetPreferences(Guid id)
    {
        if (!await _db.FamilyMembers.AnyAsync(m => m.Id == id))
            return NotFound();

        var prefs = await _db.MemberPreferences
            .Where(p => p.FamilyMemberId == id)
            .OrderBy(p => p.PreferenceType)
            .Select(p => new FamilyDtos.MemberPreferenceDto(
                p.Id, p.PreferenceType.ToString(), p.Value, p.Notes, p.CreatedAt))
            .ToListAsync();

        return Ok(prefs);
    }

    [HttpPost("{id:guid}/preferences")]
    public async Task<IActionResult> AddPreference(Guid id, [FromBody] FamilyDtos.AddPreferenceDto dto)
    {
        if (!await _db.FamilyMembers.AnyAsync(m => m.Id == id))
            return NotFound();

        if (!Enum.TryParse<PreferenceType>(dto.PreferenceType, ignoreCase: true, out var prefType))
            return BadRequest(new { message = $"Unknown preference type: {dto.PreferenceType}" });

        var pref = new MemberPreference
        {
            FamilyMemberId = id,
            PreferenceType = prefType,
            Value = dto.Value,
            Notes = dto.Notes
        };

        _db.MemberPreferences.Add(pref);
        await _db.SaveChangesAsync();
        return Created(string.Empty, new FamilyDtos.MemberPreferenceDto(
            pref.Id, pref.PreferenceType.ToString(), pref.Value, pref.Notes, pref.CreatedAt));
    }

    [HttpDelete("{id:guid}/preferences/{prefId:guid}")]
    public async Task<IActionResult> DeletePreference(Guid id, Guid prefId)
    {
        var pref = await _db.MemberPreferences.FirstOrDefaultAsync(p => p.Id == prefId && p.FamilyMemberId == id);
        if (pref == null)
            return NotFound();

        _db.MemberPreferences.Remove(pref);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Favorites ---

    [HttpGet("{id:guid}/favorites")]
    public async Task<ActionResult<IEnumerable<FamilyDtos.FavoriteDishDto>>> GetFavorites(Guid id)
    {
        if (!await _db.FamilyMembers.AnyAsync(m => m.Id == id))
            return NotFound();

        var favorites = await _db.FavoriteDishes
            .Where(f => f.FamilyMemberId == id)
            .OrderBy(f => f.DishName)
            .Select(f => new FamilyDtos.FavoriteDishDto(f.Id, f.RecipeId, f.DishName, f.CreatedAt))
            .ToListAsync();

        return Ok(favorites);
    }

    [HttpPost("{id:guid}/favorites")]
    public async Task<IActionResult> AddFavorite(Guid id, [FromBody] FamilyDtos.AddFavoriteDto dto)
    {
        if (!await _db.FamilyMembers.AnyAsync(m => m.Id == id))
            return NotFound();

        var favorite = new FavoriteDish
        {
            FamilyMemberId = id,
            RecipeId = dto.RecipeId,
            DishName = dto.DishName
        };

        _db.FavoriteDishes.Add(favorite);
        await _db.SaveChangesAsync();
        return Created(string.Empty, new FamilyDtos.FavoriteDishDto(
            favorite.Id, favorite.RecipeId, favorite.DishName, favorite.CreatedAt));
    }

    [HttpDelete("{id:guid}/favorites/{favId:guid}")]
    public async Task<IActionResult> DeleteFavorite(Guid id, Guid favId)
    {
        var fav = await _db.FavoriteDishes.FirstOrDefaultAsync(f => f.Id == favId && f.FamilyMemberId == id);
        if (fav == null)
            return NotFound();

        _db.FavoriteDishes.Remove(fav);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private static FamilyDtos.FamilyMemberDto MapToDto(FamilyMember m) => new(
        m.Id, m.Name, m.IsGuest, m.GuestFrom, m.GuestUntil,
        m.Preferences.Select(p => new FamilyDtos.MemberPreferenceDto(
            p.Id, p.PreferenceType.ToString(), p.Value, p.Notes, p.CreatedAt)).ToList(),
        m.FavoriteDishes.Select(f => new FamilyDtos.FavoriteDishDto(
            f.Id, f.RecipeId, f.DishName, f.CreatedAt)).ToList(),
        m.CreatedAt, m.UpdatedAt
    );
}
