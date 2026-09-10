using System.Globalization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Inventory movements ("+/- Adjust" on the materials grid). On-hand = SUM(quantity); negative quantities are
/// consumption and feed the environmental report.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Materials)]
[Route("api/inventory")]
public class InventoryController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    public record AdjustInput(int MaterialId, decimal Quantity, int? LocationId, string? BatchNumber, string? Reason, string? CustomerName);

    /// <summary>GET /api/inventory?materialId=5 — movement history, newest first.</summary>
    [HttpGet]
    public async Task<IActionResult> History([FromQuery] int materialId, [FromQuery] int take = 200)
    {
        var m = await db.Materials.AsNoTracking().FirstOrDefaultAsync(x => x.Id == materialId) ?? throw ApiException.NotFound("Material");
        await me.EnsureGroupAsync(m.GroupId);
        var rows = await db.InventoryTransactions.AsNoTracking().Where(t => t.MaterialId == materialId)
            .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(Math.Clamp(take, 1, 1000))
            .Select(t => new
            {
                t.Id, t.CreatedAt, t.Quantity, t.LocationId, LocationName = t.Location != null ? t.Location.Name : null,
                t.BatchNumber, t.Reason, t.CustomerName, t.FormulaId,
                UserName = db.Users.Where(u => u.Id == t.CreatedBy).Select(u => u.Username).FirstOrDefault(),
            }).ToListAsync();
        var onHand = await db.InventoryTransactions.Where(t => t.MaterialId == materialId).SumAsync(t => (decimal?)t.Quantity) ?? 0;
        return Ok(new { onHand, items = rows });
    }

    /// <summary>POST /api/inventory/adjust — add (+) or consume (−) stock.</summary>
    [HttpPost("adjust")]
    public async Task<IActionResult> Adjust(AdjustInput input)
    {
        var m = await db.Materials.FirstOrDefaultAsync(x => x.Id == input.MaterialId && !x.IsDeleted) ?? throw ApiException.NotFound("Material");
        await me.EnsureGroupAsync(m.GroupId);
        if (input.Quantity == 0) throw ApiException.Bad("Quantity must not be 0. Use a positive number to add stock and a negative number to consume it.");
        if (Math.Abs(input.Quantity) > 100_000_000) throw ApiException.Bad("Quantity is too large.");

        string? locationName = null;
        if (input.LocationId != null)
        {
            var loc = await db.MaterialLocations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == input.LocationId && !l.IsDeleted)
                      ?? throw ApiException.Bad("The selected location no longer exists.");
            if (loc.GroupId != m.GroupId) throw ApiException.Bad("The selected location belongs to another group.");
            locationName = loc.Name;
        }

        var onHand = await db.InventoryTransactions.Where(t => t.MaterialId == m.Id).SumAsync(t => (decimal?)t.Quantity) ?? 0;
        if (onHand + input.Quantity < 0)
            throw ApiException.Bad($"Cannot consume {Fmt(-input.Quantity)}: only {Fmt(onHand)} on hand.");

        var t = new InventoryTransaction
        {
            GroupId = m.GroupId, MaterialId = m.Id, Quantity = input.Quantity, LocationId = input.LocationId,
            BatchNumber = Text.Clean(input.BatchNumber), Reason = Text.Clean(input.Reason), CustomerName = Text.Clean(input.CustomerName),
            CreatedBy = me.Id,
        };
        db.InventoryTransactions.Add(t);
        var parts = new List<string> { $"{(input.Quantity > 0 ? "+" : "")}{Fmt(input.Quantity)} (on hand {Fmt(onHand)} → {Fmt(onHand + input.Quantity)})" };
        if (locationName != null) parts.Add($"Location: {locationName}");
        if (t.BatchNumber != null) parts.Add($"Batch: {t.BatchNumber}");
        if (t.CustomerName != null) parts.Add($"Customer: {t.CustomerName}");
        if (t.Reason != null) parts.Add($"Reason: {t.Reason}");
        audit.Log(LinkEntityTypes.Material, m.Id, "Inventory adjusted", string.Join("\n", parts), m.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Inventory updated.", id = t.Id, onHand = onHand + input.Quantity });
    }

    private static string Fmt(decimal d) => d.ToString("#,0.##", CultureInfo.InvariantCulture);
}
