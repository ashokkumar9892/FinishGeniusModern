namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Stores uploaded files on disk under <c>Storage:Root</c> (default <c>App_Data/uploads</c> next to the app; point it at a
/// folder outside the site, e.g. <c>D:\FinishGenius\Files</c>, so deployments never touch the files).
/// On IIS the application pool identity needs Modify permission on that folder.
/// Paths returned are relative ("photos/12/abc.jpg") and served by <c>GET /api/files/{path}</c>.
/// Files of the old site can be read in place from its AppData folder (<c>Storage:LegacyRoot</c>, read only).
/// </summary>
public class FileStorage
{
    private readonly string _root;
    private readonly string? _legacyRoot;

    public FileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Storage:Root"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : Path.GetFullPath(configured, env.ContentRootPath);
        Directory.CreateDirectory(_root);
        var legacy = config["Storage:LegacyRoot"];
        _legacyRoot = string.IsNullOrWhiteSpace(legacy) ? null : Path.GetFullPath(legacy, env.ContentRootPath);
    }

    /// <summary>
    /// Absolute path of an existing file, or null. Files are looked up under <c>Storage:Root</c>; the old site's files
    /// ("…/legacy/…" paths from the legacy mapping) are also found in <c>Storage:LegacyRoot</c>, so they need not be copied.
    /// </summary>
    public string? ResolveExisting(string relative)
    {
        var full = Resolve(relative);
        if (File.Exists(full)) return full;
        return LegacyPath(relative) is { } old && File.Exists(old) ? old : null;
    }

    /// <summary>Where the old site keeps a "…/legacy/…" file: AppData/Documents/{group}/, AppData/PhotoGalleryPhotos/{group}/,
    /// AppData/WorkInstructions/{group}/{document}/.</summary>
    private string? LegacyPath(string relative)
    {
        if (_legacyRoot == null) return null;
        string[]? parts = relative.Replace('\\', '/').Split('/') switch
        {
            ["documents", var g, "legacy", .. var rest] => ["Documents", g, .. rest],
            ["photos", var g, "legacy", .. var rest] => ["PhotoGalleryPhotos", g, .. rest],
            ["work-instructions", var g, "legacy", .. var rest] => ["WorkInstructions", g, .. rest],
            _ => null,
        };
        if (parts == null || parts.Any(p => p is "" or "." or "..")) return null;
        var full = Path.GetFullPath(Path.Combine([_legacyRoot, .. parts]));
        return full.StartsWith(_legacyRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

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

    /// <summary>Maps a relative path to an absolute one, refusing anything that escapes the root.</summary>
    public string Resolve(string relative)
    {
        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Bad("Invalid file path.");
        return full;
    }

    public void Delete(string? relative)
    {
        if (string.IsNullOrEmpty(relative)) return;
        try
        {
            var full = Resolve(relative);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (IOException) { /* best effort */ }
    }

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
