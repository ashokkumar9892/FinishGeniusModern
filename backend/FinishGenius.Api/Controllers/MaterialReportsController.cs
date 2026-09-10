using ClosedXML.Excel;
using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Reports tab → Environmental Report (consumption × VOC / HAP / TAP in lb per gallon).</summary>
[ApiController]
[Authorize(Roles = Access.Materials)]
[Route("api/material-reports")]
public class MaterialReportsController(AppDbContext db, CurrentUser me) : ControllerBase
{
    public record EnvironmentalRow(
        string GroupName, string? CategoryName, string ProductName, string? ProductCode, string? LocationName, string? CustomerName,
        decimal TotalGallons, decimal TotalVoc, decimal TotalHap, decimal TotalTap);

    /// <summary>GET /api/material-reports/environmental?groupId=1&amp;from=2026-01-01&amp;to=2026-12-31 (dates inclusive).</summary>
    [HttpGet("environmental")]
    public async Task<IActionResult> Environmental([FromQuery] int groupId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var rows = await BuildAsync(groupId, from, to);
        return Ok(new
        {
            from = from?.Date, to = to?.Date, rows,
            totals = new
            {
                totalGallons = rows.Sum(r => r.TotalGallons), totalVoc = rows.Sum(r => r.TotalVoc),
                totalHap = rows.Sum(r => r.TotalHap), totalTap = rows.Sum(r => r.TotalTap),
            },
        });
    }

    [HttpGet("environmental/excel")]
    public async Task<IActionResult> EnvironmentalExcel([FromQuery] int groupId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var rows = await BuildAsync(groupId, from, to);
        var groupName = await db.Groups.Where(g => g.Id == groupId).Select(g => g.Name).FirstOrDefaultAsync() ?? "";

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Environmental Report");
        ws.Cell(1, 1).Value = "Environmental Report";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).Value = $"{groupName} · {(from == null ? "All dates" : from.Value.ToString("MM/dd/yyyy"))} – {(to == null ? "today" : to.Value.ToString("MM/dd/yyyy"))}";
        ws.Cell(2, 1).Style.Font.SetFontColor(XLColor.Gray);

        string[] headers =
        [
            "Group", "Category", "Product Name", "Product Number", "Location", "Customer Name", "Total Gallons Consumed",
            "Total VOC's (lb) Emitted", "Total HAP's (lb) Emitted", "Total TAP's (lb) Emitted",
        ];
        const int headerRow = 4;
        for (var i = 0; i < headers.Length; i++) ws.Cell(headerRow, i + 1).Value = headers[i];
        var header = ws.Range(headerRow, 1, headerRow, headers.Length);
        header.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#E8660F"));

        var r = headerRow + 1;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.GroupName;
            ws.Cell(r, 2).Value = x.CategoryName ?? "";
            ws.Cell(r, 3).Value = x.ProductName;
            ws.Cell(r, 4).Value = x.ProductCode ?? "";
            ws.Cell(r, 5).Value = x.LocationName ?? "";
            ws.Cell(r, 6).Value = x.CustomerName ?? "";
            ws.Cell(r, 7).Value = x.TotalGallons;
            ws.Cell(r, 8).Value = x.TotalVoc;
            ws.Cell(r, 9).Value = x.TotalHap;
            ws.Cell(r, 10).Value = x.TotalTap;
            r++;
        }
        ws.Cell(r, 6).Value = "Total";
        ws.Cell(r, 7).Value = rows.Sum(x => x.TotalGallons);
        ws.Cell(r, 8).Value = rows.Sum(x => x.TotalVoc);
        ws.Cell(r, 9).Value = rows.Sum(x => x.TotalHap);
        ws.Cell(r, 10).Value = rows.Sum(x => x.TotalTap);
        ws.Range(r, 1, r, headers.Length).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
        ws.Range(headerRow + 1, 7, r, 10).Style.NumberFormat.Format = "#,##0.00";
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns(1, headers.Length).AdjustToContents(headerRow, r);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"EnvironmentalReport_{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private async Task<List<EnvironmentalRow>> BuildAsync(int groupId, DateTime? from, DateTime? to)
    {
        await me.EnsureGroupAsync(groupId);
        if (from != null && to != null && from.Value.Date > to.Value.Date) throw ApiException.Bad("The From date must be on or before the To date.");
        var q = db.InventoryTransactions.AsNoTracking().Where(t => t.GroupId == groupId && t.Quantity < 0);
        if (from != null) q = q.Where(t => t.CreatedAt >= from.Value.Date);
        if (to != null)
        {
            var end = to.Value.Date.AddDays(1);
            q = q.Where(t => t.CreatedAt < end);
        }

        var grouped = await q
            .GroupBy(t => new { t.MaterialId, t.LocationId, t.CustomerName })
            .Select(g => new { g.Key.MaterialId, g.Key.LocationId, g.Key.CustomerName, Gallons = -g.Sum(t => t.Quantity) })
            .ToListAsync();

        var materialIds = grouped.Select(g => g.MaterialId).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, GroupName = m.Group!.Name, CategoryName = m.Category != null ? m.Category.Name : null, m.ProductName, m.ProductCode, m.Voc, m.Hap, m.Tap })
            .ToDictionaryAsync(m => m.Id);
        var locationIds = grouped.Where(g => g.LocationId != null).Select(g => g.LocationId!.Value).Distinct().ToList();
        var locations = await db.MaterialLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name);

        return grouped
            .Where(g => materials.ContainsKey(g.MaterialId))
            .Select(g =>
            {
                var m = materials[g.MaterialId];
                return new EnvironmentalRow(
                    m.GroupName, m.CategoryName, m.ProductName, m.ProductCode,
                    g.LocationId != null && locations.TryGetValue(g.LocationId.Value, out var ln) ? ln : null,
                    g.CustomerName, g.Gallons, Math.Round(g.Gallons * m.Voc, 4), Math.Round(g.Gallons * m.Hap, 4), Math.Round(g.Gallons * m.Tap, 4));
            })
            .OrderBy(x => x.CategoryName).ThenBy(x => x.ProductName).ThenBy(x => x.LocationName).ThenBy(x => x.CustomerName)
            .ToList();
    }
}
