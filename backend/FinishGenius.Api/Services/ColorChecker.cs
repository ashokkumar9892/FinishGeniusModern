namespace FinishGenius.Api.Services;

/// <summary>
/// The ColorChecker Classic chart: 24 patches whose colours are known, so a photograph of one says exactly how the
/// camera and the light bent every colour — not just how bright the light was, which is all a single grey card can say.
/// This is the chart the requirements photograph alongside every sample.
///
/// Reference values are the BabelColor averages of many production charts, sRGB under D65 — the same numbers the
/// chart's own users quote, and close enough to any individual chart that the fit error reports the difference.
/// </summary>
public static class ColorChecker
{
    /// <summary>The 24 patches, in the order they are printed: 6 across, 4 down, starting at the brown top-left.</summary>
    public static readonly (string Name, Rgb Srgb)[] Patches =
    [
        ("Dark skin", new Rgb(115, 82, 68)), ("Light skin", new Rgb(194, 150, 130)), ("Blue sky", new Rgb(98, 122, 157)),
        ("Foliage", new Rgb(87, 108, 67)), ("Blue flower", new Rgb(133, 128, 177)), ("Bluish green", new Rgb(103, 189, 170)),
        ("Orange", new Rgb(214, 126, 44)), ("Purplish blue", new Rgb(80, 91, 166)), ("Moderate red", new Rgb(193, 90, 99)),
        ("Purple", new Rgb(94, 60, 108)), ("Yellow green", new Rgb(157, 188, 64)), ("Orange yellow", new Rgb(224, 163, 46)),
        ("Blue", new Rgb(56, 61, 150)), ("Green", new Rgb(70, 148, 73)), ("Red", new Rgb(175, 54, 60)),
        ("Yellow", new Rgb(231, 199, 31)), ("Magenta", new Rgb(187, 86, 149)), ("Cyan", new Rgb(8, 133, 161)),
        ("White", new Rgb(243, 243, 242)), ("Neutral 8", new Rgb(200, 200, 200)), ("Neutral 6.5", new Rgb(160, 160, 160)),
        ("Neutral 5", new Rgb(122, 122, 121)), ("Neutral 3.5", new Rgb(85, 85, 85)), ("Black", new Rgb(52, 52, 52)),
    ];

    public const int Columns = 6;
    public const int Rows = 4;

    /// <summary>
    /// The correction a photograph needs, as a 3×3 matrix in linear light plus an offset per channel. Fitted by least
    /// squares over all 24 patches, which handles a colour cast, a wrong white balance and channel crosstalk together.
    /// </summary>
    public sealed class Correction
    {
        public double[,] Matrix { get; init; } = new double[3, 4];

        /// <summary>The average ΔE00 left between the corrected chart and the real chart — how well the fit worked.</summary>
        public double ResidualDeltaE { get; init; }

        /// <summary>The average ΔE00 the chart was off by before correcting — how much the lighting was lying.</summary>
        public double OriginalDeltaE { get; init; }

        public Rgb Apply(Rgb rgb)
        {
            var (r, g, b) = (Linear(rgb.R), Linear(rgb.G), Linear(rgb.B));
            double Out(int row) => Matrix[row, 0] * r + Matrix[row, 1] * g + Matrix[row, 2] * b + Matrix[row, 3];
            return new Rgb(ToByte(Out(0)), ToByte(Out(1)), ToByte(Out(2)));
        }
    }

    private static double Linear(double channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double ToByte(double linear)
    {
        var c = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(Math.Max(0, linear), 1 / 2.4) - 0.055;
        return Math.Clamp(Math.Round(c * 255), 0, 255);
    }

    /// <summary>
    /// Fits the correction from the 24 patches as the camera saw them (same order as <see cref="Patches"/>).
    /// </summary>
    public static Correction Fit(IReadOnlyList<Rgb> measured)
    {
        if (measured.Count != Patches.Length)
            throw new ArgumentException($"A ColorChecker has {Patches.Length} patches; {measured.Count} were read.", nameof(measured));

        // Solve, for each output channel, measured·[r g b 1] ≈ reference, by normal equations (4×4 — small and stable
        // enough with 24 well-spread samples).
        var matrix = new double[3, 4];
        var ata = new double[4, 4];
        var atb = new double[4, 3];
        for (var i = 0; i < measured.Count; i++)
        {
            double[] x = [Linear(measured[i].R), Linear(measured[i].G), Linear(measured[i].B), 1];
            double[] y = [Linear(Patches[i].Srgb.R), Linear(Patches[i].Srgb.G), Linear(Patches[i].Srgb.B)];
            for (var r = 0; r < 4; r++)
            {
                for (var c = 0; c < 4; c++) ata[r, c] += x[r] * x[c];
                for (var c = 0; c < 3; c++) atb[r, c] += x[r] * y[c];
            }
        }
        for (var channel = 0; channel < 3; channel++)
        {
            var solution = Solve(ata, [atb[0, channel], atb[1, channel], atb[2, channel], atb[3, channel]]);
            for (var c = 0; c < 4; c++) matrix[channel, c] = solution[c];
        }

        var correction = new Correction { Matrix = matrix };
        var before = 0.0;
        var after = 0.0;
        for (var i = 0; i < measured.Count; i++)
        {
            var reference = ColorScience.RgbToLab(Patches[i].Srgb);
            before += ColorScience.DeltaE2000(ColorScience.RgbToLab(measured[i]), reference);
            after += ColorScience.DeltaE2000(ColorScience.RgbToLab(correction.Apply(measured[i])), reference);
        }
        return new Correction
        {
            Matrix = matrix,
            OriginalDeltaE = before / measured.Count,
            ResidualDeltaE = after / measured.Count,
        };
    }

    /// <summary>Gauss-Jordan with partial pivoting, with a small ridge term so a flat photo cannot make it blow up.</summary>
    private static double[] Solve(double[,] a, double[] b)
    {
        const int n = 4;
        var m = new double[n, n + 1];
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++) m[r, c] = a[r, c] + (r == c ? 1e-6 : 0);
            m[r, n] = b[r];
        }
        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-12) continue;
            if (pivot != col)
                for (var c = 0; c <= n; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            var d = m[col, col];
            for (var c = col; c <= n; c++) m[col, c] /= d;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = m[r, col];
                if (f == 0) continue;
                for (var c = col; c <= n; c++) m[r, c] -= f * m[col, c];
            }
        }
        return [m[0, n], m[1, n], m[2, n], m[3, n]];
    }
}
