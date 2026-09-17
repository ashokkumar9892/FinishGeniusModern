using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using ImageEncoder = System.Drawing.Imaging.Encoder;

namespace FinishGenius.Api.Infrastructure;

/// <summary>
/// Small copies of stored pictures for grids and lists. A photo out of a camera is several megabytes, and a gallery
/// shows dozens of them at a few hundred pixels wide: sending the originals is what makes that page slow. Each size is
/// made once and kept under <c>_thumbnails</c> in the storage folder; if anything about the picture cannot be read the
/// caller simply serves the original, so a gallery never breaks over a thumbnail.
/// </summary>
public class Thumbnails(FileStorage files, ILogger<Thumbnails> log)
{
    /// <summary>Widths the API will produce (a request is rounded up to one of these, so the cache stays small).</summary>
    private static readonly int[] Sizes = [200, 400, 800, 1200];

    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };

    public static int? NormalizeWidth(int? width) =>
        width is > 0 ? Sizes.FirstOrDefault(s => s >= width, Sizes[^1]) : null;

    /// <summary>The cached thumbnail for a picture, or null when one cannot be made (unsupported file, no GDI, failure).</summary>
    public string? PathFor(string sourceFullPath, int width)
    {
        if (!OperatingSystem.IsWindows()) return null; // GDI+ is only supported on Windows; the original is served instead
        if (!Supported.Contains(Path.GetExtension(sourceFullPath))) return null;
        try
        {
            var source = new FileInfo(sourceFullPath);
            if (!source.Exists || source.Length < 64 * 1024) return null; // already small enough to send as it is
            var key = $"{sourceFullPath.ToLowerInvariant()}|{source.LastWriteTimeUtc.Ticks}|{source.Length}|{width}";
            var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..32];
            var cached = Path.Combine(files.Root, "_thumbnails", width.ToString(), $"{name}.jpg");
            if (File.Exists(cached)) return cached;
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            return Render(sourceFullPath, cached, width) ? cached : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            log.LogWarning(e, "Could not make a {Width}px thumbnail of {File}", width, sourceFullPath);
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool Render(string sourcePath, string targetPath, int width)
    {
        using var source = Image.FromFile(sourcePath);
        if (source.Width <= width) return false; // no point shrinking it
        var height = Math.Max(1, (int)Math.Round(source.Height * (width / (double)source.Width)));
        using var thumb = new Bitmap(width, height);
        using (var g = Graphics.FromImage(thumb))
        {
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, width, height);
        }
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var settings = new EncoderParameters(1);
        settings.Param[0] = new EncoderParameter(ImageEncoder.Quality, 80L);
        // Written next to the final name first, so two requests at once cannot leave half a file behind.
        var temp = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        thumb.Save(temp, jpeg, settings);
        File.Move(temp, targetPath, overwrite: true);
        return true;
    }
}
