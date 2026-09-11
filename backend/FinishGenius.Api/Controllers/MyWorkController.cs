using System.Text.RegularExpressions;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

public record StartExecutionRequest(int ScheduleId);
public record CheckLineRequest(string? RecordedValue);
public record AddDefectRequest(int DefectTypeId, int Quantity, string? Notes);
public record AddAdderRequest(int AdderTypeId, string? Value);
public record ReorderRequest(List<int> Ids);
public record AssignDepartmentRequest(int? DepartmentId);
public record DefectTypeRequest(int GroupId, string? Name, string? ChartColor, bool? IsArchived);
public record AdderTypeRequest(int GroupId, string? Name, bool? IsArchived);

/// <summary>
/// My Work: runs ("executions") of process schedules on the shop floor — checklist, defects, adders, history —
/// plus the Processes Management tab (department assignment, defect and adder types).
/// </summary>
[ApiController]
[Authorize(Roles = Access.MyWork)]
[Route("api/my-work")]
public partial class MyWorkController(AppDbContext db, CurrentUser me, AuditService audit, ScheduleCalculator calculator) : ControllerBase
{
    private const string Entity = "WorkExecution";

    // -----------------------------------------------------------------------------------------
    // Executions
    // -----------------------------------------------------------------------------------------

    /// <summary>GET /api/my-work/executions?groupId=2&amp;status=InProgress|Completed|Cancelled|all</summary>
    [HttpGet("executions")]
    public async Task<IActionResult> Executions([FromQuery] int groupId, [FromQuery] string? status = "InProgress")
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.WorkExecutions.AsNoTracking().Where(e => e.GroupId == groupId);
        ExecutionStatus? filter = null;
        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            var s = Enum.TryParse<ExecutionStatus>(status, true, out var parsed) ? parsed : ExecutionStatus.InProgress;
            if (s == ExecutionStatus.Cancelled && db is LegacyAppDbContext) return Ok(Array.Empty<object>()); // cancelled runs are deleted there
            filter = s;
            q = q.Where(e => e.Status == s);
        }
        var ordered = q.OrderBy(e => e.Ordering).ThenBy(e => e.Id);
        // On the old database the counts and completion times come from one batch for all listed runs
        // (LegacyModel.RunSummariesAsync): per-run subqueries there cost close to a minute for a 600-run list on Prod.
        var legacy = db is LegacyAppDbContext;
        var rows = legacy
            ? await ordered.Select(e => new
            {
                e.Id, e.GroupId, GroupName = e.Group!.Name, e.ScheduleId, ScheduleName = e.Schedule!.Name,
                ScheduleNumber = e.Schedule.Number, e.UserId, UserName = e.User!.Username, e.StartedAt, CompletedAt = (DateTime?)null,
                Status = (int)e.Status, e.Ordering,
                TotalLines = 0,
                CheckedLines = 0,
                DefectCount = 0,
                AdderCount = 0,
            }).Take(2000).ToListAsync()
            : await ordered.Select(e => new
            {
                e.Id, e.GroupId, GroupName = e.Group!.Name, e.ScheduleId, ScheduleName = e.Schedule!.Name,
                ScheduleNumber = e.Schedule.Number, e.UserId, UserName = e.User!.Username, e.StartedAt, e.CompletedAt,
                Status = (int)e.Status, e.Ordering,
                TotalLines = e.Lines.Count,
                CheckedLines = e.Lines.Count(l => l.Checks.Any()),
                DefectCount = db.WorkExecutionDefects.Count(d => d.ExecutionId == e.Id),
                AdderCount = db.WorkExecutionAdders.Count(a => a.ExecutionId == e.Id),
            }).Take(2000).ToListAsync();
        var summaries = legacy ? await LegacyModel.RunSummariesAsync(db, groupId, filter) : null;
        return Ok(rows.Select(r =>
        {
            var sum = summaries?.GetValueOrDefault(r.Id);
            var total = summaries == null ? r.TotalLines : sum?.Total ?? 0;
            var done = summaries == null ? r.CheckedLines : sum?.Checked ?? 0;
            // Old runs have no completion time: a completed run ends at its last checklist entry.
            var completedAt = summaries == null ? r.CompletedAt
                : r.Status == (int)ExecutionStatus.Completed ? sum?.LastDate ?? r.StartedAt : null;
            return new
            {
                r.Id, r.GroupId, r.GroupName, r.ScheduleId, r.ScheduleName, r.ScheduleNumber, r.UserId, r.UserName,
                r.StartedAt, CompletedAt = completedAt, r.Status, StatusLabel = ((ExecutionStatus)r.Status).ToString(), r.Ordering,
                TotalLines = total, CheckedLines = done,
                Progress = total == 0 ? 0 : (int)Math.Round(100.0 * done / total),
                DefectCount = summaries == null ? r.DefectCount : sum?.Defects ?? 0,
                AdderCount = summaries == null ? r.AdderCount : sum?.Adders ?? 0,
            };
        }));
    }

    /// <summary>POST /api/my-work/executions { scheduleId } — "Start Process".</summary>
    [HttpPost("executions")]
    public async Task<IActionResult> Start([FromBody] StartExecutionRequest req)
    {
        var schedule = await db.ProcessSchedules.FirstOrDefaultAsync(s => s.Id == req.ScheduleId)
            ?? throw ApiException.NotFound("Process schedule");
        await me.EnsureGroupAsync(schedule.GroupId);
        if (schedule.IsArchived) throw ApiException.Bad("This process schedule is archived and cannot be started.");

        // On the old database the checklist is the My Work processes of the schedule's steps, created with the run.
        var legacy = db is LegacyAppDbContext;
        List<WorkExecutionLine> lines = legacy ? [] : await calculator.BuildChecklistAsync(schedule.Id);
        if (legacy && await LegacyModel.MyWorkProcessCountAsync(db, schedule.Id) == 0)
            throw ApiException.Bad("No active My Work process is set up for the steps of this process schedule.");
        if (!legacy && lines.Count == 0) throw ApiException.Bad("This process schedule has no steps. Add steps to the schedule before starting it.");

        var maxOrdering = await db.WorkExecutions.Where(e => e.GroupId == schedule.GroupId).MaxAsync(e => (int?)e.Ordering) ?? 0;
        var execution = new WorkExecution
        {
            GroupId = schedule.GroupId,
            ScheduleId = schedule.Id,
            UserId = me.Id,
            Status = ExecutionStatus.InProgress,
            Ordering = maxOrdering + 1,
            StartedAt = DateTime.UtcNow,
            Lines = lines,
        };
        db.WorkExecutions.Add(execution);
        await db.SaveChangesAsync();

        var lineCount = legacy ? (await LegacyModel.RunSummaryAsync(db, execution.Id)).Total : lines.Count;
        audit.Log(Entity, execution.Id, "Started", $"Process \"{schedule.Name}\" (#{schedule.Number}) started with {lineCount} checklist lines.", schedule.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Process started.", id = execution.Id });
    }

    /// <summary>GET /api/my-work/executions/{id} — header + checklist grouped by step.</summary>
    [HttpGet("executions/{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var e = await db.WorkExecutions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.GroupId, GroupName = x.Group!.Name, x.Group.ChecklistDeletionEnabled, x.ScheduleId,
                ScheduleName = x.Schedule!.Name, ScheduleNumber = x.Schedule.Number, x.Schedule.CustomerName,
                x.UserId, UserName = x.User!.Username, x.Status, x.Ordering, x.Notes, x.StartedAt, x.CompletedAt,
            }).FirstOrDefaultAsync() ?? throw ApiException.NotFound("Process");
        await me.EnsureGroupAsync(e.GroupId);

        var lines = db is LegacyAppDbContext
            ? await LegacyChecklistAsync(id)
            : await db.WorkExecutionLines.AsNoTracking().Where(l => l.ExecutionId == id)
                .OrderBy(l => l.StepNumber).ThenBy(l => l.Sequence).ThenBy(l => l.Id)
                .Select(l => new ChecklistLine(l.Id, l.StepNumber, l.StepName, l.Sequence, l.Description, l.Value, l.Unit, l.MinValue, l.MaxValue,
                    l.Checks.OrderBy(c => c.CheckedAt).ThenBy(c => c.Id)
                        .Select(c => new ChecklistCheck(c.Id, c.UserId, c.UserName, c.CheckedAt, c.RecordedValue)).ToList()))
                .ToListAsync();

        var defectCount = await db.WorkExecutionDefects.CountAsync(d => d.ExecutionId == id);
        var adderCount = await db.WorkExecutionAdders.CountAsync(a => a.ExecutionId == id);

        var steps = lines.GroupBy(l => new { l.StepNumber, l.StepName }).OrderBy(g => g.Key.StepNumber).Select(g => new
        {
            g.Key.StepNumber, g.Key.StepName,
            Lines = g.Select(l => new
            {
                l.Id, l.Sequence, l.Description, l.Value, l.Unit, l.MinValue, l.MaxValue, l.Checks,
                OutOfRange = l.Checks.Count > 0 && IsOutOfRange(l.Checks[^1].RecordedValue, l.MinValue, l.MaxValue),
            }).ToList(),
        }).ToList();

        return Ok(new
        {
            e.Id, e.GroupId, e.GroupName, e.ChecklistDeletionEnabled, e.ScheduleId, e.ScheduleName, e.ScheduleNumber,
            e.CustomerName, e.UserId, e.UserName, Status = (int)e.Status, StatusLabel = e.Status.ToString(), e.Ordering,
            e.Notes, e.StartedAt, e.CompletedAt,
            TotalLines = lines.Count, CheckedLines = lines.Count(l => l.Checks.Count > 0),
            DefectCount = defectCount, AdderCount = adderCount,
            CanUndoAny = me.IsAdmin,
            Steps = steps,
        });
    }

    private sealed record ChecklistCheck(int Id, int UserId, string? UserName, DateTime CheckedAt, string? RecordedValue);
    private sealed record ChecklistLine(int Id, int StepNumber, string StepName, int Sequence, string Description, string? Value, string? Unit,
        decimal? MinValue, decimal? MaxValue, List<ChecklistCheck> Checks);

    /// <summary>The old database's checklist, read with queries that filter by the run first (see LegacyModel.RunLinesAsync).</summary>
    private async Task<List<ChecklistLine>> LegacyChecklistAsync(int id)
    {
        var lines = await LegacyModel.RunLinesAsync(db, id);
        var checks = (await LegacyModel.RunChecksAsync(db, id)).ToLookup(c => c.LineId);
        return lines.OrderBy(l => l.StepNumber).ThenBy(l => l.Sequence).ThenBy(l => l.Id)
            .Select(l => new ChecklistLine(l.Id, l.StepNumber, l.StepName, l.Sequence, l.Description, l.Value, l.Unit, l.MinValue, l.MaxValue,
                checks[l.Id].OrderBy(c => c.CheckedAt).ThenBy(c => c.Id)
                    .Select(c => new ChecklistCheck(c.Id, c.UserId, c.UserName, c.CheckedAt, c.RecordedValue)).ToList()))
            .ToList();
    }

    private async Task<WorkExecutionLine?> FindLineAsync(int lineId) => db is LegacyAppDbContext
        ? await LegacyModel.RunLineAsync(db, lineId)
        : await db.WorkExecutionLines.FirstOrDefaultAsync(l => l.Id == lineId);

    /// <summary>POST /api/my-work/lines/{lineId}/check { recordedValue? } — stamps the line with date/time + user.</summary>
    [HttpPost("lines/{lineId:int}/check")]
    public async Task<IActionResult> Check(int lineId, [FromBody] CheckLineRequest? req)
    {
        var line = await FindLineAsync(lineId) ?? throw ApiException.NotFound("Checklist line");
        var execution = await LoadEditableAsync(line.ExecutionId);

        var recorded = Text.Clean(req?.RecordedValue);
        var hasRange = line.MinValue != null || line.MaxValue != null;
        if (recorded is { Length: > 100 }) throw ApiException.Bad("Recorded value must be 100 characters or fewer.");
        if (hasRange && recorded != null && ScheduleCalculator.Num(recorded) == null)
            throw ApiException.Bad("Recorded value must be a number.");
        var outOfRange = IsOutOfRange(recorded, line.MinValue, line.MaxValue);

        var check = new WorkLineCheck { LineId = line.Id, UserId = me.Id, UserName = me.UserName, RecordedValue = recorded, CheckedAt = DateTime.UtcNow };
        db.WorkLineChecks.Add(check);
        var details = $"Line checked \"#{line.StepNumber} {line.StepName} › {line.Description}\"" +
                      (recorded != null ? $" · recorded {recorded}{(line.Unit is { Length: > 0 } u && u != "-" ? " " + u : "")}" : "") +
                      (outOfRange ? " · OUT OF TOLERANCE" : "");
        audit.Log(Entity, execution.Id, "Line checked", details, execution.GroupId);
        await db.SaveChangesAsync();

        return Ok(new
        {
            message = outOfRange ? "Line checked — recorded value is out of tolerance." : "Line checked.",
            id = check.Id,
            outOfRange,
            check = new { check.Id, check.UserId, check.UserName, check.CheckedAt, check.RecordedValue },
        });
    }

    /// <summary>DELETE /api/my-work/checks/{checkId} — undo a check (own checks; administrators any).</summary>
    [HttpDelete("checks/{checkId:int}")]
    public async Task<IActionResult> Uncheck(int checkId)
    {
        var check = await db.WorkLineChecks.FirstOrDefaultAsync(c => c.Id == checkId) ?? throw ApiException.NotFound("Check");
        var line = await FindLineAsync(check.LineId) ?? throw ApiException.NotFound("Checklist line");
        var execution = await LoadEditableAsync(line.ExecutionId);
        if (check.UserId != me.Id && !me.IsAdmin)
            throw new ApiException(StatusCodes.Status403Forbidden, "You can only undo your own checks.");

        db.WorkLineChecks.Remove(check);
        audit.Log(Entity, execution.Id, "Check removed",
            $"Check removed from \"#{line.StepNumber} {line.StepName} › {line.Description}\" (checked by {check.UserName} at {check.CheckedAt:u})", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Check removed." });
    }

    /// <summary>POST /api/my-work/executions/{id}/complete</summary>
    [HttpPost("executions/{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        var execution = await LoadEditableAsync(id);
        var group = await db.Groups.AsNoTracking().FirstAsync(g => g.Id == execution.GroupId);
        int total, done;
        if (db is LegacyAppDbContext)
        {
            var summary = await LegacyModel.RunSummaryAsync(db, id);
            (total, done) = (summary.Total, summary.Checked);
        }
        else
        {
            total = await db.WorkExecutionLines.CountAsync(l => l.ExecutionId == id);
            done = await db.WorkExecutionLines.CountAsync(l => l.ExecutionId == id && l.Checks.Any());
        }

        execution.Status = ExecutionStatus.Completed;
        execution.CompletedAt = DateTime.UtcNow;
        var details = $"Process completed with {done} of {total} lines checked.";
        var legacy = db is LegacyAppDbContext;
        if (group.ChecklistDeletionEnabled && !legacy)
        {
            // "Enable Checklist Deletion on Submit": keep the lines, clear the marks.
            var checks = await db.WorkLineChecks.Where(c => db.WorkExecutionLines.Any(l => l.Id == c.LineId && l.ExecutionId == id)).ToListAsync();
            db.WorkLineChecks.RemoveRange(checks);
            details += $" Checklist marks cleared ({checks.Count}) — group setting \"Enable Checklist Deletion on Submit\".";
        }
        else if (group.ChecklistDeletionEnabled && await LegacyModel.ScheduleDeletionEnabledAsync(db, execution.ScheduleId))
        {
            // The old site's version of the setting: the submitted run's schedule becomes inactive (when the schedule allows it).
            var schedule = await db.ProcessSchedules.FirstAsync(s => s.Id == execution.ScheduleId);
            schedule.IsArchived = true;
            details += " Process schedule archived — group setting \"Enable Checklist Deletion on Submit\".";
        }
        audit.Log(Entity, id, "Completed", details, execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Process completed.", checklistCleared = group.ChecklistDeletionEnabled && !legacy });
    }

    /// <summary>POST /api/my-work/executions/{id}/cancel</summary>
    [HttpPost("executions/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var execution = await LoadEditableAsync(id);
        if (db is LegacyAppDbContext)
        {
            // The old database has no cancelled state: like the old site's "Remove", the run and its entries are deleted.
            db.WorkExecutions.Remove(execution);
            audit.Log(Entity, id, "Cancelled", "Process cancelled and removed.", execution.GroupId);
            await db.SaveChangesAsync();
            return Ok(new { message = "Process cancelled and removed." });
        }
        execution.Status = ExecutionStatus.Cancelled;
        execution.CompletedAt = DateTime.UtcNow;
        audit.Log(Entity, id, "Cancelled", "Process cancelled.", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Process cancelled." });
    }

    /// <summary>PUT /api/my-work/executions/order { ids[] } — drag &amp; drop reorder of the progress grid.</summary>
    [HttpPut("executions/order")]
    public async Task<IActionResult> Reorder([FromBody] ReorderRequest req)
    {
        var ids = (req.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Nothing to reorder.");
        var rows = await db.WorkExecutions.Where(e => ids.Contains(e.Id)).ToListAsync();
        if (rows.Count != ids.Count) throw ApiException.NotFound("Process");
        foreach (var gid in rows.Select(r => r.GroupId).Distinct()) await me.EnsureGroupAsync(gid);

        // Re-use the rows' existing ordering slots so their position relative to other rows is preserved.
        var slots = rows.Select(r => r.Ordering).OrderBy(o => o).ToList();
        if (slots.Distinct().Count() != slots.Count) slots = Enumerable.Range(slots.Min(), slots.Count).ToList();
        var byId = rows.ToDictionary(r => r.Id);
        for (var i = 0; i < ids.Count; i++) byId[ids[i]].Ordering = slots[i];
        await db.SaveChangesAsync();
        return Ok(new { message = "Order updated." });
    }

    /// <summary>GET /api/my-work/executions/{id}/history — time-ordered timeline.</summary>
    [HttpGet("executions/{id:int}/history")]
    public async Task<IActionResult> History(int id)
    {
        var e = await db.WorkExecutions.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.GroupId, x.StartedAt, UserName = x.User!.Username }).FirstOrDefaultAsync()
            ?? throw ApiException.NotFound("Process");
        await me.EnsureGroupAsync(e.GroupId);

        var logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == Entity && a.EntityId == id)
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.Action, a.Details, a.UserName, a.CreatedAt })
            .ToListAsync();

        var events = logs.Select(a => new HistoryEvent(a.Id, Kind(a.Action), a.Action, a.Details, a.UserName, a.CreatedAt)).ToList();
        if (!events.Any(x => x.Kind == "started"))
            events.Insert(0, new HistoryEvent(0, "started", "Started", "Process started.", e.UserName, e.StartedAt));
        return Ok(events);
    }

    private record HistoryEvent(long Id, string Kind, string Action, string? Details, string? UserName, DateTime CreatedAt);

    private static string Kind(string action) => action switch
    {
        "Started" => "started",
        "Line checked" => "checked",
        "Check removed" => "unchecked",
        "Defect added" => "defect",
        "Defect removed" => "defectRemoved",
        "Adder added" => "adder",
        "Adder removed" => "adderRemoved",
        "Completed" => "completed",
        "Cancelled" => "cancelled",
        _ => "other",
    };

    // -----------------------------------------------------------------------------------------
    // Defects & adders recorded on an execution
    // -----------------------------------------------------------------------------------------

    [HttpGet("executions/{id:int}/defects")]
    public async Task<IActionResult> Defects(int id)
    {
        await LoadAsync(id);
        var rows = await db.WorkExecutionDefects.AsNoTracking().Where(d => d.ExecutionId == id)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id)
            .Select(d => new
            {
                d.Id, d.DefectTypeId, DefectTypeName = d.DefectType!.Name, d.DefectType.ChartColor, d.Quantity, d.Notes,
                d.UserId, UserName = db.Users.Where(u => u.Id == d.UserId).Select(u => u.Username).FirstOrDefault(), d.CreatedAt,
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("executions/{id:int}/defects")]
    public async Task<IActionResult> AddDefect(int id, [FromBody] AddDefectRequest req)
    {
        var execution = await LoadEditableAsync(id);
        var type = await db.DefectTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.DefectTypeId && t.GroupId == execution.GroupId && !t.IsArchived)
            ?? throw ApiException.Bad("Defect type is required.");
        if (req.Quantity < 1) throw ApiException.Bad("Quantity must be at least 1.");
        if (req.Quantity > 100000) throw ApiException.Bad("Quantity is too large.");
        var notes = Text.Clean(req.Notes);
        if (notes is { Length: > 400 }) throw ApiException.Bad("Notes must be 400 characters or fewer.");

        var defect = new WorkExecutionDefect { ExecutionId = id, DefectTypeId = type.Id, Quantity = req.Quantity, Notes = notes, UserId = me.Id };
        db.WorkExecutionDefects.Add(defect);
        audit.Log(Entity, id, "Defect added", $"{type.Name} × {req.Quantity}{(notes != null ? $" — {notes}" : "")}", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Defect added.", id = defect.Id });
    }

    [HttpDelete("defects/{defectId:int}")]
    public async Task<IActionResult> DeleteDefect(int defectId)
    {
        var defect = await db.WorkExecutionDefects.Include(d => d.DefectType).FirstOrDefaultAsync(d => d.Id == defectId)
            ?? throw ApiException.NotFound("Defect");
        var execution = await LoadEditableAsync(defect.ExecutionId);
        db.WorkExecutionDefects.Remove(defect);
        audit.Log(Entity, execution.Id, "Defect removed", $"{defect.DefectType?.Name} × {defect.Quantity}", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Defect removed." });
    }

    [HttpGet("executions/{id:int}/adders")]
    public async Task<IActionResult> Adders(int id)
    {
        await LoadAsync(id);
        var rows = await db.WorkExecutionAdders.AsNoTracking().Where(a => a.ExecutionId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Select(a => new
            {
                a.Id, a.AdderTypeId, AdderTypeName = a.AdderType!.Name, a.Value, a.UserId,
                UserName = db.Users.Where(u => u.Id == a.UserId).Select(u => u.Username).FirstOrDefault(), a.CreatedAt,
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("executions/{id:int}/adders")]
    public async Task<IActionResult> AddAdder(int id, [FromBody] AddAdderRequest req)
    {
        var execution = await LoadEditableAsync(id);
        var type = await db.AdderTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == req.AdderTypeId && t.GroupId == execution.GroupId && !t.IsArchived)
            ?? throw ApiException.Bad("Adder type is required.");
        var value = Text.Req(req.Value, "Value");
        if (value.Length > 400) throw ApiException.Bad("Value must be 400 characters or fewer.");

        var adder = new WorkExecutionAdder { ExecutionId = id, AdderTypeId = type.Id, Value = value, UserId = me.Id };
        db.WorkExecutionAdders.Add(adder);
        audit.Log(Entity, id, "Adder added", $"{type.Name}: {value}", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Adder added.", id = adder.Id });
    }

    [HttpDelete("adders/{adderId:int}")]
    public async Task<IActionResult> DeleteAdder(int adderId)
    {
        var adder = await db.WorkExecutionAdders.Include(a => a.AdderType).FirstOrDefaultAsync(a => a.Id == adderId)
            ?? throw ApiException.NotFound("Adder");
        var execution = await LoadEditableAsync(adder.ExecutionId);
        db.WorkExecutionAdders.Remove(adder);
        audit.Log(Entity, execution.Id, "Adder removed", $"{adder.AdderType?.Name}: {adder.Value}", execution.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Adder removed." });
    }

    // -----------------------------------------------------------------------------------------
    // Processes Management
    // -----------------------------------------------------------------------------------------

    /// <summary>GET /api/my-work/processes?groupId=2 — schedules with their department and run statistics.</summary>
    [HttpGet("processes")]
    public async Task<IActionResult> Processes([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.ProcessSchedules.AsNoTracking()
            .Where(s => s.GroupId == groupId && !s.IsArchived)
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.GroupId, GroupName = s.Group!.Name, s.Name, s.Number, s.CustomerName, s.DepartmentId,
                DepartmentName = s.Department != null ? s.Department.Name : null,
                StepCount = s.Steps.Count,
                Runs = db.WorkExecutions.Count(e => e.ScheduleId == s.Id),
                ActiveRuns = db.WorkExecutions.Count(e => e.ScheduleId == s.Id && e.Status == ExecutionStatus.InProgress),
                LastRun = db.WorkExecutions.Where(e => e.ScheduleId == s.Id).Max(e => (DateTime?)e.StartedAt),
            }).ToListAsync();
        return Ok(rows);
    }

    /// <summary>PUT /api/my-work/processes/{scheduleId}/department { departmentId|null }</summary>
    [HttpPut("processes/{scheduleId:int}/department")]
    public async Task<IActionResult> AssignDepartment(int scheduleId, [FromBody] AssignDepartmentRequest req)
    {
        var schedule = await db.ProcessSchedules.FirstOrDefaultAsync(s => s.Id == scheduleId) ?? throw ApiException.NotFound("Process schedule");
        await me.EnsureGroupAsync(schedule.GroupId);
        string? newName = null;
        if (req.DepartmentId is { } depId)
        {
            var dep = await db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == depId && d.GroupId == schedule.GroupId)
                ?? throw ApiException.Bad("Department must belong to the schedule's group.");
            newName = dep.Name;
        }
        var oldName = schedule.DepartmentId == null ? null
            : await db.Departments.Where(d => d.Id == schedule.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync();
        if (schedule.DepartmentId == req.DepartmentId) return Ok(new { message = "Department updated." });

        schedule.DepartmentId = req.DepartmentId;
        schedule.UpdatedAt = DateTime.UtcNow;
        audit.Log("ProcessSchedule", schedule.Id, "Department changed", $"Department {oldName ?? "(none)"} → {newName ?? "(none)"}", schedule.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Department updated." });
    }

    // ---- Defect types -------------------------------------------------------------------------

    [HttpGet("defect-types")]
    public async Task<IActionResult> DefectTypes([FromQuery] int groupId, [FromQuery] bool includeArchived = false)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.DefectTypes.AsNoTracking()
            .Where(t => t.GroupId == groupId && (includeArchived || !t.IsArchived))
            .OrderBy(t => t.IsArchived).ThenBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.GroupId, t.Name, t.ChartColor, t.IsArchived,
                UsageCount = db.WorkExecutionDefects.Count(d => d.DefectTypeId == t.Id),
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("defect-types")]
    public async Task<IActionResult> CreateDefectType([FromBody] DefectTypeRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var name = Text.Req(req.Name, "Defect Type Name");
        var color = ValidColor(req.ChartColor);
        if (await db.DefectTypes.AnyAsync(t => t.GroupId == req.GroupId && !t.IsArchived && t.Name == name))
            throw ApiException.Bad("Defect type already exists.");
        var t = new DefectType { GroupId = req.GroupId, Name = name, ChartColor = color };
        db.DefectTypes.Add(t);
        await db.SaveChangesAsync();
        audit.Log("DefectType", t.Id, "Created", $"{name} ({color})", req.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Defect type created.", id = t.Id });
    }

    [HttpPut("defect-types/{id:int}")]
    public async Task<IActionResult> UpdateDefectType(int id, [FromBody] DefectTypeRequest req)
    {
        var t = await db.DefectTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Defect type");
        await me.EnsureGroupAsync(t.GroupId);
        var name = Text.Req(req.Name, "Defect Type Name");
        var color = ValidColor(req.ChartColor);
        var archived = req.IsArchived ?? t.IsArchived;
        if (!archived && await db.DefectTypes.AnyAsync(x => x.GroupId == t.GroupId && x.Id != id && !x.IsArchived && x.Name == name))
            throw ApiException.Bad("Defect type already exists.");
        var changes = Changes(("Name", t.Name, name), ("Color", t.ChartColor, color), ("Archived", t.IsArchived.ToString(), archived.ToString()));
        t.Name = name;
        t.ChartColor = color;
        t.IsArchived = archived;
        audit.Log("DefectType", t.Id, "Updated", changes, t.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Defect type updated." });
    }

    /// <summary>Deletes an unused defect type; archives it when defects already reference it.</summary>
    [HttpDelete("defect-types/{id:int}")]
    public async Task<IActionResult> DeleteDefectType(int id)
    {
        var t = await db.DefectTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Defect type");
        await me.EnsureGroupAsync(t.GroupId);
        if (await db.WorkExecutionDefects.AnyAsync(d => d.DefectTypeId == id))
        {
            t.IsArchived = true;
            audit.Log("DefectType", t.Id, "Archived", t.Name, t.GroupId);
            await db.SaveChangesAsync();
            return Ok(new { message = "Defect type archived (it is used by recorded defects).", archived = true });
        }
        db.DefectTypes.Remove(t);
        audit.Log("DefectType", t.Id, "Deleted", t.Name, t.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Defect type deleted.", archived = false });
    }

    // ---- Adder types --------------------------------------------------------------------------

    [HttpGet("adder-types")]
    public async Task<IActionResult> AdderTypes([FromQuery] int groupId, [FromQuery] bool includeArchived = false)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.AdderTypes.AsNoTracking()
            .Where(t => t.GroupId == groupId && (includeArchived || !t.IsArchived))
            .OrderBy(t => t.IsArchived).ThenBy(t => t.Name)
            .Select(t => new
            {
                t.Id, t.GroupId, t.Name, t.IsArchived,
                UsageCount = db.WorkExecutionAdders.Count(a => a.AdderTypeId == t.Id),
            }).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("adder-types")]
    public async Task<IActionResult> CreateAdderType([FromBody] AdderTypeRequest req)
    {
        await me.EnsureGroupAsync(req.GroupId);
        var name = Text.Req(req.Name, "Adder Type Name");
        if (await db.AdderTypes.AnyAsync(t => t.GroupId == req.GroupId && !t.IsArchived && t.Name == name))
            throw ApiException.Bad("Adder type already exists.");
        var t = new AdderType { GroupId = req.GroupId, Name = name };
        db.AdderTypes.Add(t);
        await db.SaveChangesAsync();
        audit.Log("AdderType", t.Id, "Created", name, req.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Adder type created.", id = t.Id });
    }

    [HttpPut("adder-types/{id:int}")]
    public async Task<IActionResult> UpdateAdderType(int id, [FromBody] AdderTypeRequest req)
    {
        var t = await db.AdderTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Adder type");
        await me.EnsureGroupAsync(t.GroupId);
        var name = Text.Req(req.Name, "Adder Type Name");
        var archived = req.IsArchived ?? t.IsArchived;
        if (!archived && await db.AdderTypes.AnyAsync(x => x.GroupId == t.GroupId && x.Id != id && !x.IsArchived && x.Name == name))
            throw ApiException.Bad("Adder type already exists.");
        var changes = Changes(("Name", t.Name, name), ("Archived", t.IsArchived.ToString(), archived.ToString()));
        t.Name = name;
        t.IsArchived = archived;
        audit.Log("AdderType", t.Id, "Updated", changes, t.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Adder type updated." });
    }

    [HttpDelete("adder-types/{id:int}")]
    public async Task<IActionResult> DeleteAdderType(int id)
    {
        var t = await db.AdderTypes.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Adder type");
        await me.EnsureGroupAsync(t.GroupId);
        if (await db.WorkExecutionAdders.AnyAsync(a => a.AdderTypeId == id))
        {
            t.IsArchived = true;
            audit.Log("AdderType", t.Id, "Archived", t.Name, t.GroupId);
            await db.SaveChangesAsync();
            return Ok(new { message = "Adder type archived (it is used by recorded adders).", archived = true });
        }
        db.AdderTypes.Remove(t);
        audit.Log("AdderType", t.Id, "Deleted", t.Name, t.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Adder type deleted.", archived = false });
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private async Task<WorkExecution> LoadAsync(int id)
    {
        var e = await db.WorkExecutions.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Process");
        await me.EnsureGroupAsync(e.GroupId);
        return e;
    }

    /// <summary>Loads an execution that may still be changed (in progress).</summary>
    private async Task<WorkExecution> LoadEditableAsync(int id)
    {
        var e = await LoadAsync(id);
        if (e.Status != ExecutionStatus.InProgress)
            throw ApiException.Bad($"This process is {(e.Status == ExecutionStatus.Completed ? "completed" : "cancelled")} and can no longer be changed.");
        return e;
    }

    public static bool IsOutOfRange(string? recorded, decimal? min, decimal? max)
    {
        if (min == null && max == null) return false;
        var v = ScheduleCalculator.Num(recorded);
        if (v == null) return false;
        return (min != null && v < min) || (max != null && v > max);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    private static string ValidColor(string? color)
    {
        var c = Text.Clean(color) ?? "#f97316";
        if (!HexColor().IsMatch(c)) throw ApiException.Bad("Chart colour must be a hex colour like #f97316.");
        return c.ToLowerInvariant();
    }

    private static string Changes(params (string field, string? from, string? to)[] items)
    {
        var diffs = items.Where(i => i.from != i.to).Select(i => $"{i.field} {i.from ?? "(empty)"} → {i.to ?? "(empty)"}").ToList();
        return diffs.Count == 0 ? "No changes" : string.Join("; ", diffs);
    }
}
