using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

public record DepartmentRequest(int GroupId, string? Name);

/// <summary>Departments (Dashboard › Department Management). They group process schedules into dashboard panels.</summary>
[ApiController]
[Authorize]
[Route("api/departments")]
public class DepartmentsController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    private const string Entity = "Department";

    /// <summary>GET /api/departments?groupId=2&amp;search=line</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] string? search)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.Departments.AsNoTracking().Where(d => d.GroupId == groupId);
        var s = Text.Clean(search);
        if (s != null) q = q.Where(d => d.Name.Contains(s));
        var rows = await q.OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Select(d => new
            {
                d.Id, d.GroupId, GroupName = d.Group!.Name, d.Name,
                ScheduleCount = db.ProcessSchedules.Count(p => p.DepartmentId == d.Id && !p.IsArchived),
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Create([FromBody] DepartmentRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var name = Text.Req(req.Name, "Department Name");
        var d = new Department { GroupId = req.GroupId, Name = name };
        db.Departments.Add(d);
        await db.SaveChangesAsync();
        audit.Log(Entity, d.Id, "Created", name, req.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Department created.", id = d.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Update(int id, [FromBody] DepartmentRequest req)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Department");
        await me.EnsureGroupAsync(d.GroupId);
        var name = Text.Req(req.Name, "Department Name");
        var groupId = req.GroupId > 0 ? req.GroupId : d.GroupId;
        if (groupId != d.GroupId)
        {
            await me.EnsureGroupAsync(groupId);
            if (await db.ProcessSchedules.AnyAsync(p => p.DepartmentId == id))
                throw ApiException.Bad("This department cannot be moved to another group because process schedules are assigned to it.");
        }
        var details = new List<string>();
        if (d.Name != name) details.Add($"Name {d.Name} → {name}");
        if (d.GroupId != groupId) details.Add($"Group {d.GroupId} → {groupId}");
        d.Name = name;
        d.GroupId = groupId;
        audit.Log(Entity, d.Id, "Updated", details.Count == 0 ? "No changes" : string.Join("; ", details), groupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Department updated." });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.Dashboard)]
    public async Task<IActionResult> Delete(int id)
    {
        var d = await db.Departments.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Department");
        await me.EnsureGroupAsync(d.GroupId);
        var assigned = await db.ProcessSchedules.CountAsync(p => p.DepartmentId == id);
        if (assigned > 0)
            throw ApiException.Bad($"The \"{d.Name}\" department cannot be deleted because it is assigned to {assigned} process schedule{(assigned == 1 ? "" : "s")}. Reassign them in My Work › Processes Management first.");
        db.Departments.Remove(d);
        audit.Log(Entity, d.Id, "Deleted", d.Name, d.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Department deleted." });
    }
}
