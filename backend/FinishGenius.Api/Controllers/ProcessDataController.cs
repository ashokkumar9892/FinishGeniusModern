using ClosedXML.Excel;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// What actually happened on one process schedule: every value recorded on the shop floor, the defects and the adders,
/// with the charts that make a drift visible. This is the old site's Graphs, Quality-Control charts and "Process Data
/// Details" (Time Stamps) on one screen, because they are three views of the same runs.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Dashboard)]
[Route("api/process-data")]
public class ProcessDataController(AppDbContext db, CurrentUser me) : ControllerBase
{
    /// <summary>
    /// How many runs one request will open. Runs are read one at a time on the old database (its tables have no
    /// indexes and a join across all of them fills tempdb), so this is what keeps the screen quick and the server safe.
    /// </summary>
    private const int MaxRuns = 40;

    /// <summary>Opened when the screen does not ask for more — enough to see a trend without a long wait.</summary>
    private const int DefaultRuns = 15;

    public record Entry(int RunId, DateTime At, string? User, int Step, string StepName, string Description,
        string? Value, string? Unit, decimal? Min, decimal? Max, bool? InRange);

    /// <summary>GET /api/process-data/{scheduleId}?from=&amp;to= — the runs of a schedule with everything recorded on them.</summary>
    [HttpGet("{scheduleId:int}")]
    public async Task<IActionResult> Get(int scheduleId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? runs)
    {
        var (schedule, executions, entries, defects, adders) = await LoadAsync(scheduleId, from, to, runs);

        // One series per thing measured, so a drift in "Viscosity" is a line you can see rather than rows to read.
        var series = entries
            .Where(e => decimal.TryParse(e.Value, out _))
            .GroupBy(e => e.Description)
            .Select(g => new
            {
                Name = g.Key,
                Unit = g.Select(x => x.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                Min = g.Select(x => x.Min).FirstOrDefault(m => m != null),
                Max = g.Select(x => x.Max).FirstOrDefault(m => m != null),
                Points = g.OrderBy(x => x.At).Select(x => new { x.At, Value = decimal.Parse(x.Value!), x.RunId, x.User }).ToList(),
            })
            .Where(s => s.Points.Count > 1)
            .OrderByDescending(s => s.Points.Count)
            .Take(8)
            .ToList();

        var inRange = entries.Where(e => e.InRange != null).ToList();
        var byDay = executions.GroupBy(e => DateOnly.FromDateTime(e.StartedAt))
            .OrderBy(g => g.Key)
            .Select(g => new { Date = g.Key.ToString("yyyy-MM-dd"), Runs = g.Count(), Completed = g.Count(x => x.Status == ExecutionStatus.Completed) })
            .ToList();

        return Ok(new
        {
            schedule = new { schedule.Id, schedule.Name, schedule.Number, schedule.CustomerName, schedule.GroupId },
            range = new { from, to, runsShown = executions.Count, cappedAt = MaxRuns, asked = Math.Clamp(runs ?? DefaultRuns, 1, MaxRuns) },
            totals = new
            {
                Runs = executions.Count,
                Completed = executions.Count(e => e.Status == ExecutionStatus.Completed),
                InProgress = executions.Count(e => e.Status == ExecutionStatus.InProgress),
                Entries = entries.Count,
                Measurements = entries.Count(e => decimal.TryParse(e.Value, out _)),
                // "Passed" counts only the entries a schedule actually set limits for; the rest are simply records.
                Checked = inRange.Count,
                Passed = inRange.Count(e => e.InRange == true),
                Failed = inRange.Count(e => e.InRange == false),
                Defects = defects.Sum(d => d.Quantity),
                Adders = adders.Count,
            },
            runsPerDay = byDay,
            series,
            defectsByType = defects.GroupBy(d => d.Type).Select(g => new { Name = g.Key, Quantity = g.Sum(x => x.Quantity) })
                .OrderByDescending(x => x.Quantity).ToList(),
            entries = entries.OrderByDescending(e => e.At).Take(2000),
            defects = defects.OrderByDescending(d => d.At).Take(1000),
            adders = adders.OrderByDescending(a => a.At).Take(1000),
        });
    }

    /// <summary>GET /api/process-data/{scheduleId}/excel — the same period as a workbook (one sheet per table).</summary>
    [HttpGet("{scheduleId:int}/excel")]
    public async Task<IActionResult> Excel(int scheduleId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? runs)
    {
        var (schedule, executions, entries, defects, adders) = await LoadAsync(scheduleId, from, to, runs);
        using var wb = new XLWorkbook();

        var runsSheet = wb.AddWorksheet("Runs");
        Header(runsSheet, "Run", "Started", "Completed", "Status", "Operator");
        var r = 2;
        foreach (var e in executions.OrderByDescending(x => x.StartedAt))
        {
            runsSheet.Cell(r, 1).Value = e.Id;
            runsSheet.Cell(r, 2).Value = e.StartedAt;
            runsSheet.Cell(r, 3).Value = e.CompletedAt;
            runsSheet.Cell(r, 4).Value = e.Status.ToString();
            runsSheet.Cell(r, 5).Value = e.User;
            r++;
        }

        var entrySheet = wb.AddWorksheet("Checklist entries");
        Header(entrySheet, "Run", "When", "Operator", "Step", "Step name", "Description", "Value", "Unit", "Min", "Max", "In range");
        r = 2;
        foreach (var e in entries.OrderByDescending(x => x.At))
        {
            entrySheet.Cell(r, 1).Value = e.RunId;
            entrySheet.Cell(r, 2).Value = e.At;
            entrySheet.Cell(r, 3).Value = e.User;
            entrySheet.Cell(r, 4).Value = e.Step;
            entrySheet.Cell(r, 5).Value = e.StepName;
            entrySheet.Cell(r, 6).Value = e.Description;
            entrySheet.Cell(r, 7).Value = e.Value;
            entrySheet.Cell(r, 8).Value = e.Unit;
            entrySheet.Cell(r, 9).Value = e.Min;
            entrySheet.Cell(r, 10).Value = e.Max;
            entrySheet.Cell(r, 11).Value = e.InRange == null ? "" : e.InRange.Value ? "Yes" : "No";
            r++;
        }

        var defectSheet = wb.AddWorksheet("Defects");
        Header(defectSheet, "Run", "When", "Operator", "Defect", "Quantity", "Notes");
        r = 2;
        foreach (var d in defects.OrderByDescending(x => x.At))
        {
            defectSheet.Cell(r, 1).Value = d.RunId;
            defectSheet.Cell(r, 2).Value = d.At;
            defectSheet.Cell(r, 3).Value = d.User;
            defectSheet.Cell(r, 4).Value = d.Type;
            defectSheet.Cell(r, 5).Value = d.Quantity;
            defectSheet.Cell(r, 6).Value = d.Notes;
            r++;
        }

        var adderSheet = wb.AddWorksheet("Adders");
        Header(adderSheet, "Run", "When", "Operator", "Adder", "Value");
        r = 2;
        foreach (var a in adders.OrderByDescending(x => x.At))
        {
            adderSheet.Cell(r, 1).Value = a.RunId;
            adderSheet.Cell(r, 2).Value = a.At;
            adderSheet.Cell(r, 3).Value = a.User;
            adderSheet.Cell(r, 4).Value = a.Type;
            adderSheet.Cell(r, 5).Value = a.Value;
            r++;
        }

        foreach (var sheet in wb.Worksheets)
        {
            sheet.Column(2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            sheet.Columns().AdjustToContents();
        }
        using var stream = new MemoryStream();
        wb.SaveAs(stream);
        var safe = new string((schedule.Name ?? scheduleId.ToString()).Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ProcessData-{(safe.Length == 0 ? scheduleId.ToString() : safe)}-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private static void Header(IXLWorksheet sheet, params string[] names)
    {
        for (var i = 0; i < names.Length; i++) sheet.Cell(1, i + 1).Value = names[i];
        sheet.Row(1).Style.Font.Bold = true;
    }

    private record Run(int Id, DateTime StartedAt, DateTime? CompletedAt, ExecutionStatus Status, string? User);
    private record DefectRow(int RunId, DateTime At, string? User, string Type, int Quantity, string? Notes);
    private record AdderRow(int RunId, DateTime At, string? User, string Type, string Value);

    private async Task<(ProcessSchedule Schedule, List<Run> Runs, List<Entry> Entries, List<DefectRow> Defects, List<AdderRow> Adders)>
        LoadAsync(int scheduleId, DateTime? from, DateTime? to, int? runs)
    {
        var schedule = await db.ProcessSchedules.AsNoTracking().FirstOrDefaultAsync(s => s.Id == scheduleId)
                       ?? throw ApiException.NotFound("Process schedule");
        await me.EnsureGroupAsync(schedule.GroupId);

        var take = Math.Clamp(runs ?? DefaultRuns, 1, MaxRuns);
        var q = db.WorkExecutions.AsNoTracking().Where(e => e.ScheduleId == scheduleId);
        if (from is { } f) q = q.Where(e => e.StartedAt >= f.Date);
        if (to is { } t) q = q.Where(e => e.StartedAt < t.Date.AddDays(1));
        var executions = await q.OrderByDescending(e => e.StartedAt).Take(take)
            .Select(e => new Run(e.Id, e.StartedAt, e.CompletedAt, e.Status, e.User!.Username))
            .ToListAsync();
        var ids = executions.Select(e => e.Id).ToList();

        var entries = new List<Entry>();
        var legacy = db is LegacyAppDbContext;
        foreach (var run in executions)
        {
            // One run at a time: on the old database this is the only shape that stays fast (see LegacyModel.MyWork).
            var lines = legacy ? await LegacyModel.RunLinesAsync(db, run.Id)
                : await db.Set<WorkExecutionLine>().AsNoTracking().Where(l => l.ExecutionId == run.Id).ToListAsync();
            var checks = legacy ? await LegacyModel.RunChecksAsync(db, run.Id)
                : await db.Set<WorkLineCheck>().AsNoTracking().Where(c => lines.Select(l => l.Id).Contains(c.LineId)).ToListAsync();
            var byLine = lines.ToDictionary(l => l.Id);
            foreach (var check in checks)
            {
                if (!byLine.TryGetValue(check.LineId, out var line)) continue;
                var value = check.RecordedValue ?? line.Value;
                bool? inRange = null;
                if ((line.MinValue != null || line.MaxValue != null) && decimal.TryParse(value, out var v))
                    inRange = (line.MinValue == null || v >= line.MinValue) && (line.MaxValue == null || v <= line.MaxValue);
                entries.Add(new Entry(run.Id, check.CheckedAt, check.UserName ?? run.User, line.StepNumber, line.StepName,
                    line.Description, value, line.Unit, line.MinValue, line.MaxValue, inRange));
            }
        }

        var defects = ids.Count == 0 ? [] : await db.WorkExecutionDefects.AsNoTracking()
            .Where(d => ids.Contains(d.ExecutionId))
            .Select(d => new DefectRow(d.ExecutionId, d.CreatedAt, null, d.DefectType!.Name, d.Quantity, d.Notes))
            .ToListAsync();
        var adders = ids.Count == 0 ? [] : await db.WorkExecutionAdders.AsNoTracking()
            .Where(a => ids.Contains(a.ExecutionId))
            .Select(a => new AdderRow(a.ExecutionId, a.CreatedAt, null, a.AdderType!.Name, a.Value))
            .ToListAsync();

        return (schedule, executions, entries, defects, adders);
    }
}
