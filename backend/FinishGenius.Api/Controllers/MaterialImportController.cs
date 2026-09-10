using FinishGenius.Api.Data;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinishGenius.Api.Controllers;

/// <summary>"Blk Upld" on Equipment &amp; Materials and the Import page (same Excel template).</summary>
[ApiController]
[Authorize(Roles = Access.Import)]
[Route("api/material-import")]
public class MaterialImportController(AppDbContext db, FileStorage files, AuditService audit, CurrentUser me) : ControllerBase
{
    // MaterialImportService is not registered in DI (Program.cs is foundation), so it is composed here.
    private readonly MaterialImportService importer = new(db, files, audit, me);

    /// <summary>GET /api/material-import/template — the 5-sheet .xlsx template with one sample row per sheet.</summary>
    [HttpGet("template")]
    public IActionResult Template() =>
        File(MaterialImportService.BuildTemplate(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "MaterialsImportTemplate.xlsx");

    /// <summary>POST /api/material-import (multipart: groupId, file, documents[]).</summary>
    [HttpPost]
    [RequestSizeLimit(200L * 1024 * 1024)]
    public async Task<IActionResult> Import([FromForm] int groupId, IFormFile? file, [FromForm] List<IFormFile>? documents)
    {
        await me.EnsureGroupAsync(groupId);
        var result = await importer.ImportAsync(groupId, file, documents ?? []);
        return Ok(result);
    }
}
