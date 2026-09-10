using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Streams stored uploads. Paths start with a folder and the owning group id ("photos/12/x.jpg"),
/// so access is checked against the caller's groups. Logos are readable by any signed-in user.
/// </summary>
[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController(FileStorage files, CurrentUser me) : ControllerBase
{
    /// <param name="download">"1"/"true" = send as an attachment.</param>
    /// <param name="name">Optional original file name for the download.</param>
    [HttpGet("{**path}")]
    public async Task<IActionResult> Get(string path, [FromQuery] string? download = null, [FromQuery] string? name = null)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && int.TryParse(parts[1], out var groupId) && parts[0] != "logos")
            await me.EnsureGroupAsync(groupId);

        var full = files.Resolve(path);
        if (!System.IO.File.Exists(full)) return NotFound(new { message = "File not found." });
        var type = FileStorage.ContentTypeFor(full);
        var stream = System.IO.File.OpenRead(full);
        var asDownload = download is not null && (download == "1" || download.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (!asDownload) return File(stream, type, enableRangeProcessing: true);
        var fileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full) : Path.GetFileName(name);
        return File(stream, type, fileName);
    }
}
