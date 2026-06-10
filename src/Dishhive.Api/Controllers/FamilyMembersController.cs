using Dishhive.Api.Data;
using Dishhive.Api.Models;
using Dishhive.Api.Models.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

/// <summary>
/// Controller for managing household members and guests
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FamilyMembersController : ControllerBase
{
    private readonly DishhiveDbContext _context;
    private readonly ILogger<FamilyMembersController> _logger;

    public FamilyMembersController(DishhiveDbContext context, ILogger<FamilyMembersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get all family members. Inactive members are excluded unless includeInactive is true.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FamilyMemberDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<FamilyMemberDto>>> GetMembers([FromQuery] bool includeInactive = false)
    {
        var query = _context.FamilyMembers.AsNoTracking();

        if (!includeInactive)
        {
            query = query.Where(m => m.IsActive);
        }

        var members = await query
            .OrderBy(m => m.IsGuest)
            .ThenBy(m => m.Name)
            .Select(m => ToDto(m))
            .ToListAsync();

        return Ok(members);
    }

    /// <summary>
    /// Get a single family member by id
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberDto>> GetMember(Guid id)
    {
        var member = await _context.FamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);

        if (member == null)
        {
            return NotFound();
        }

        return Ok(ToDto(member));
    }

    /// <summary>
    /// Create a family member or guest
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FamilyMemberDto>> CreateMember(CreateFamilyMemberDto dto)
    {
        var member = new FamilyMember
        {
            Name = dto.Name,
            IsGuest = dto.IsGuest,
            Allergies = dto.Allergies,
            DietaryConstraints = dto.DietaryConstraints,
            PreferenceNotes = dto.PreferenceNotes
        };

        _context.FamilyMembers.Add(member);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created family member {Name} ({Id})", member.Name, member.Id);
        return CreatedAtAction(nameof(GetMember), new { id = member.Id }, ToDto(member));
    }

    /// <summary>
    /// Update a family member
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FamilyMemberDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberDto>> UpdateMember(Guid id, UpdateFamilyMemberDto dto)
    {
        var member = await _context.FamilyMembers.FindAsync(id);
        if (member == null)
        {
            return NotFound();
        }

        member.Name = dto.Name;
        member.IsGuest = dto.IsGuest;
        member.Allergies = dto.Allergies;
        member.DietaryConstraints = dto.DietaryConstraints;
        member.PreferenceNotes = dto.PreferenceNotes;
        member.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();

        return Ok(ToDto(member));
    }

    /// <summary>
    /// Delete a family member. Members referenced by meal history are deactivated
    /// (soft delete) instead, so attendance history is preserved.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMember(Guid id)
    {
        var member = await _context.FamilyMembers.FindAsync(id);
        if (member == null)
        {
            return NotFound();
        }

        var hasHistory = await _context.PlannedMealAttendees.AnyAsync(a => a.FamilyMemberId == id);
        if (hasHistory)
        {
            member.IsActive = false;
            _logger.LogInformation("Deactivated family member {Id} (has meal history)", id);
        }
        else
        {
            // Remove favorites explicitly so behavior is identical across EF providers
            var favorites = await _context.FamilyMemberFavorites
                .Where(f => f.FamilyMemberId == id)
                .ToListAsync();
            _context.FamilyMemberFavorites.RemoveRange(favorites);
            _context.FamilyMembers.Remove(member);
            _logger.LogInformation("Deleted family member {Id}", id);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Get the favorite dishes of a family member
    /// </summary>
    [HttpGet("{id:guid}/favorites")]
    [ProducesResponseType(typeof(IEnumerable<FamilyMemberFavoriteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<FamilyMemberFavoriteDto>>> GetFavorites(Guid id)
    {
        if (!await _context.FamilyMembers.AnyAsync(m => m.Id == id))
        {
            return NotFound();
        }

        var favorites = await _context.FamilyMemberFavorites
            .AsNoTracking()
            .Where(f => f.FamilyMemberId == id)
            .OrderBy(f => f.DishName)
            .Select(f => new FamilyMemberFavoriteDto
            {
                Id = f.Id,
                FamilyMemberId = f.FamilyMemberId,
                RecipeId = f.RecipeId,
                DishName = f.DishName
            })
            .ToListAsync();

        return Ok(favorites);
    }

    /// <summary>
    /// Add a favorite dish (recipe link, free-text dish name, or both)
    /// </summary>
    [HttpPost("{id:guid}/favorites")]
    [ProducesResponseType(typeof(FamilyMemberFavoriteDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FamilyMemberFavoriteDto>> AddFavorite(Guid id, CreateFamilyMemberFavoriteDto dto)
    {
        if (!await _context.FamilyMembers.AnyAsync(m => m.Id == id))
        {
            return NotFound();
        }

        var dishName = string.IsNullOrWhiteSpace(dto.DishName) ? null : dto.DishName.Trim();
        if (!dto.RecipeId.HasValue && dishName == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Empty favorite",
                Detail = "Set a recipe or a dish name."
            });
        }

        if (dto.RecipeId.HasValue)
        {
            var recipeTitle = await _context.Recipes
                .Where(r => r.Id == dto.RecipeId.Value)
                .Select(r => r.Title)
                .FirstOrDefaultAsync();

            if (recipeTitle == null)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Unknown recipe",
                    Detail = $"Recipe '{dto.RecipeId}' does not exist."
                });
            }

            // Denormalize so the favorite survives recipe deletion
            dishName ??= recipeTitle;
        }

        var favorite = new FamilyMemberFavorite
        {
            FamilyMemberId = id,
            RecipeId = dto.RecipeId,
            DishName = dishName
        };

        _context.FamilyMemberFavorites.Add(favorite);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Added favorite {DishName} for member {MemberId}", dishName, id);

        return CreatedAtAction(nameof(GetFavorites), new { id }, new FamilyMemberFavoriteDto
        {
            Id = favorite.Id,
            FamilyMemberId = favorite.FamilyMemberId,
            RecipeId = favorite.RecipeId,
            DishName = favorite.DishName
        });
    }

    /// <summary>
    /// Remove a favorite
    /// </summary>
    [HttpDelete("{id:guid}/favorites/{favoriteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFavorite(Guid id, Guid favoriteId)
    {
        var favorite = await _context.FamilyMemberFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.FamilyMemberId == id);

        if (favorite == null)
        {
            return NotFound();
        }

        _context.FamilyMemberFavorites.Remove(favorite);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private static FamilyMemberDto ToDto(FamilyMember member) => new()
    {
        Id = member.Id,
        Name = member.Name,
        IsGuest = member.IsGuest,
        Allergies = member.Allergies,
        DietaryConstraints = member.DietaryConstraints,
        PreferenceNotes = member.PreferenceNotes,
        IsActive = member.IsActive,
        CreatedAt = member.CreatedAt,
        UpdatedAt = member.UpdatedAt
    };
}
