using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Family;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/guests")]
[Produces("application/json")]
public class GuestsController : ControllerBase
{
    private readonly DishhiveDbContext _db;

    public GuestsController(DishhiveDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<GuestDto>>> GetAll()
    {
        var guests = await _db.Guests.OrderBy(g => g.DisplayName).ToListAsync();
        return Ok(guests.Select(g => g.ToDto()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GuestDto>> Get(Guid id)
    {
        var g = await _db.Guests.FindAsync(id);
        return g is null ? NotFound() : Ok(g.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<GuestDto>> Create(CreateGuestDto dto)
    {
        var g = new Guest { DisplayName = dto.DisplayName, Notes = dto.Notes };
        _db.Guests.Add(g);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = g.Id }, g.ToDto());
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GuestDto>> Update(Guid id, UpdateGuestDto dto)
    {
        var g = await _db.Guests.FindAsync(id);
        if (g is null) return NotFound();
        g.DisplayName = dto.DisplayName;
        g.Notes = dto.Notes;
        await _db.SaveChangesAsync();
        return Ok(g.ToDto());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var g = await _db.Guests.FindAsync(id);
        if (g is null) return NotFound();
        _db.Guests.Remove(g);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
