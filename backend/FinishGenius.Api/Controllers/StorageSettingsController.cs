using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// System Settings → file storage: where uploads are saved, and the old site's AppData folder (read only).
/// Saved in App_Data/storage-settings.json (see <see cref="FileStorage.SettingsFile"/>) and used at once.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Settings)]
[Route("api/settings/storage")]
public class StorageSettingsController(FileStorage files, CurrentUser me, ILogger<StorageSettingsController> log) : ControllerBase
{
    public record FoldersRequest(string? Root, string? LegacyRoot, bool CopyExisting = false);

    /// <summary>GET /api/settings/storage — the folders in use, with a check of each.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(State());

    /// <summary>POST /api/settings/storage/check { root, legacyRoot } — tests folders without saving them.</summary>
    [HttpPost("check")]
    public IActionResult Check(FoldersRequest r) =>
        Ok(new { root = files.CheckStorageFolder(r.Root), legacyRoot = files.CheckOldSiteFolder(r.LegacyRoot) });

    /// <summary>PUT /api/settings/storage { root, legacyRoot, copyExisting } — saves and applies the folders.</summary>
    [HttpPut]
    public IActionResult Save(FoldersRequest r)
    {
        var root = files.CheckStorageFolder(r.Root);
        if (!root.Ok) throw ApiException.Bad(root.Message);
        var legacy = files.CheckOldSiteFolder(r.LegacyRoot);
        if (!legacy.Ok) throw ApiException.Bad(legacy.Message);

        var before = files.Root;
        try
        {
            files.SaveSettings(r.Root, r.LegacyRoot);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw ApiException.Bad($"The settings could not be saved to {files.SettingsPath}: {e.Message}");
        }
        log.LogWarning("Storage folders changed by {User}: storage {Before} -> {After}; old site folder {Legacy}",
            me.UserName, before, files.Root, files.LegacyRoot ?? "(none)");

        var copying = r.CopyExisting && files.PreviousRoots.Count > 0 && files.StartCopy();
        return Ok(new
        {
            message = copying ? "Storage folders saved. Copying the existing files to the new folder…" : "Storage folders saved.",
            state = State(),
        });
    }

    /// <summary>POST /api/settings/storage/copy — copies the files of earlier folders into the storage folder (background).</summary>
    [HttpPost("copy")]
    public IActionResult StartCopy()
    {
        if (files.PreviousRoots.Count == 0) throw ApiException.Bad("There is no earlier folder to copy files from.");
        if (!files.StartCopy()) throw ApiException.Bad("A copy is already running.");
        return Ok(new { message = "Copying files to the storage folder…" });
    }

    /// <summary>GET /api/settings/storage/copy — progress of the last copy.</summary>
    [HttpGet("copy")]
    public IActionResult CopyProgress() => Ok(files.Copy);

    private object State() => new
    {
        root = files.Root,
        isDefault = string.Equals(Path.TrimEndingDirectorySeparator(files.Root), Path.TrimEndingDirectorySeparator(files.DefaultRoot), StringComparison.OrdinalIgnoreCase),
        defaultRoot = files.DefaultRoot,
        legacyRoot = files.LegacyRoot,
        previousRoots = files.PreviousRoots,
        settingsFile = files.SettingsPath,
        rootCheck = files.CheckStorageFolder(files.Root),
        legacyCheck = files.CheckOldSiteFolder(files.LegacyRoot),
        copy = files.Copy,
    };
}
