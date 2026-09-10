using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Storage locations per material type ("Locs"). Used by inventory adjustments and the environmental report.</summary>
[ApiController]
[Authorize(Roles = Access.Materials)]
[Route("api/material-locations")]
public class MaterialLocationsController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    public record LocationInput(int GroupId, MaterialType MaterialType, string? Name);

    private const string Entity = "MaterialLocation";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] MaterialType? type)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.MaterialLocations.AsNoTracking().Where(l => l.GroupId == groupId && !l.IsDeleted);
        if (type != null) q = q.Where(l => l.MaterialType == type);
        var rows = await q.OrderBy(l => l.Name).ToListAsync();
        return Ok(rows.Select(l => new
        {
            l.Id, l.GroupId, l.Name, MaterialType = (int)l.MaterialType, MaterialTypeLabel = MaterialTypes.Label(l.MaterialType),
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(LocationInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        if (!Enum.IsDefined(input.MaterialType)) throw ApiException.Bad("Material Type is required.");
        var name = await ValidateName(input.GroupId, input.MaterialType, input.Name, 0);
        var l = new MaterialLocation { GroupId = input.GroupId, MaterialType = input.MaterialType, Name = name };
        db.MaterialLocations.Add(l);
        await db.SaveChangesAsync();
        audit.Log(Entity, l.Id, "Created", $"{MaterialTypes.Label(l.MaterialType)} location \"{l.Name}\"", l.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Location created.", id = l.Id });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, LocationInput input)
    {
        var l = await Load(id);
        var name = await ValidateName(l.GroupId, l.MaterialType, input.Name, l.Id);
        var old = l.Name;
        l.Name = name;
        audit.Log(Entity, l.Id, "Updated", $"Name: {old} → {name}", l.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Location updated.", id = l.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var l = await Load(id);
        l.IsDeleted = true;
        audit.Log(Entity, l.Id, "Deleted", $"\"{l.Name}\"", l.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Location deleted." });
    }

    private async Task<MaterialLocation> Load(int id)
    {
        var l = await db.MaterialLocations.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Location");
        await me.EnsureGroupAsync(l.GroupId);
        return l;
    }

    private async Task<string> ValidateName(int groupId, MaterialType type, string? input, int id)
    {
        var name = Text.Req(input, "Location name");
        if (await db.MaterialLocations.AnyAsync(x => x.GroupId == groupId && x.MaterialType == type && !x.IsDeleted && x.Name == name && x.Id != id))
            throw ApiException.Bad($"Location \"{name}\" already exists for {MaterialTypes.Label(type)} materials.");
        return name;
    }
}
