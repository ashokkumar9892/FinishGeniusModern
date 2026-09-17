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
public class FilesController(FileStorage files, CurrentUser me, Thumbnails thumbnails) : ControllerBase
{
    /// <param name="download">"1"/"true" = send as an attachment.</param>
    /// <param name="name">Optional original file name for the download.</param>
    /// <param name="w">Wanted width in pixels: a picture is sent shrunk to it (grids and lists), the original otherwise.</param>
    [HttpGet("{**path}")]
    public async Task<IActionResult> Get(string path, [FromQuery] string? download = null, [FromQuery] string? name = null, [FromQuery] int? w = null)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3 && int.TryParse(parts[1], out var groupId) && parts[0] != "logos")
            await me.EnsureGroupAsync(groupId);

        // Under Storage:Root, or (old site's files) under Storage:LegacyRoot.
        var full = files.ResolveExisting(path);
        if (full == null) return NotFound(new { message = "File not found." });
        var type = FileStorage.ContentTypeOf(full);
        if (download is null && Thumbnails.NormalizeWidth(w) is int width && thumbnails.PathFor(full, width) is { } small)
        {
            full = small;
            type = "image/jpeg";
        }
        var info = new FileInfo(full);
        // Photo galleries and work instructions show the same pictures on every visit: let the browser keep them
        // (the URL is private to the caller) and ask again only when the file itself changed.
        Response.Headers.CacheControl = "private, max-age=86400";
        var modified = new DateTimeOffset(info.LastWriteTimeUtc);
        var tag = new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{info.LastWriteTimeUtc.Ticks:x}-{info.Length:x}\"");
        var stream = System.IO.File.OpenRead(full);
        var asDownload = download is not null && (download == "1" || download.Equals("true", StringComparison.OrdinalIgnoreCase));
        if (!asDownload) return File(stream, type, modified, tag, enableRangeProcessing: true);
        var fileName = string.IsNullOrWhiteSpace(name) ? Path.GetFileName(full) : Path.GetFileName(name);
        return File(stream, type, fileName, modified, tag, enableRangeProcessing: true);
    }
}
