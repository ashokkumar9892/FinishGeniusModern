using System.Text.RegularExpressions;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Vendors ("Vndrs" / Vendors tab on Equipment &amp; Materials).</summary>
[ApiController]
[Authorize(Roles = Access.Materials)]
[Route("api/vendors")]
public partial class VendorsController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier) : ControllerBase
{
    public record VendorInput(
        int GroupId, string? VendorName, string? Address, string? City, string? State, string? Zip, string? Country,
        string? PaymentTerms, string? AccountNumber, string? ContactName, string? OfficePhone, string? MobilePhone,
        string? VendorEmail, string? RequestorEmail);

    public record VendorBulkCopyInput(List<int> Ids, int DestinationGroupId);

    private const string Entity = "Vendor";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.Vendors.AsNoTracking().Where(v => v.GroupId == groupId && !v.IsDeleted)
            .OrderBy(v => v.VendorName)
            .Select(v => new
            {
                v.Id, v.GroupId, GroupName = v.Group!.Name, v.VendorName, v.Address, v.City, v.State, v.Zip, v.Country,
                v.PaymentTerms, v.AccountNumber, v.ContactName, v.OfficePhone, v.MobilePhone, v.VendorEmail, v.RequestorEmail,
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var v = await Load(id);
        var groupName = await db.Groups.Where(g => g.Id == v.GroupId).Select(g => g.Name).FirstOrDefaultAsync();
        return Ok(new
        {
            v.Id, v.GroupId, GroupName = groupName, v.VendorName, v.Address, v.City, v.State, v.Zip, v.Country,
            v.PaymentTerms, v.AccountNumber, v.ContactName, v.OfficePhone, v.MobilePhone, v.VendorEmail, v.RequestorEmail,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(VendorInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var v = new Vendor { GroupId = input.GroupId };
        await ApplyAsync(v, input);
        db.Vendors.Add(v);
        await db.SaveChangesAsync();
        audit.Log(Entity, v.Id, "Created", $"\"{v.VendorName}\"", v.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Vendor Created.", id = v.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, VendorInput input)
    {
        var v = await Load(id);
        if (input.GroupId != v.GroupId) await me.EnsureGroupAsync(input.GroupId);
        var before = Snapshot(v);
        v.GroupId = input.GroupId;
        await ApplyAsync(v, input);
        var after = Snapshot(v);
        var diff = string.Join("\n", before.Zip(after).Where(p => p.First.value != p.Second.value)
            .Select(p => $"{p.First.field}: {Show(p.First.value)} → {Show(p.Second.value)}"));
        audit.Log(Entity, v.Id, "Updated", diff.Length == 0 ? "No changes." : diff, v.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Vendor Updated.", id = v.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        me.EnsureAdmin();
        var v = await Load(id);
        v.IsDeleted = true;
        audit.Log(Entity, v.Id, "Deleted", $"\"{v.VendorName}\"", v.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Vendor deleted." });
    }

    /// <summary>POST /api/vendors/bulk-copy { ids, destinationGroupId } (admins).</summary>
    [HttpPost("bulk-copy")]
    public async Task<IActionResult> BulkCopy(VendorBulkCopyInput input)
    {
        me.EnsureAdmin();
        var ids = (input.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Select at least one vendor to copy.");
        var dest = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == input.DestinationGroupId && !g.IsDeleted)
                   ?? throw ApiException.Bad("Please select the destination group.");
        await me.EnsureGroupAsync(dest.Id);
        var sources = await db.Vendors.AsNoTracking().Where(v => ids.Contains(v.Id) && !v.IsDeleted)
            .Select(v => new { v.Id, v.GroupId }).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.Bad("Some of the selected vendors no longer exist. Refresh the list and try again.");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        var copied = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            copied = await copier.CopyVendorsAsync(ids, dest.Id);
            foreach (var s in sources) audit.Log(Entity, s.Id, "Copied", $"Bulk copied to the {dest.Name} group", s.GroupId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
        return Ok(new { message = $"Successfully copied {copied} Vendors to the {dest.Name} group.", copied });
    }

    // ------------------------------------------------------------------

    private async Task<Vendor> Load(int id)
    {
        var v = await db.Vendors.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Vendor");
        await me.EnsureGroupAsync(v.GroupId);
        return v;
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    private static string? Email(string? value, string label)
    {
        var s = Text.Clean(value);
        if (s != null && !EmailRegex().IsMatch(s)) throw ApiException.Bad($"{label} is not a valid email address.");
        return s;
    }

    private static string? Phone(string? value, string label)
    {
        var s = Text.Clean(value);
        if (s == null) return null;
        var digits = s.Count(char.IsDigit);
        if (digits < 7 || digits > 15) throw ApiException.Bad($"{label}: Not a valid phone number");
        return s;
    }

    private async Task ApplyAsync(Vendor v, VendorInput input)
    {
        if (input.GroupId <= 0) throw ApiException.Bad("Group is required.");
        var name = Text.Req(input.VendorName, "Vendor Name");
        if (await db.Vendors.AnyAsync(x => x.GroupId == input.GroupId && !x.IsDeleted && x.VendorName == name && x.Id != v.Id))
            throw ApiException.Bad($"A vendor named \"{name}\" already exists in this group.");
        v.VendorName = name;
        v.Address = Text.Clean(input.Address);
        v.City = Text.Clean(input.City);
        v.State = Text.Clean(input.State);
        v.Zip = Text.Clean(input.Zip);
        v.Country = Text.Clean(input.Country);
        v.PaymentTerms = Text.Clean(input.PaymentTerms);
        v.AccountNumber = Text.Clean(input.AccountNumber);
        v.ContactName = Text.Clean(input.ContactName);
        v.OfficePhone = Phone(input.OfficePhone, "Office Phone");
        v.MobilePhone = Phone(input.MobilePhone, "Mobile Phone");
        v.VendorEmail = Email(input.VendorEmail, "Vendor Email");
        v.RequestorEmail = Email(input.RequestorEmail, "Requestor Email");
    }

    private static string Show(string v) => v.Length == 0 ? "(blank)" : v;

    private static List<(string field, string value)> Snapshot(Vendor v) =>
    [
        ("Group", v.GroupId.ToString()), ("Vendor Name", v.VendorName), ("Address", v.Address ?? ""), ("City", v.City ?? ""),
        ("State", v.State ?? ""), ("Zip", v.Zip ?? ""), ("Country", v.Country ?? ""), ("Payment Terms", v.PaymentTerms ?? ""),
        ("Account #", v.AccountNumber ?? ""), ("Contact Name", v.ContactName ?? ""), ("Office Phone", v.OfficePhone ?? ""),
        ("Mobile Phone", v.MobilePhone ?? ""), ("Vendor Email", v.VendorEmail ?? ""), ("Requestor Email", v.RequestorEmail ?? ""),
    ];
}
