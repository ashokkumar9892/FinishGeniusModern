using System.Globalization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Equipment &amp; Materials. The list endpoint is shared by Formulas, Process Steps, Devices and Work Instructions.</summary>
[ApiController]
[Authorize]
[Route("api/materials")]
public class MaterialsController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier) : ControllerBase
{
    public record MaterialInput(
        int GroupId, MaterialType MaterialType, int? CategoryId, string? ProductCode, string? ProductName,
        decimal Density, decimal Price, decimal Voc, decimal Hap, decimal Tap, decimal MinQuantity, int? VendorId, string? Notes);

    public record IdsInput(List<int> Ids);

    public record BulkCopyInput(List<int> Ids, int DestinationGroupId);

    /// <summary>Material types that can be created on the Equipment &amp; Materials screen (Formula is formula-only).</summary>
    public static readonly MaterialType[] EditableTypes =
        [MaterialType.Base, MaterialType.Pigment, MaterialType.Dye, MaterialType.Equipment, MaterialType.Sundry, MaterialType.Product];

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
            VendorName = db.Vendors.Where(v => v.Id == m.VendorId).Select(v => v.VendorName).FirstOrDefault(),
            OnHand = db.InventoryTransactions.Where(t => t.MaterialId == m.Id).Sum(t => (decimal?)t.Quantity) ?? 0,
        }).OrderByDescending(m => m.Id).ToListAsync();
        return Ok(rows.Select(m => new
        {
            m.Id, m.GroupId, m.GroupName, m.MaterialType, MaterialTypeLabel = MaterialTypes.Label((MaterialType)m.MaterialType),
            m.CategoryId, m.CategoryName, m.ProductCode, m.ProductName, m.Density, m.Price, m.Voc, m.Hap, m.Tap,
            m.MinQuantity, m.OnHand, m.VendorId, m.VendorName, m.Notes, m.CreatedAt, m.UpdatedAt,
        }));
    }

    /// <summary>GET /api/materials/5</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> Get(int id)
    {
        var m = await db.Materials.AsNoTracking().Include(x => x.Category).Include(x => x.Group)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Material");
        await me.EnsureGroupAsync(m.GroupId);
        var onHand = await db.InventoryTransactions.Where(t => t.MaterialId == id).SumAsync(t => (decimal?)t.Quantity) ?? 0;
        var vendorName = m.VendorId == null ? null : await db.Vendors.Where(v => v.Id == m.VendorId).Select(v => v.VendorName).FirstOrDefaultAsync();
        return Ok(new
        {
            m.Id, m.GroupId, GroupName = m.Group?.Name, MaterialType = (int)m.MaterialType, MaterialTypeLabel = MaterialTypes.Label(m.MaterialType),
            m.CategoryId, CategoryName = m.Category?.Name, m.ProductCode, m.ProductName, m.Density, m.Price, m.Voc, m.Hap, m.Tap,
            m.MinQuantity, OnHand = onHand, m.VendorId, VendorName = vendorName, m.Notes, m.CreatedAt, m.UpdatedAt,
        });
    }

    [HttpPost]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> Create(MaterialInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var m = new Material { GroupId = input.GroupId };
        await ApplyAsync(m, input);
        db.Materials.Add(m);
        await db.SaveChangesAsync();
        audit.Log(LinkEntityTypes.Material, m.Id, "Created",
            $"{MaterialTypes.Label(m.MaterialType)} \"{m.ProductName}\"{(m.ProductCode == null ? "" : $" ({m.ProductCode})")}", m.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Material created.", id = m.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> Update(int id, MaterialInput input)
    {
        var m = await Load(id);
        if (input.GroupId != m.GroupId) await me.EnsureGroupAsync(input.GroupId);
        var before = await SnapshotAsync(m);
        m.GroupId = input.GroupId;
        await ApplyAsync(m, input);
        m.UpdatedAt = DateTime.UtcNow;
        var after = await SnapshotAsync(m);
        var diff = Diff(before, after);
        audit.Log(LinkEntityTypes.Material, m.Id, "Updated", diff.Length == 0 ? "No changes." : diff, m.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Material updated.", id = m.Id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> Delete(int id)
    {
        me.EnsureAdmin();
        var m = await Load(id);
        m.IsDeleted = true;
        m.UpdatedAt = DateTime.UtcNow;
        audit.Log(LinkEntityTypes.Material, m.Id, "Deleted", $"\"{m.ProductName}\"", m.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Material deleted." });
    }

    /// <summary>POST /api/materials/5/copy — duplicates the material in the same group ("{name} - Copy").</summary>
    [HttpPost("{id:int}/copy")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> Copy(int id)
    {
        var src = await Load(id);
        var copy = new Material
        {
            GroupId = src.GroupId, MaterialType = src.MaterialType, CategoryId = src.CategoryId, ProductCode = src.ProductCode,
            ProductName = Truncate(src.ProductName + " - Copy", 400), Density = src.Density, Price = src.Price, Voc = src.Voc, Hap = src.Hap,
            Tap = src.Tap, MinQuantity = src.MinQuantity, VendorId = src.VendorId, Notes = src.Notes,
        };
        db.Materials.Add(copy);
        await db.SaveChangesAsync();
        audit.Log(LinkEntityTypes.Material, copy.Id, "Created", $"Copied from #{src.Id} \"{src.ProductName}\"", copy.GroupId);
        audit.Log(LinkEntityTypes.Material, src.Id, "Copied", $"Copied to #{copy.Id} \"{copy.ProductName}\"", src.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Material copied.", id = copy.Id });
    }

    /// <summary>POST /api/materials/bulk-copy { ids, destinationGroupId } (admins).</summary>
    [HttpPost("bulk-copy")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> BulkCopy(BulkCopyInput input)
    {
        me.EnsureAdmin();
        var ids = (input.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Select at least one material to copy.");
        var dest = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == input.DestinationGroupId && !g.IsDeleted)
                   ?? throw ApiException.Bad("Please select the destination group.");
        await me.EnsureGroupAsync(dest.Id);

        var sources = await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id) && !m.IsDeleted)
            .Select(m => new { m.Id, m.GroupId, m.ProductName }).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.Bad("Some of the selected materials no longer exist. Refresh the list and try again.");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        var copied = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            copied = await copier.CopyMaterialsAsync(ids, dest.Id);
            foreach (var s in sources)
                audit.Log(LinkEntityTypes.Material, s.Id, "Copied", $"Bulk copied to the {dest.Name} group", s.GroupId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
        return Ok(new { message = $"Successfully copied {copied} materials to the {dest.Name} group.", copied });
    }

    /// <summary>POST /api/materials/bulk-delete { ids } (admins).</summary>
    [HttpPost("bulk-delete")]
    [Authorize(Roles = Access.Materials)]
    public async Task<IActionResult> BulkDelete(IdsInput input)
    {
        me.EnsureAdmin();
        var ids = (input.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Select at least one material to delete.");
        var rows = await db.Materials.Where(m => ids.Contains(m.Id) && !m.IsDeleted).ToListAsync();
        foreach (var g in rows.Select(r => r.GroupId).Distinct()) await me.EnsureGroupAsync(g);
        foreach (var m in rows)
        {
            m.IsDeleted = true;
            m.UpdatedAt = DateTime.UtcNow;
            audit.Log(LinkEntityTypes.Material, m.Id, "Deleted", $"\"{m.ProductName}\" (bulk delete)", m.GroupId);
        }
        await db.SaveChangesAsync();
        return Ok(new { message = rows.Count == 1 ? "1 material deleted." : $"{rows.Count} materials deleted.", deleted = rows.Count });
    }

    // ------------------------------------------------------------------

    private async Task<Material> Load(int id)
    {
        var m = await db.Materials.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Material");
        await me.EnsureGroupAsync(m.GroupId);
        return m;
    }

    private async Task ApplyAsync(Material m, MaterialInput input)
    {
        if (input.GroupId <= 0) throw ApiException.Bad("Group is required.");
        var name = Text.Req(input.ProductName, "Product Name");
        if (!Enum.IsDefined(input.MaterialType) || !EditableTypes.Contains(input.MaterialType))
            throw ApiException.Bad("Material Type is required.");
        foreach (var (value, label) in new[]
                 {
                     (input.Density, "Density"), (input.Price, "Price"), (input.Voc, "VOC"), (input.Hap, "HAP"),
                     (input.Tap, "TAP"), (input.MinQuantity, "Min. Quantity"),
                 })
            if (value < 0) throw ApiException.Bad($"{label} cannot be negative.");

        if (input.CategoryId != null)
        {
            var cat = await db.MaterialCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CategoryId)
                      ?? throw ApiException.Bad("The selected Material Category does not exist.");
            if (cat.GroupId != null && cat.GroupId != input.GroupId)
                throw ApiException.Bad("The selected Material Category belongs to another group.");
            if (cat.MaterialType != input.MaterialType)
                throw ApiException.Bad($"The selected Material Category is a {MaterialTypes.Label(cat.MaterialType)} category; choose a {MaterialTypes.Label(input.MaterialType)} category.");
        }
        if (input.VendorId != null && !await db.Vendors.AnyAsync(v => v.Id == input.VendorId && v.GroupId == input.GroupId && !v.IsDeleted))
            throw ApiException.Bad("The selected Vendor does not belong to this group.");

        var isEquipment = input.MaterialType == MaterialType.Equipment;
        m.MaterialType = input.MaterialType;
        m.CategoryId = input.CategoryId;
        m.ProductCode = Text.Clean(input.ProductCode);
        m.ProductName = Truncate(name, 400);
        m.Density = input.Density;
        m.Price = input.Price;
        m.Voc = isEquipment ? 0 : input.Voc;
        m.Hap = isEquipment ? 0 : input.Hap;
        m.Tap = isEquipment ? 0 : input.Tap;
        m.MinQuantity = input.MinQuantity;
        m.VendorId = input.VendorId;
        m.Notes = Text.Clean(input.Notes) is { } n ? Truncate(n, 4000) : null;
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

    /// <summary>Readable field values used to build the audit diff.</summary>
    private async Task<List<(string field, string value)>> SnapshotAsync(Material m)
    {
        string N(decimal d) => d.ToString("0.00##", CultureInfo.InvariantCulture);
        var group = await db.Groups.Where(g => g.Id == m.GroupId).Select(g => g.Name).FirstOrDefaultAsync();
        var category = m.CategoryId == null ? null : await db.MaterialCategories.Where(c => c.Id == m.CategoryId).Select(c => c.Name).FirstOrDefaultAsync();
        var vendor = m.VendorId == null ? null : await db.Vendors.Where(v => v.Id == m.VendorId).Select(v => v.VendorName).FirstOrDefaultAsync();
        return
        [
            ("Group", group ?? ""), ("Material Type", MaterialTypes.Label(m.MaterialType)), ("Material Category", category ?? ""),
            ("Product Code", m.ProductCode ?? ""), ("Product Name", m.ProductName), ("Density", N(m.Density)), ("Price", N(m.Price)),
            ("VOC", N(m.Voc)), ("HAP", N(m.Hap)), ("TAP", N(m.Tap)), ("Min. Quantity", N(m.MinQuantity)), ("Vendor", vendor ?? ""),
            ("Notes", m.Notes ?? ""),
        ];
    }

    private static string Diff(List<(string field, string value)> before, List<(string field, string value)> after)
    {
        static string Show(string v) => v.Length == 0 ? "(blank)" : v.Length > 80 ? v[..80] + "…" : v;
        return string.Join("\n", before.Zip(after)
            .Where(p => p.First.value != p.Second.value)
            .Select(p => $"{p.First.field}: {Show(p.First.value)} → {Show(p.Second.value)}"));
    }
}
