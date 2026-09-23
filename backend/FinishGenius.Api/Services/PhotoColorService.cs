using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using FinishGenius.Api.Infrastructure;
using ImageEncoder = System.Drawing.Imaging.Encoder;

namespace FinishGenius.Api.Services;

/// <summary>A rectangle picked on a photograph, as fractions of its width and height (0-1).</summary>
public record PhotoRegion(double X, double Y, double Width, double Height);

/// <summary>What a photograph says a colour is, and how much the lighting had to be corrected to say it.</summary>
public record PhotoReading(Lab Lab, string Hex, bool Calibrated, double CorrectionDeltaE, string Note);

/// <summary>
/// Reads colour out of a photograph, and paints a predicted colour onto one.
///
/// A photograph is not a measurement: the camera's white balance, the light in the room and the exposure all move the
/// numbers. So a reading is only called calibrated when a grey or white card was in the same shot and pointed out —
/// then the card, whose colour is known, says how far the light pushed everything, and the same correction is undone
/// on the sample. Without a card the reading is still returned, marked uncalibrated, because it is useful for sorting
/// and previewing but must never be filed as a measurement. A spectrophotometer reading always beats both.
/// </summary>
public class PhotoColorService(ILogger<PhotoColorService> log)
{
    /// <summary>Middle grey and white cards, as their makers specify them (L* under D65).</summary>
    public const double GreyCardL = 50.0;
    public const double WhiteCardL = 96.0;

    /// <summary>
    /// The average colour inside <paramref name="sample"/>. When <paramref name="card"/> is given, the picture is
    /// corrected first so that the card reads as the neutral it is.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public PhotoReading Read(string imagePath, PhotoRegion sample, PhotoRegion? card, double cardL)
    {
        using var bitmap = new Bitmap(imagePath);
        var sampleRgb = Average(bitmap, sample) ?? throw ApiException.Bad("The selected area of the photo is empty — draw the box over the wood.");

        if (card == null)
        {
            var uncalibrated = ColorScience.RgbToLab(sampleRgb);
            return new PhotoReading(uncalibrated, ColorScience.LabToHex(uncalibrated), false, 0,
                "No calibration card was marked, so this reading carries the room's lighting with it. Use it to compare and preview, not as a measurement.");
        }

        var cardRgb = Average(bitmap, card) ?? throw ApiException.Bad("The selected calibration card area of the photo is empty.");
        var cardLab = ColorScience.RgbToLab(cardRgb);
        // The card is neutral by definition: a*=b*=0 at its own lightness. Whatever the camera made of it is the error.
        var shouldBe = ColorScience.LabToRgb(new Lab(cardL, 0, 0));
        var correctionDeltaE = ColorScience.DeltaE2000(cardLab, new Lab(cardL, 0, 0));

        // Von Kries-style: scale each channel in linear light so the card lands on neutral, and apply that to the sample.
        var corrected = new Rgb(
            Scale(sampleRgb.R, cardRgb.R, shouldBe.R),
            Scale(sampleRgb.G, cardRgb.G, shouldBe.G),
            Scale(sampleRgb.B, cardRgb.B, shouldBe.B));
        var lab = ColorScience.RgbToLab(corrected);
        var note = correctionDeltaE > 12
            ? $"The card had to be corrected by ΔE {correctionDeltaE:0.#}, which is a lot: check the lighting is even and nothing is in shadow."
            : $"Corrected against the card (ΔE {correctionDeltaE:0.#}).";
        return new PhotoReading(lab, ColorScience.LabToHex(lab), true, correctionDeltaE, note);
    }

    private static double Scale(double channel, double measuredCard, double expectedCard)
    {
        if (measuredCard <= 0.5) return channel; // a black card tells us nothing; leave the channel alone
        var factor = expectedCard / measuredCard;
        return Math.Clamp(channel * factor, 0, 255);
    }

    /// <summary>
    /// The average colour of a region, ignoring the darkest and lightest tenth so that a shadow, a glare spot or a
    /// knot does not decide the answer.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static Rgb? Average(Bitmap bitmap, PhotoRegion region)
    {
        var rect = ToRectangle(bitmap, region);
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        var pixels = new List<(double Luma, Color Color)>(rect.Width * rect.Height);
        // A big photo does not need every pixel: a grid of at most ~200x200 is plenty and keeps this fast.
        var stepX = Math.Max(1, rect.Width / 200);
        var stepY = Math.Max(1, rect.Height / 200);
        for (var y = rect.Top; y < rect.Bottom; y += stepY)
        for (var x = rect.Left; x < rect.Right; x += stepX)
        {
            var c = bitmap.GetPixel(x, y);
            pixels.Add((0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B, c));
        }
        if (pixels.Count == 0) return null;
        var kept = pixels.OrderBy(p => p.Luma).Skip(pixels.Count / 10).Take(Math.Max(1, pixels.Count - pixels.Count / 5)).ToList();
        return new Rgb(kept.Average(p => p.Color.R), kept.Average(p => p.Color.G), kept.Average(p => p.Color.B));
    }

    [SupportedOSPlatform("windows")]
    private static Rectangle ToRectangle(Bitmap bitmap, PhotoRegion r)
    {
        var x = (int)Math.Round(Math.Clamp(r.X, 0, 1) * bitmap.Width);
        var y = (int)Math.Round(Math.Clamp(r.Y, 0, 1) * bitmap.Height);
        var w = (int)Math.Round(Math.Clamp(r.Width, 0, 1) * bitmap.Width);
        var h = (int)Math.Round(Math.Clamp(r.Height, 0, 1) * bitmap.Height);
        w = Math.Min(w, bitmap.Width - x);
        h = Math.Min(h, bitmap.Height - y);
        return new Rectangle(x, y, Math.Max(0, w), Math.Max(0, h));
    }

    /// <summary>
    /// A photograph of the bare wood, shown as it is expected to look once this colour is on it.
    ///
    /// The grain is what makes wood look like wood, so it is kept: every pixel keeps its own distance from the board's
    /// average, and the whole picture is moved so that the average lands on the predicted colour. The move is made in
    /// OKLab, where a shift of a given size looks the same wherever it starts. It is a picture of a prediction — the
    /// colour comes from measured samples, the gloss, grain-raise and blotching do not.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public byte[] Preview(string imagePath, PhotoRegion woodRegion, Lab predicted, int maxWidth = 1200)
    {
        using var source = new Bitmap(imagePath);
        var woodRgb = Average(source, woodRegion) ?? throw ApiException.Bad("Mark the bare wood on the photo first.");
        var from = ColorScience.RgbToOklab(woodRgb);
        var to = ColorScience.RgbToOklab(ColorScience.LabToRgb(predicted));
        double dL = to.L - from.L, dA = to.A - from.A, dB = to.B - from.B;

        var scale = Math.Min(1.0, maxWidth / (double)source.Width);
        var width = Math.Max(1, (int)(source.Width * scale));
        var height = Math.Max(1, (int)(source.Height * scale));
        using var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, width, height);
        }

        // Straight at the pixel buffer: GetPixel/SetPixel on a photo-sized picture takes seconds.
        var data = result.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var i = row + x * 3; // 24bpp is stored blue, green, red
                    var ok = ColorScience.RgbToOklab(new Rgb(bytes[i + 2], bytes[i + 1], bytes[i]));
                    var shifted = ColorScience.OklabToRgb((ok.L + dL, ok.A + dA, ok.B + dB));
                    bytes[i] = (byte)shifted.B;
                    bytes[i + 1] = (byte)shifted.G;
                    bytes[i + 2] = (byte)shifted.R;
                }
            }
            System.Runtime.InteropServices.Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }

        using var stream = new MemoryStream();
        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var settings = new EncoderParameters(1);
        settings.Param[0] = new EncoderParameter(ImageEncoder.Quality, 85L);
        result.Save(stream, jpeg, settings);
        log.LogDebug("Stain preview rendered {Width}x{Height} towards {Lab}", width, height, predicted);
        return stream.ToArray();
    }
}
