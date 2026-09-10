using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Equipment &amp; Materials. The list endpoint is shared by Formulas, Process Steps, Devices and Work Instructions.</summary>
[ApiController]
[Authorize]
[Route("api/materials")]
public class MaterialsController(AppDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>GET /api/materials?groupId=1&amp;type=1&amp;categoryId=5 — non-deleted materials with on-hand inventory.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] MaterialType? type, [FromQuery] int? categoryId)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.Materials.AsNoTracking().Where(m => m.GroupId == groupId && !m.IsDeleted);
        if (type != null) q = q.Where(m => m.MaterialType == type);
        if (categoryId != null) q = q.Where(m => m.CategoryId == categoryId);
        var rows = await q.Select(m => new
        {
            m.Id, m.GroupId, GroupName = m.Group!.Name, MaterialType = (int)m.MaterialType, m.CategoryId,
            CategoryName = m.Category != null ? m.Category.Name : null, m.ProductCode, m.ProductName, m.Density, m.Price,
            m.Voc, m.Hap, m.Tap, m.MinQuantity, m.VendorId, m.Notes, m.CreatedAt, m.UpdatedAt,
            OnHand = db.InventoryTransactions.Where(t => t.MaterialId == m.Id).Sum(t => (decimal?)t.Quantity) ?? 0,
        }).OrderByDescending(m => m.Id).ToListAsync();
        return Ok(rows.Select(m => new
        {
            m.Id, m.GroupId, m.GroupName, m.MaterialType, MaterialTypeLabel = MaterialTypes.Label((MaterialType)m.MaterialType),
            m.CategoryId, m.CategoryName, m.ProductCode, m.ProductName, m.Density, m.Price, m.Voc, m.Hap, m.Tap,
            m.MinQuantity, m.OnHand, m.VendorId, m.Notes, m.CreatedAt, m.UpdatedAt,
        }));
    }
}
