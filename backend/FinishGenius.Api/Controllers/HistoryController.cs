using ClosedXML.Excel;
using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Audit trail shown by every "History" (⟲) action.</summary>
[ApiController]
[Authorize]
[Route("api/history")]
public class HistoryController(AppDbContext db, CurrentUser me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string entityType, [FromQuery] int entityId)
    {
        var groupIds = await me.GroupIdsAsync();
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == entityType && a.EntityId == entityId && (a.GroupId == null || groupIds.Contains(a.GroupId.Value)))
            .OrderByDescending(a => a.Id).Take(500)
            .Select(a => new { a.Id, a.Action, a.Details, a.UserName, a.CreatedAt })
            .ToListAsync();
        return Ok(rows);
    }

    /// <summary>
    /// GET /api/history/group?groupId=1&amp;from=2026-01-01&amp;to=2026-03-31 — everything that happened in a group over a
    /// period, whatever it happened to (the old site's "Group Download"). Newest first, capped so one query cannot pull
    /// years of rows across the wire; the Excel download takes the same range in full.
    /// </summary>
    [HttpGet("group")]
    public async Task<IActionResult> Group([FromQuery] int groupId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? search, [FromQuery] int? take)
    {
        var rows = await GroupRowsAsync(groupId, from, to, search, Math.Clamp(take ?? 1000, 1, 5000));
        return Ok(rows);
    }

    /// <summary>GET /api/history/group/excel — the same rows as a spreadsheet.</summary>
    [HttpGet("group/excel")]
    public async Task<IActionResult> GroupExcel([FromQuery] int groupId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? search)
    {
        var rows = await GroupRowsAsync(groupId, from, to, search, 50000);
        var group = await db.Groups.AsNoTracking().Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync();

        using var wb = new XLWorkbook();
        var sheet = wb.AddWorksheet("Group history");
        string[] headers = ["When (UTC)", "User", "Screen", "Record", "Action", "Details", "Old value", "New value"];
        for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
        sheet.Row(1).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
        {
            sheet.Cell(r, 1).Value = row.CreatedAt;
            sheet.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            sheet.Cell(r, 2).Value = row.UserName;
            sheet.Cell(r, 3).Value = row.EntityType;
            sheet.Cell(r, 4).Value = row.EntityId;
            sheet.Cell(r, 5).Value = row.Action;
            sheet.Cell(r, 6).Value = row.Details;
            sheet.Cell(r, 7).Value = row.OldValue;
            sheet.Cell(r, 8).Value = row.NewValue;
            r++;
        }
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        // A group name can hold anything; keep only what is safe in a file name.
        var safe = new string((group ?? groupId.ToString()).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        var name = $"GroupHistory-{(safe.Length == 0 ? groupId.ToString() : safe)}-{DateTime.UtcNow:yyyyMMdd}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    private async Task<List<AuditRow>> GroupRowsAsync(int groupId, DateTime? from, DateTime? to, string? search, int take)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.AuditLogs.AsNoTracking().Where(a => a.GroupId == groupId);
        if (from is { } f) q = q.Where(a => a.CreatedAt >= f.Date);
        // "to" is a day the user picked, and they mean the whole of it.
        if (to is { } t) q = q.Where(a => a.CreatedAt < t.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(a => a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s))
                             || (a.UserName != null && a.UserName.Contains(s)) || a.EntityType.Contains(s));
        }
        return await q.OrderByDescending(a => a.Id).Take(take)
            .Select(a => new AuditRow(a.Id, a.EntityType, a.EntityId, a.Action, a.Details, a.OldValue, a.NewValue, a.UserName, a.CreatedAt))
            .ToListAsync();
    }

    private record AuditRow(long Id, string EntityType, int EntityId, string Action, string? Details, string? OldValue,
        string? NewValue, string? UserName, DateTime CreatedAt);
}
