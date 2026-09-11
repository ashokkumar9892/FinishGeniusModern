using System.Text.Json;
using Microsoft.Extensions.Primitives;

namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Stores uploaded files (documents, photos, work-instruction media, logos) on disk under the storage folder
/// (<c>Storage:Root</c>; default <c>App_Data/uploads</c> next to the app). A System Administrator sets it on the System
/// Settings page — ideally a folder outside the site such as <c>D:\FinishGeniusData</c>, so deployments never touch the
/// files. On IIS the application pool identity needs Modify permission on that folder.
/// Paths returned are relative ("photos/12/abc.jpg") and served by <c>GET /api/files/{path}</c>.
/// After the folder changes, files are still found in the previous folders until they are copied over, and files of the
/// old site can be read in place from its AppData folder (<c>Storage:LegacyRoot</c>, read only).
/// </summary>
public class FileStorage
{
    /// <summary>
    /// Folders chosen on the System Settings page. Loaded as configuration after appsettings*.json (so it wins) and kept by
    /// deploy/Install-IIS.ps1 together with the rest of App_Data.
    /// </summary>
    public const string SettingsFile = "App_Data/storage-settings.json";

    private sealed record Folders(string Root, string? LegacyRoot, string[] PreviousRoots);

    private readonly IConfiguration _config;
    private readonly ILogger<FileStorage> _log;
    private readonly string _contentRoot;
    private volatile Folders _folders;

    public FileStorage(IConfiguration config, IWebHostEnvironment env, ILogger<FileStorage> log)
    {
        _config = config;
        _log = log;
        _contentRoot = env.ContentRootPath;
        DefaultRoot = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
        _folders = Load();
        ChangeToken.OnChange(config.GetReloadToken, () => _folders = Load());
    }

    /// <summary>Used when no folder is set.</summary>
    public string DefaultRoot { get; }
    /// <summary>Where new files are saved.</summary>
    public string Root => _folders.Root;
    /// <summary>The old site's AppData folder (read only), or null.</summary>
    public string? LegacyRoot => _folders.LegacyRoot;
    /// <summary>Earlier storage folders whose files have not been copied over yet (read, never written).</summary>
    public IReadOnlyList<string> PreviousRoots => _folders.PreviousRoots;
    public string SettingsPath => Path.Combine(_contentRoot, SettingsFile);

    private Folders Load()
    {
        var root = Full(_config["Storage:Root"]) ?? DefaultRoot;
        try { Directory.CreateDirectory(root); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log.LogError(e, "Storage folder {Root} cannot be created", root); }
        var previous = _config.GetSection("Storage:PreviousRoots").Get<string[]>() ?? [];
        return new Folders(root, Full(_config["Storage:LegacyRoot"]),
            previous.Select(Full).OfType<string>().Where(p => !SamePath(p, root)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private string? Full(string? path) => string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path.Trim(), _contentRoot);

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string folder) =>
        SamePath(path, folder) || path.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp"];
    public static readonly string[] MediaExtensions = [".jpg", ".jpeg", ".png", ".gif", ".mp4", ".mov"];

    public async Task<string> SaveAsync(IFormFile file, string folder, string[]? allowedExtensions = null)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (allowedExtensions != null && !allowedExtensions.Contains(ext))
        {
            var list = string.Join(", ", allowedExtensions.Select(e => e.TrimStart('.')).Where(e => e != "jpeg"));
            throw ApiException.Bad($"Invalid extension for file \"{file.FileName}\". Only \"{list}\" files are supported.");
        }
        var safeFolder = string.Join('/', folder.Split('/', '\\').Where(p => p is not ("" or "." or "..")));
        var relative = $"{safeFolder}/{Guid.NewGuid():N}{ext}";
        var full = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await file.CopyToAsync(fs);
        return relative;
    }

    public async Task<string> CopyAsync(string relative, string folder)
    {
        var source = ResolveExisting(relative);
        var target = $"{folder}/{Guid.NewGuid():N}{Path.GetExtension(relative)}";
        var full = Resolve(target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (source != null)
        {
            await using var src = File.OpenRead(source);
            await using var dst = File.Create(full);
            await src.CopyToAsync(dst);
        }
        return target;
    }

    /// <summary>Maps a relative path into the storage folder, refusing anything that escapes it.</summary>
    public string Resolve(string relative) => Under(Root, relative) ?? throw ApiException.Bad("Invalid file path.");

    private static string? Under(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return IsUnder(full, root) ? full : null;
    }

    /// <summary>
    /// Absolute path of an existing file, or null: the storage folder first, then earlier storage folders, then — for the
    /// old site's files ("…/legacy/…" paths from the legacy mapping) — the old site's AppData folder, read in place.
    /// </summary>
    public string? ResolveExisting(string relative)
    {
        var folders = _folders;
        var full = Resolve(relative);
        if (File.Exists(full)) return full;
        foreach (var previous in folders.PreviousRoots)
            if (Under(previous, relative) is { } old && File.Exists(old)) return old;
        return LegacyPath(folders.LegacyRoot, relative) is { } legacy && File.Exists(legacy) ? legacy : null;
    }

    /// <summary>Where the old site keeps a "…/legacy/…" file: AppData/Documents/{group}/, AppData/PhotoGalleryPhotos/{group}/,
    /// AppData/WorkInstructions/{group}/{document}/.</summary>
    private static string? LegacyPath(string? legacyRoot, string relative)
    {
        if (legacyRoot == null) return null;
        string[]? parts = relative.Replace('\\', '/').Split('/') switch
        {
            ["documents", var g, "legacy", .. var rest] => ["Documents", g, .. rest],
            ["photos", var g, "legacy", .. var rest] => ["PhotoGalleryPhotos", g, .. rest],
            ["work-instructions", var g, "legacy", .. var rest] => ["WorkInstructions", g, .. rest],
            _ => null,
        };
        if (parts == null || parts.Any(p => p is "" or "." or "..")) return null;
        var full = Path.GetFullPath(Path.Combine([legacyRoot, .. parts]));
        return IsUnder(full, legacyRoot) ? full : null;
    }

    /// <summary>Deletes a stored file (current or earlier storage folder; never the old site's folder).</summary>
    public void Delete(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;
        var folders = _folders;
        foreach (var root in folders.PreviousRoots.Prepend(folders.Root))
        {
            try
            {
                if (Under(root, relative) is { } full && File.Exists(full)) File.Delete(full);
            }
            catch (IOException) { /* best effort */ }
            catch (UnauthorizedAccessException) { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------ System Settings page

    public sealed record FolderCheck(bool Ok, string Path, string Message, string? Warning = null, double? FreeGb = null);

    /// <summary>Can the app create and write files in this folder? Empty = the default folder.</summary>
    public FolderCheck CheckStorageFolder(string? input)
    {
        var raw = input?.Trim();
        if (!string.IsNullOrEmpty(raw) && !Path.IsPathFullyQualified(raw))
            return new(false, raw, @"Enter a full folder path, e.g. D:\FinishGeniusData or \\fileserver\share\FinishGenius.");
        var full = Full(raw) ?? DefaultRoot;
        try
        {
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new(false, full, $"The app cannot write to this folder: {e.Message} On IIS give the application pool identity Modify permission on it.");
        }
        var warning = IsUnder(full, _contentRoot)
            ? @"This folder is inside the site folder: redeploying or moving the site can lose the files. A folder outside the site (e.g. D:\FinishGeniusData) is safer."
            : null;
        return new(true, full, "The app can create and write files here.", warning, FreeGb(full));
    }

    /// <summary>Can the app read the old site's AppData folder? Empty = not used.</summary>
    public FolderCheck CheckOldSiteFolder(string? input)
    {
        var raw = input?.Trim();
        if (string.IsNullOrEmpty(raw))
            return new(true, "", "Not used: old documents, photos and work-instruction pictures are only shown when copied into the storage folder.");
        if (!Path.IsPathFullyQualified(raw))
            return new(false, raw, @"Enter a full folder path, e.g. \\oldserver\FinishGenius\AppData or D:\OldFinishGenius\AppData.");
        var full = Full(raw)!;
        try
        {
            if (!Directory.Exists(full)) return new(false, full, "Folder not found, or the app is not allowed to see it.");
            _ = Directory.EnumerateFileSystemEntries(full).FirstOrDefault();
            var found = new[] { "Documents", "PhotoGalleryPhotos", "WorkInstructions" }.Where(d => Directory.Exists(Path.Combine(full, d))).ToList();
            return found.Count == 0
                ? new(true, full, "The app can read this folder.", "It has none of the old site's folders (Documents, PhotoGalleryPhotos, WorkInstructions) — check that it is the old site's AppData folder.")
                : new(true, full, $"The app can read this folder; found {string.Join(", ", found)}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new(false, full, $"The app cannot read this folder: {e.Message}");
        }
    }

    private static double? FreeGb(string path)
    {
        try
        {
            var drive = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(drive) || drive.StartsWith(@"\\")) return null;
            return Math.Round(new DriveInfo(drive).AvailableFreeSpace / 1024d / 1024 / 1024, 1);
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Saves the folders chosen on the System Settings page and uses them at once. The folder being replaced stays in
    /// PreviousRoots, so its files keep opening until they are copied over.
    /// </summary>
    public void SaveSettings(string? root, string? legacyRoot)
    {
        var current = _folders;
        var newRoot = Full(root) ?? DefaultRoot;
        var previous = current.PreviousRoots.ToList();
        if (!SamePath(current.Root, newRoot) && !previous.Any(p => SamePath(p, current.Root))) previous.Add(current.Root);
        previous.RemoveAll(p => SamePath(p, newRoot));
        var folders = new Folders(newRoot, Full(legacyRoot), [.. previous]);
        WriteSettings(folders);
        Directory.CreateDirectory(newRoot);
        _folders = folders;
    }

    /// <summary>Writes the folders in use (not IConfiguration: it reloads the file a moment after each write).</summary>
    private void WriteSettings(Folders folders)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var storage = new
        {
            Root = SamePath(folders.Root, DefaultRoot) ? "" : folders.Root,
            LegacyRoot = folders.LegacyRoot ?? "",
            folders.PreviousRoots,
        };
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new { Storage = storage }, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ------------------------------------------------------------------ copying files to a new folder

    public sealed class CopyProgress
    {
        public bool Running { get; set; }
        public string? To { get; set; }
        public List<string> From { get; set; } = [];
        public int Total { get; set; }
        public int Copied { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public string? LastError { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public CopyProgress Snapshot() => (CopyProgress)MemberwiseClone();
    }

    private readonly object _copyLock = new();
    private CopyProgress _copy = new();

    public CopyProgress Copy
    {
        get { lock (_copyLock) return _copy.Snapshot(); }
    }

    /// <summary>
    /// Copies every file of the earlier folders into the storage folder in the background (files already there with the
    /// same size are skipped; nothing is deleted). Folders copied without errors are dropped from PreviousRoots.
    /// Returns false when a copy is already running.
    /// </summary>
    public bool StartCopy()
    {
        var target = Root;
        var sources = PreviousRoots.Where(Directory.Exists).ToList();
        lock (_copyLock)
        {
            if (_copy.Running) return false;
            _copy = new CopyProgress { Running = true, To = target, From = sources, StartedAt = DateTime.UtcNow };
        }
        _ = Task.Run(() =>
        {
            var done = new List<string>();
            try
            {
                foreach (var source in sources)
                {
                    var failedBefore = _copy.Failed;
                    // Listed up front, and never from inside the target, so a folder nested in another is not copied into itself.
                    var list = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(f => !IsUnder(f, target)).ToList();
                    lock (_copyLock) _copy.Total += list.Count;
                    foreach (var file in list)
                    {
                        try
                        {
                            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
                            if (File.Exists(dest) && new FileInfo(dest).Length == new FileInfo(file).Length)
                            {
                                lock (_copyLock) _copy.Skipped++;
                                continue;
                            }
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            File.Copy(file, dest, overwrite: true);
                            lock (_copyLock) _copy.Copied++;
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                            lock (_copyLock)
                            {
                                _copy.Failed++;
                                _copy.LastError = $"{file}: {e.Message}";
                            }
                        }
                    }
                    if (_copy.Failed == failedBefore) done.Add(source);
                }
                if (done.Count > 0)
                {
                    var current = _folders;
                    var updated = current with { PreviousRoots = current.PreviousRoots.Where(p => !done.Any(d => SamePath(d, p))).ToArray() };
                    WriteSettings(updated);
                    _folders = updated;
                }
            }
            catch (Exception e)
            {
                _log.LogError(e, "Copying stored files to {Target} failed", target);
                lock (_copyLock) _copy.LastError = e.Message;
            }
            finally
            {
                lock (_copyLock)
                {
                    _copy.Running = false;
                    _copy.FinishedAt = DateTime.UtcNow;
                }
                _log.LogInformation("Copied stored files to {Target}: {Copied} copied, {Skipped} already there, {Failed} failed", target, _copy.Copied, _copy.Skipped, _copy.Failed);
            }
        });
        return true;
    }

    // ------------------------------------------------------------------ content types

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".mov" => "video/quicktime",
        ".pdf" => "application/pdf",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xls" => "application/vnd.ms-excel",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".doc" => "application/msword",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        _ => "application/octet-stream",
    };

    /// <summary>Content type by extension, else by the first bytes (old work-instruction attachments have no extension).</summary>
    public static string ContentTypeOf(string fullPath)
    {
        var byExtension = ContentTypeFor(fullPath);
        if (byExtension != "application/octet-stream") return byExtension;
        var head = new byte[12];
        int read;
        using (var f = File.OpenRead(fullPath)) read = f.Read(head, 0, head.Length);
        bool Starts(params byte[] sig) => read >= sig.Length && head.AsSpan(0, sig.Length).SequenceEqual(sig);
        if (Starts(0xFF, 0xD8, 0xFF)) return "image/jpeg";
        if (Starts(0x89, 0x50, 0x4E, 0x47)) return "image/png";
        if (Starts(0x47, 0x49, 0x46, 0x38)) return "image/gif";
        if (Starts(0x42, 0x4D)) return "image/bmp";
        if (Starts(0x25, 0x50, 0x44, 0x46)) return "application/pdf";
        if (read >= 12 && Starts(0x52, 0x49, 0x46, 0x46) && head.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (read >= 8 && head.AsSpan(4, 4).SequenceEqual("ftyp"u8)) return head.AsSpan(8, 2).SequenceEqual("qt"u8) ? "video/quicktime" : "video/mp4";
        return byExtension;
    }
}
