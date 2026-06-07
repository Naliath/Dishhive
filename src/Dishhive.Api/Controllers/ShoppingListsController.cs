using System.Text;
using Dishhive.Api.Data;
using Dishhive.Api.Models.DTOs;
using Dishhive.Api.Models.Shopping;
using Dishhive.Api.Services.Shopping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Dishhive.Api.Controllers;

[ApiController]
[Route("api/shopping-lists")]
[Produces("application/json")]
public class ShoppingListsController : ControllerBase
{
    private readonly DishhiveDbContext _db;
    private readonly IShoppingListGenerationService _generator;

    public ShoppingListsController(DishhiveDbContext db, IShoppingListGenerationService generator)
    {
        _db = db;
        _generator = generator;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ShoppingListDto>>> GetAll(CancellationToken ct)
    {
        var lists = await _db.ShoppingLists
            .Include(l => l.Items)
            .OrderByDescending(l => l.UpdatedAt)
            .ToListAsync(ct);
        return Ok(lists.Select(l => l.ToDto()));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ShoppingListDto>> Get(Guid id, CancellationToken ct)
    {
        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        return list is null ? NotFound() : Ok(list.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<ShoppingListDto>> Create(CreateShoppingListDto dto, CancellationToken ct)
    {
        var list = new ShoppingList { Title = dto.Title };
        _db.ShoppingLists.Add(list);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = list.Id }, list.ToDto());
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var list = await _db.ShoppingLists.FindAsync(new object?[] { id }, ct);
        if (list is null) return NotFound();
        _db.ShoppingLists.Remove(list);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("from-week-plan/{weekPlanId:guid}")]
    [ProducesResponseType(typeof(ShoppingListDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ShoppingListDto>> GenerateFromWeekPlan(Guid weekPlanId, CancellationToken ct)
    {
        try
        {
            var list = await _generator.GenerateFromWeekPlanAsync(weekPlanId, ct);
            // Reload with Items tracked from the same context.
            var reloaded = await _db.ShoppingLists.Include(l => l.Items).FirstAsync(l => l.Id == list.Id, ct);
            return CreatedAtAction(nameof(Get), new { id = reloaded.Id }, reloaded.ToDto());
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<ShoppingListItemDto>> AddItem(Guid id, CreateShoppingListItemDto dto, CancellationToken ct)
    {
        var list = await _db.ShoppingLists.Include(l => l.Items).FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return NotFound();

        var item = new ShoppingListItem
        {
            ShoppingListId = id,
            Order = dto.Order ?? (list.Items.Count == 0 ? 0 : list.Items.Max(i => i.Order) + 1),
            Name = dto.Name,
            Quantity = dto.Quantity,
            Unit = dto.Unit,
            Section = dto.Section,
            Checked = dto.Checked,
            Note = dto.Note,
        };
        _db.ShoppingListItems.Add(item);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id }, item.ToDto());
    }

    [HttpPut("{id:guid}/items/{itemId:guid}")]
    public async Task<ActionResult<ShoppingListItemDto>> UpdateItem(Guid id, Guid itemId, UpdateShoppingListItemDto dto, CancellationToken ct)
    {
        var item = await _db.ShoppingListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == id, ct);
        if (item is null) return NotFound();

        item.Name = dto.Name;
        item.Quantity = dto.Quantity;
        item.Unit = dto.Unit;
        item.Section = dto.Section;
        item.Checked = dto.Checked;
        item.Note = dto.Note;
        await _db.SaveChangesAsync(ct);
        return Ok(item.ToDto());
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid id, Guid itemId, CancellationToken ct)
    {
        var item = await _db.ShoppingListItems.FirstOrDefaultAsync(i => i.Id == itemId && i.ShoppingListId == id, ct);
        if (item is null) return NotFound();
        _db.ShoppingListItems.Remove(item);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/export")]
    [Produces("text/markdown", "application/json")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] string format = "markdown", CancellationToken ct = default)
    {
        var list = await _db.ShoppingLists
            .Include(l => l.Items)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
        if (list is null) return NotFound();

        if (!string.Equals(format, "markdown", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"Unsupported export format '{format}'." });

        var sb = new StringBuilder();
        sb.AppendLine($"# {list.Title}");
        sb.AppendLine();

        foreach (var section in list.Items.GroupBy(i => i.Section ?? "Other").OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine($"## {section.Key}");
            foreach (var item in section.OrderBy(i => i.Order))
            {
                var qty = item.Quantity.HasValue ? $"{item.Quantity:0.##} " : "";
                var unit = string.IsNullOrEmpty(item.Unit) ? "" : $"{item.Unit} ";
                var box = item.Checked ? "[x]" : "[ ]";
                sb.AppendLine($"- {box} {qty}{unit}{item.Name}");
            }
            sb.AppendLine();
        }

        return Content(sb.ToString(), "text/markdown");
    }
}
