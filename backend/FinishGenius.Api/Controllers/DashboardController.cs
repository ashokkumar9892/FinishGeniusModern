using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Dashboard: KPIs, charts and the department panels of "My Work Processes".</summary>
[ApiController]
[Authorize(Roles = Access.Dashboard)]
[Route("api/dashboard")]
public class DashboardController(AppDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>
    /// GET /api/dashboard/summary?groupId=2&amp;tzOffset=240 — KPI cards and chart series.
    /// <c>tzOffset</c> (browser <c>getTimezoneOffset()</c>) decides day boundaries; defaults to the group time zone.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary([FromQuery] int groupId, [FromQuery] int? tzOffset)
    {
        await me.EnsureGroupAsync(groupId);
        var offset = tzOffset ?? await LocalDay.GroupOffsetMinutesAsync(db, groupId);
        var now = DateTime.UtcNow;
        var since7 = now.AddDays(-7);
        var since30 = now.AddDays(-30);
        var today = now.AddMinutes(-offset).Date;
        var chartFrom = today.AddDays(-13).AddMinutes(offset); // UTC start of the first local day

        var executions = db.WorkExecutions.AsNoTracking().Where(e => e.GroupId == groupId);
        var running = await executions.CountAsync(e => e.Status == ExecutionStatus.InProgress);
        var completed7 = await executions.CountAsync(e => e.Status == ExecutionStatus.Completed && e.CompletedAt >= since7);
        var started7 = await executions.CountAsync(e => e.StartedAt >= since7);

        var defects = db.WorkExecutionDefects.AsNoTracking().Where(d => db.WorkExecutions.Any(e => e.Id == d.ExecutionId && e.GroupId == groupId));
        var defects7 = await defects.Where(d => d.CreatedAt >= since7).SumAsync(d => (int?)d.Quantity) ?? 0;

        var devices = await db.Devices.AsNoTracking().Where(d => d.GroupId == groupId && !d.IsArchived)
            .Select(d => d.LastSeenAt).ToListAsync();
        var onlineCutoff = now - DevicesController.OnlineWindow;
        var schedules = await db.ProcessSchedules.CountAsync(s => s.GroupId == groupId && !s.IsArchived);

        // Executions per local day (last 14 days).
        var recent = await executions.Where(e => e.StartedAt >= chartFrom || e.CompletedAt >= chartFrom)
            .Select(e => new { e.StartedAt, e.CompletedAt, e.Status }).ToListAsync();
        var perDay = Enumerable.Range(0, 14).Select(i => today.AddDays(i - 13)).Select(day => new
        {
            Date = day.ToString("yyyy-MM-dd"),
            Started = recent.Count(e => e.StartedAt.AddMinutes(-offset).Date == day),
            Completed = recent.Count(e => e.Status == ExecutionStatus.Completed && e.CompletedAt != null && e.CompletedAt.Value.AddMinutes(-offset).Date == day),
            Cancelled = recent.Count(e => e.Status == ExecutionStatus.Cancelled && e.CompletedAt != null && e.CompletedAt.Value.AddMinutes(-offset).Date == day),
        }).ToList();

        var byType = await defects.Where(d => d.CreatedAt >= since30)
            .GroupBy(d => new { d.DefectTypeId, d.DefectType!.Name, d.DefectType.ChartColor })
            .Select(g => new { g.Key.DefectTypeId, g.Key.Name, Color = g.Key.ChartColor, Quantity = g.Sum(d => d.Quantity) })
            .OrderByDescending(x => x.Quantity).ToListAsync();

        var topSchedules = await executions.Where(e => e.StartedAt >= since30)
            .GroupBy(e => new { e.ScheduleId, e.Schedule!.Name, e.Schedule.Number })
            .Select(g => new { g.Key.ScheduleId, g.Key.Name, g.Key.Number, Runs = g.Count() })
            .OrderByDescending(x => x.Runs).ThenBy(x => x.Name).Take(5).ToListAsync();

        return Ok(new
        {
            ProcessesRunning = running,
            CompletedLast7Days = completed7,
            StartedLast7Days = started7,
            DefectsLast7Days = defects7,
            DevicesTotal = devices.Count,
            DevicesOnline = devices.Count(s => s != null && s >= onlineCutoff),
            SchedulesCount = schedules,
            ExecutionsPerDay = perDay,
            DefectsByType = byType,
            TopSchedules = topSchedules,
        });
    }

    /// <summary>
    /// GET /api/dashboard/departments?groupId=2&amp;search=line — department panels with their schedules.
    /// Schedules without a department are returned in an "Unassigned" panel (id = null).
    /// </summary>
    [HttpGet("departments")]
    public async Task<IActionResult> Departments([FromQuery] int groupId, [FromQuery] string? search)
    {
        await me.EnsureGroupAsync(groupId);
        var s = Text.Clean(search);
        var groupName = await db.Groups.Where(g => g.Id == groupId).Select(g => g.Name).FirstAsync();

        var depQuery = db.Departments.AsNoTracking().Where(d => d.GroupId == groupId);
        if (s != null) depQuery = depQuery.Where(d => d.Name.Contains(s));
        var departments = await depQuery.OrderBy(d => d.Name).ThenBy(d => d.Id).Select(d => new { d.Id, d.Name }).ToListAsync();

        var userId = me.Id;
        var schedules = await db.ProcessSchedules.AsNoTracking()
            .Where(p => p.GroupId == groupId && !p.IsArchived)
            .Select(p => new PanelSchedule(
                p.Id, groupId, groupName, p.Name, p.Number, p.DepartmentId,
                db.WorkExecutions.Where(e => e.ScheduleId == p.Id).Max(e => (DateTime?)e.StartedAt),
                db.WorkExecutions.Count(e => e.ScheduleId == p.Id),
                db.WorkExecutions
                    .Where(e => e.ScheduleId == p.Id && e.Status == ExecutionStatus.InProgress && e.UserId == userId)
                    .OrderByDescending(e => e.Id).Select(e => (int?)e.Id).FirstOrDefault()))
            .ToListAsync();

        var panels = departments.Select(d => new Panel(d.Id, d.Name, groupId, groupName,
            schedules.Where(p => p.DepartmentId == d.Id).OrderByDescending(p => p.LastRun).ToList())).ToList();

        var unassigned = schedules.Where(p => p.DepartmentId == null).OrderByDescending(p => p.LastRun).ToList();
        if (unassigned.Count > 0 && (s == null || "unassigned".Contains(s, StringComparison.OrdinalIgnoreCase)))
            panels.Add(new Panel(null, "Unassigned", groupId, groupName, unassigned));
        return Ok(panels);
    }

    private record PanelSchedule(int Id, int GroupId, string GroupName, string Name, string Number, int? DepartmentId,
        DateTime? LastRun, int Runs, int? ActiveExecutionId);

    private record Panel(int? Id, string Name, int GroupId, string GroupName, List<PanelSchedule> Schedules);
}

/// <summary>Maps UTC instants to "local days" for charts and telemetry, in JS <c>getTimezoneOffset()</c> minutes.</summary>
public static class LocalDay
{
    public static async Task<int> GroupOffsetMinutesAsync(AppDbContext db, int groupId)
    {
        var tz = await db.Groups.Where(g => g.Id == groupId).Select(g => g.TimeZone).FirstOrDefaultAsync();
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(tz) ? "UTC" : tz);
            return (int)-zone.GetUtcOffset(DateTime.UtcNow).TotalMinutes;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return 0;
        }
    }
}
