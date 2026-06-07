using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Family;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/family-members")]
[Produces("application/json")]
public class FamilyMembersController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public FamilyMembersController(DishhiveDbContext db) => _db = db;

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FamilyMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<FamilyMemberDto>>> GetAll()
    {
        var members = await _db.FamilyMembers
            .Include(m => m.Preferences)
            .OrderBy(m => m.DisplayName)
            .ToListAsync();
        return Ok(members.Select(m => m.ToDto()));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberDto>> Get(Guid id)
    {
        var member = await _db.FamilyMembers
            .Include(m => m.Preferences)
            .FirstOrDefaultAsync(m => m.Id == id);
        return member is null ? NotFound() : Ok(member.ToDto());
    }

    [HttpPost]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FamilyMemberDto>> Create(CreateFamilyMemberDto dto)
    {
        var member = new FamilyMember { DisplayName = dto.DisplayName, Notes = dto.Notes };
        _db.FamilyMembers.Add(member);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = member.Id }, member.ToDto());
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberDto>> Update(Guid id, UpdateFamilyMemberDto dto)
    {
        var member = await _db.FamilyMembers
            .Include(m => m.Preferences)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (member is null) return NotFound();

        member.DisplayName = dto.DisplayName;
        member.Notes = dto.Notes;
        await _db.SaveChangesAsync();
        return Ok(member.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var member = await _db.FamilyMembers.FindAsync(id);
        if (member is null) return NotFound();
        _db.FamilyMembers.Remove(member);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ---- Preferences sub-resource ------------------------------------------

    [HttpGet("{id:guid}/preferences")]
    [ProducesResponseType(typeof(IEnumerable<FamilyMemberPreferenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<FamilyMemberPreferenceDto>>> GetPreferences(Guid id)
    {
        var exists = await _db.FamilyMembers.AnyAsync(m => m.Id == id);
        if (!exists) return NotFound();

        var prefs = await _db.FamilyMemberPreferences
            .Where(p => p.FamilyMemberId == id)
            .ToListAsync();
        return Ok(prefs.Select(p => p.ToDto()));
    }

    [HttpPost("{id:guid}/preferences")]
    [ProducesResponseType(typeof(FamilyMemberPreferenceDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberPreferenceDto>> AddPreference(Guid id, CreatePreferenceDto dto)
    {
        var exists = await _db.FamilyMembers.AnyAsync(m => m.Id == id);
        if (!exists) return NotFound();

        var pref = new FamilyMemberPreference
        {
            FamilyMemberId = id,
            Kind = dto.Kind,
            Value = dto.Value,
            RecipeId = dto.RecipeId
        };
        _db.FamilyMemberPreferences.Add(pref);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetPreferences), new { id }, pref.ToDto());
    }

    [HttpPut("{id:guid}/preferences/{prefId:guid}")]
    [ProducesResponseType(typeof(FamilyMemberPreferenceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberPreferenceDto>> UpdatePreference(Guid id, Guid prefId, UpdatePreferenceDto dto)
    {
        var pref = await _db.FamilyMemberPreferences
            .FirstOrDefaultAsync(p => p.Id == prefId && p.FamilyMemberId == id);
        if (pref is null) return NotFound();

        pref.Kind = dto.Kind;
        pref.Value = dto.Value;
        pref.RecipeId = dto.RecipeId;
        await _db.SaveChangesAsync();
        return Ok(pref.ToDto());
    }

    [HttpDelete("{id:guid}/preferences/{prefId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePreference(Guid id, Guid prefId)
    {
        var pref = await _db.FamilyMemberPreferences
            .FirstOrDefaultAsync(p => p.Id == prefId && p.FamilyMemberId == id);
        if (pref is null) return NotFound();

        _db.FamilyMemberPreferences.Remove(pref);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
