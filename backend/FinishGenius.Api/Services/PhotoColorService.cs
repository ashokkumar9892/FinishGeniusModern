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

/// <summary>Areas the app found by itself, for the operator to accept or drag over.</summary>
public record SuggestedRegions(PhotoRegion? Sample, PhotoRegion? Chart, string Note);

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
    /// The colour inside <paramref name="sample"/>, corrected against a 24-patch ColorChecker where one was marked.
    /// The chart says how the camera and the light bent every colour, not only how bright the light was, so this is
    /// the reading to prefer whenever the chart is in the shot.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public PhotoReading ReadWithChart(string imagePath, PhotoRegion sample, PhotoRegion chart)
    {
        using var bitmap = new Bitmap(imagePath);
        var sampleRgb = Average(bitmap, sample) ?? throw ApiException.Bad("The selected area of the photo is empty — draw the box over the wood.");

        // The chart may be laid down any way round; the orientation that fits the known colours best is the right one.
        Correction? best = null;
        foreach (var rotation in new[] { 0, 90, 180, 270 })
        {
            var measured = ReadPatches(bitmap, chart, rotation);
            if (measured == null) continue;
            var fit = ColorChecker.Fit(measured);
            if (best == null || fit.ResidualDeltaE < best.Fit.ResidualDeltaE) best = new Correction(fit, rotation);
        }
        if (best == null) throw ApiException.Bad("The marked area is too small to read a ColorChecker from — draw the box around the whole chart.");

        var corrected = best.Fit.Apply(sampleRgb);
        var lab = ColorScience.RgbToLab(corrected);
        var note = best.Fit.ResidualDeltaE > 4
            ? $"The chart still reads ΔE {best.Fit.ResidualDeltaE:0.#} out after correcting (it was {best.Fit.OriginalDeltaE:0.#}). " +
              "Check the box is tight around the chart, the light is even, and nothing is glaring off it."
            : $"Corrected against all 24 chart patches: the photo was ΔE {best.Fit.OriginalDeltaE:0.#} out, now {best.Fit.ResidualDeltaE:0.#}.";
        return new PhotoReading(lab, ColorScience.LabToHex(lab), true, best.Fit.OriginalDeltaE, note);
    }

    private sealed record Correction(ColorChecker.Correction Fit, int Rotation);

    /// <summary>The 24 patch colours, read out of the marked chart at the given rotation (null when it is too small).</summary>
    [SupportedOSPlatform("windows")]
    private static List<Rgb>? ReadPatches(Bitmap bitmap, PhotoRegion chart, int rotation)
    {
        var rect = ToRectangle(bitmap, chart);
        var landscape = rotation is 0 or 180;
        int cols = landscape ? ColorChecker.Columns : ColorChecker.Rows;
        int rows = landscape ? ColorChecker.Rows : ColorChecker.Columns;
        if (rect.Width < cols * 8 || rect.Height < rows * 8) return null;

        var cellW = rect.Width / (double)cols;
        var cellH = rect.Height / (double)rows;
        var patches = new Rgb[ColorChecker.Patches.Length];
        for (var row = 0; row < rows; row++)
        for (var col = 0; col < cols; col++)
        {
            // Only the middle of each patch is read, so the printed border and any shadow in the gaps are left out.
            var region = new PhotoRegion(
                (rect.Left + (col + 0.3) * cellW) / bitmap.Width,
                (rect.Top + (row + 0.3) * cellH) / bitmap.Height,
                cellW * 0.4 / bitmap.Width,
                cellH * 0.4 / bitmap.Height);
            var colour = Average(bitmap, region);
            if (colour == null) return null;
            var index = rotation switch
            {
                0 => row * ColorChecker.Columns + col,
                180 => (ColorChecker.Rows - 1 - row) * ColorChecker.Columns + (ColorChecker.Columns - 1 - col),
                90 => (ColorChecker.Rows - 1 - col) * ColorChecker.Columns + row,
                _ => col * ColorChecker.Columns + (ColorChecker.Columns - 1 - row),
            };
            patches[index] = colour.Value;
        }
        return patches.ToList();
    }

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
    /// Where the wood and the chart appear to be. The wood is the largest even, wood-coloured area; the chart is the
    /// block of many strong colours next to it. It is a suggestion to save dragging — the operator sees both boxes and
    /// can move them, and a wrong guess costs nothing because the reading is taken from whatever the boxes end up on.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public SuggestedRegions Suggest(string imagePath)
    {
        const int gridX = 32, gridY = 24;
        using var bitmap = new Bitmap(imagePath);
        var lab = new Lab[gridY, gridX];
        var chroma = new double[gridY, gridX];
        for (var y = 0; y < gridY; y++)
        for (var x = 0; x < gridX; x++)
        {
            var region = new PhotoRegion(x / (double)gridX, y / (double)gridY, 1.0 / gridX, 1.0 / gridY);
            var rgb = Average(bitmap, region) ?? new Rgb(0, 0, 0);
            lab[y, x] = ColorScience.RgbToLab(rgb);
            chroma[y, x] = Math.Sqrt(lab[y, x].A * lab[y, x].A + lab[y, x].B * lab[y, x].B);
        }

        // How much each tile disagrees with its neighbours: even boards sit low, printed patches sit high. The line
        // between them is drawn from this picture rather than from a fixed number, because grain varies so much.
        var spread = new double[gridY, gridX];
        var all = new List<double>(gridX * gridY);
        for (var y = 0; y < gridY; y++)
        for (var x = 0; x < gridX; x++)
        {
            spread[y, x] = NeighbourSpread(lab, x, y, gridX, gridY);
            all.Add(spread[y, x]);
        }
        all.Sort();
        var median = all[all.Count / 2];
        var even = Math.Max(8, median * 1.5);       // still looks like one surface
        var busy = Math.Max(20, median * 3);        // a block of many different printed colours

        // Wood: an even surface with some colour in it.
        var wood = new bool[gridY, gridX];
        for (var y = 0; y < gridY; y++)
        for (var x = 0; x < gridX; x++)
        {
            // Not by hue: the photo may well have a colour cast on it, which is the very thing being corrected.
            // A board is simply the biggest evenly-coloured thing that is neither black, blown out, nor dead grey.
            var l = lab[y, x];
            var woodish = l.L is > 12 and < 95 && chroma[y, x] is > 3 and < 70;
            wood[y, x] = woodish && spread[y, x] <= even;
        }
        var woodBox = LargestRectangle(wood, gridX, gridY);

        // Chart: strong colours that disagree with their neighbours — 24 printed patches in a small block. Its greyscale
        // row is not "busy" at all, so the busy core is grown outwards over anything that is still not plain board.
        var chart = new bool[gridY, gridX];
        for (var y = 0; y < gridY; y++)
        for (var x = 0; x < gridX; x++)
            chart[y, x] = !wood[y, x] && spread[y, x] >= busy;
        var chartBox = Grow(LargestRectangle(chart, gridX, gridY), wood, gridX, gridY);

        // A suggested chart is only offered if reading it actually works: the 24 patches are fitted here and the box is
        // dropped when the fit is poor, so nobody is handed a confident-looking reading taken from the wrong place.
        PhotoRegion? chartRegion = null;
        if (chartBox is { } cb)
        {
            var region = Box(cb, gridX, gridY);
            var patches = ReadPatches(bitmap, region, 0) ?? ReadPatches(bitmap, region, 90);
            if (patches != null && ColorChecker.Fit(patches).ResidualDeltaE <= 6) chartRegion = region;
        }

        var note = woodBox == null
            ? "The wood could not be picked out — drag the boxes yourself."
            : chartRegion == null
                ? "Wood found. No ColorChecker was read: mark it yourself if it is in the shot."
                : "Wood and chart found. Check both boxes before reading.";
        return new SuggestedRegions(
            woodBox is { } w ? Shrink(w, gridX, gridY) : null,
            chartRegion,
            note);
    }

    /// <summary>How much a tile's colour differs from the tiles around it (ΔE00).</summary>
    private static double NeighbourSpread(Lab[,] lab, int x, int y, int gridX, int gridY)
    {
        double worst = 0;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var nx = x + dx;
            var ny = y + dy;
            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= gridX || ny >= gridY) continue;
            worst = Math.Max(worst, ColorScience.DeltaE2000(lab[y, x], lab[ny, nx]));
        }
        return worst;
    }

    /// <summary>The biggest solid block of true tiles (largest rectangle in a histogram, row by row).</summary>
    private static (int X, int Y, int W, int H)? LargestRectangle(bool[,] grid, int gridX, int gridY)
    {
        var heights = new int[gridX];
        (int X, int Y, int W, int H)? best = null;
        var bestArea = 8; // anything smaller is noise, not a board
        for (var y = 0; y < gridY; y++)
        {
            for (var x = 0; x < gridX; x++) heights[x] = grid[y, x] ? heights[x] + 1 : 0;
            for (var left = 0; left < gridX; left++)
            {
                var minHeight = int.MaxValue;
                for (var right = left; right < gridX; right++)
                {
                    minHeight = Math.Min(minHeight, heights[right]);
                    if (minHeight == 0) break;
                    var area = minHeight * (right - left + 1);
                    if (area > bestArea)
                    {
                        bestArea = area;
                        best = (left, y - minHeight + 1, right - left + 1, minHeight);
                    }
                }
            }
        }
        return best;
    }

    /// <summary>
    /// Stretches a box outwards while the next row or column is still not plain board — the chart's grey patches do not
    /// look "busy", so the colourful core alone always stops short of the whole chart.
    /// </summary>
    private static (int X, int Y, int W, int H)? Grow((int X, int Y, int W, int H)? box, bool[,] wood, int gridX, int gridY)
    {
        if (box is not { } b) return null;
        bool FreeColumn(int x) => x >= 0 && x < gridX && Enumerable.Range(b.Y, b.H).All(y => !wood[y, x]);
        bool FreeRow(int y) => y >= 0 && y < gridY && Enumerable.Range(b.X, b.W).All(x => !wood[y, x]);
        for (var i = 0; i < 4 && FreeColumn(b.X - 1); i++) b = b with { X = b.X - 1, W = b.W + 1 };
        for (var i = 0; i < 4 && FreeColumn(b.X + b.W); i++) b = b with { W = b.W + 1 };
        for (var i = 0; i < 4 && FreeRow(b.Y - 1); i++) b = b with { Y = b.Y - 1, H = b.H + 1 };
        for (var i = 0; i < 4 && FreeRow(b.Y + b.H); i++) b = b with { H = b.H + 1 };
        return b;
    }

    /// <summary>Tiles to a fraction-of-the-picture box, exactly as found.</summary>
    private static PhotoRegion Box((int X, int Y, int W, int H) box, int gridX, int gridY) =>
        new(box.X / (double)gridX, box.Y / (double)gridY, box.W / (double)gridX, box.H / (double)gridY);

    /// <summary>Tiles to a fraction-of-the-picture box, pulled in a little so it cannot sit on an edge.</summary>
    private static PhotoRegion Shrink((int X, int Y, int W, int H) box, int gridX, int gridY)
    {
        var inset = 0.15;
        var x = (box.X + box.W * inset) / gridX;
        var y = (box.Y + box.H * inset) / gridY;
        return new PhotoRegion(x, y, box.W * (1 - inset * 2) / gridX, box.H * (1 - inset * 2) / gridY);
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
