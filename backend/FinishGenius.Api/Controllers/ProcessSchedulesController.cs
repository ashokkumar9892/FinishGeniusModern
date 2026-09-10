using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Process Schedules ("Process Systems"). The list endpoint is shared by Material Quantities, Pricing, My Work and Dashboard.</summary>
[ApiController]
[Authorize]
[Route("api/process-schedules")]
public class ProcessSchedulesController(AppDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>GET /api/process-schedules?groupId=1 — non-archived schedules of a group.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] bool includeArchived = false)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.ProcessSchedules.AsNoTracking()
            .Where(s => s.GroupId == groupId && (includeArchived || !s.IsArchived))
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.GroupId, GroupName = s.Group!.Name, s.Name, s.Number, s.CustomerName, s.DepartmentId,
                DepartmentName = s.Department != null ? s.Department.Name : null, s.IsArchived,
                StepCount = s.Steps.Count, s.CreatedAt, s.UpdatedAt,
            }).ToListAsync();
        return Ok(rows);
    }
}
