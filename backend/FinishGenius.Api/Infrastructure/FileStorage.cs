namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Stores uploaded files on disk under <c>Storage:Root</c> (default <c>App_Data/uploads</c> next to the app).
/// On IIS the application pool identity needs Modify permission on that folder.
/// Paths returned are relative ("photos/12/abc.jpg") and served by <c>GET /api/files/{path}</c>.
/// </summary>
public class FileStorage
{
    private readonly string _root;

    public FileStorage(IConfiguration config, IWebHostEnvironment env)
    {
        var configured = config["Storage:Root"];
        _root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(env.ContentRootPath, "App_Data", "uploads")
            : Path.GetFullPath(configured, env.ContentRootPath);
        Directory.CreateDirectory(_root);
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
        var source = Resolve(relative);
        var target = $"{folder}/{Guid.NewGuid():N}{Path.GetExtension(relative)}";
        var full = Resolve(target);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(source))
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
}
