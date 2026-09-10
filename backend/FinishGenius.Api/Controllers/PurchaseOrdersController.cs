using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Reorder Materials → "Place Order", and the Order History tab.</summary>
[ApiController]
[Authorize(Roles = Access.Materials)]
[Route("api/purchase-orders")]
public class PurchaseOrdersController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    public record PurchaseOrderLineInput(int MaterialId, decimal Quantity, string? QuantityType);

    public record PurchaseOrderInput(
        int GroupId, int VendorId, string? PoNumber, DateTime? DeliveryDate,
        string? ShipName, string? ShipAddress1, string? ShipAddress2, string? ShipCity, string? ShipState, string? ShipZip, string? ShipCountry,
        List<PurchaseOrderLineInput>? Lines);

    private const string Entity = "PurchaseOrder";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.PurchaseOrders.AsNoTracking().Where(p => p.GroupId == groupId)
            .OrderByDescending(p => p.Id)
            .Select(p => new
            {
                p.Id, p.GroupId, p.PoNumber, p.VendorId, VendorName = p.Vendor!.VendorName, p.CreatedAt, p.DeliveryDate,
                LineCount = p.Lines.Count,
                Total = p.Lines.Sum(l => (decimal?)(l.Quantity * l.UnitPrice)) ?? 0,
                CreatedByName = db.Users.Where(u => u.Id == p.CreatedBy).Select(u => u.Username).FirstOrDefault(),
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var p = await db.PurchaseOrders.AsNoTracking().Include(x => x.Vendor).Include(x => x.Group)
            .Include(x => x.Lines).ThenInclude(l => l.Material).ThenInclude(m => m!.Category)
            .FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Purchase order");
        await me.EnsureGroupAsync(p.GroupId);
        var createdBy = await db.Users.Where(u => u.Id == p.CreatedBy)
            .Select(u => new { u.Username, u.FirstName, u.LastName, u.Email }).FirstOrDefaultAsync();
        var v = p.Vendor!;
        return Ok(new
        {
            p.Id, p.GroupId, GroupName = p.Group?.Name, p.PoNumber, p.DeliveryDate, p.CreatedAt,
            p.ShipName, p.ShipAddress1, p.ShipAddress2, p.ShipCity, p.ShipState, p.ShipZip, p.ShipCountry,
            CreatedByName = createdBy == null ? null : ($"{createdBy.FirstName} {createdBy.LastName}".Trim() is { Length: > 0 } n ? n : createdBy.Username),
            CreatedByEmail = createdBy?.Email,
            Vendor = new
            {
                v.Id, v.VendorName, v.Address, v.City, v.State, v.Zip, v.Country, v.PaymentTerms, v.AccountNumber,
                v.ContactName, v.OfficePhone, v.MobilePhone, v.VendorEmail, v.RequestorEmail,
            },
            Lines = p.Lines.OrderBy(l => l.Id).Select(l => new
            {
                l.Id, l.MaterialId, ProductName = l.Material?.ProductName, ProductCode = l.Material?.ProductCode,
                CategoryName = l.Material?.Category?.Name, MaterialTypeLabel = l.Material == null ? null : MaterialTypes.Label(l.Material.MaterialType),
                l.Quantity, l.QuantityType, l.UnitPrice, LineTotal = l.Quantity * l.UnitPrice,
            }),
            Total = p.Lines.Sum(l => l.Quantity * l.UnitPrice),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseOrderInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        if (input.VendorId <= 0) throw ApiException.Bad("Vendor is required.");
        var vendor = await db.Vendors.AsNoTracking().FirstOrDefaultAsync(v => v.Id == input.VendorId && v.GroupId == input.GroupId && !v.IsDeleted)
                     ?? throw ApiException.Bad("The selected Vendor does not belong to this group.");
        var po = Text.Req(input.PoNumber, "P.O. Number");
        if (await db.PurchaseOrders.AnyAsync(p => p.GroupId == input.GroupId && p.PoNumber == po))
            throw ApiException.Bad($"P.O. Number \"{po}\" already exists.");
        var lines = input.Lines ?? [];
        if (lines.Count == 0) throw ApiException.Bad("Add at least one material to the order.");
        if (lines.Select(l => l.MaterialId).Distinct().Count() != lines.Count) throw ApiException.Bad("A material appears more than once in the order.");

        var ids = lines.Select(l => l.MaterialId).ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id) && m.GroupId == input.GroupId && !m.IsDeleted)
            .ToDictionaryAsync(m => m.Id);
        var order = new PurchaseOrder
        {
            GroupId = input.GroupId, VendorId = vendor.Id, PoNumber = po, DeliveryDate = input.DeliveryDate?.Date,
            ShipName = Text.Clean(input.ShipName), ShipAddress1 = Text.Clean(input.ShipAddress1), ShipAddress2 = Text.Clean(input.ShipAddress2),
            ShipCity = Text.Clean(input.ShipCity), ShipState = Text.Clean(input.ShipState), ShipZip = Text.Clean(input.ShipZip),
            ShipCountry = Text.Clean(input.ShipCountry), CreatedBy = me.Id,
        };
        foreach (var l in lines)
        {
            if (!materials.TryGetValue(l.MaterialId, out var m)) throw ApiException.Bad("One of the ordered materials no longer exists in this group.");
            if (l.Quantity <= 0) throw ApiException.Bad($"PO Qty for \"{m.ProductName}\" must be greater than 0.");
            order.Lines.Add(new PurchaseOrderLine { MaterialId = m.Id, Quantity = l.Quantity, QuantityType = Text.Clean(l.QuantityType), UnitPrice = m.Price });
        }
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync();
        var total = order.Lines.Sum(l => l.Quantity * l.UnitPrice);
        audit.Log(Entity, order.Id, "Created", $"P.O. {po} to {vendor.VendorName}: {order.Lines.Count} line(s), total {total:C2}", order.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Order placed.", id = order.Id });
    }
}
