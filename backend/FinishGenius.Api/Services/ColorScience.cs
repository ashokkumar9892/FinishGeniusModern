namespace FinishGenius.Api.Services;

/// <summary>A colour as measured: CIE L*a*b* under D65 / 2° (what a spectrophotometer reports and what stain work uses).</summary>
public readonly record struct Lab(double L, double A, double B)
{
    public override string ToString() => $"L* {L:0.##} a* {A:0.##} b* {B:0.##}";
}

/// <summary>A colour as a picture holds it: sRGB, 0-255 per channel.</summary>
public readonly record struct Rgb(double R, double G, double B);

/// <summary>
/// Colour arithmetic for stain matching: camera/screen colours (sRGB) converted to the measurement spaces the trade
/// works in, and the colour-difference numbers finishers judge a match by.
///
/// ΔE2000 is the one the app shows: it corrects CIE76's well-known habit of exaggerating differences in saturated
/// colours, which on wood tones (browns, reds, yellows) is exactly where stain work lives. OKLab is here because it
/// behaves predictably when colours are blended or shifted, which is what the virtual preview does to every pixel.
/// </summary>
public static class ColorScience
{
    // D65 / 2° white point, the reference for sRGB and for spectrophotometer readings quoted as "D65 10°/2°".
    private const double Xn = 95.047, Yn = 100.000, Zn = 108.883;

    // ---------------------------------------------------------------- sRGB <-> Lab

    private static double ToLinear(double channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static double ToSrgb(double linear)
    {
        var c = linear <= 0.0031308 ? linear * 12.92 : 1.055 * Math.Pow(linear, 1 / 2.4) - 0.055;
        return Math.Clamp(Math.Round(c * 255), 0, 255);
    }

    public static Lab RgbToLab(Rgb rgb)
    {
        double r = ToLinear(rgb.R), g = ToLinear(rgb.G), b = ToLinear(rgb.B);
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) * 100;
        var y = (0.2126729 * r + 0.7151522 * g + 0.0721750 * b) * 100;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) * 100;

        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        double fx = F(x / Xn), fy = F(y / Yn), fz = F(z / Zn);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    public static Rgb LabToRgb(Lab lab)
    {
        var fy = (lab.L + 16) / 116;
        var fx = fy + lab.A / 500;
        var fz = fy - lab.B / 200;

        static double Inv(double f) => f * f * f > 216.0 / 24389 ? f * f * f : (116 * f - 16) * 27 / 24389;
        double x = Inv(fx) * Xn / 100, y = Inv(fy) * Yn / 100, z = Inv(fz) * Zn / 100;

        var r = 3.2404542 * x - 1.5371385 * y - 0.4985314 * z;
        var g = -0.9692660 * x + 1.8760108 * y + 0.0415560 * z;
        var b = 0.0556434 * x - 0.2040259 * y + 1.0572252 * z;
        return new Rgb(ToSrgb(r), ToSrgb(g), ToSrgb(b));
    }

    /// <summary>"#8a5a32" for a measured colour — the swatch shown next to every reading.</summary>
    public static string LabToHex(Lab lab)
    {
        var rgb = LabToRgb(lab);
        return $"#{(int)rgb.R:x2}{(int)rgb.G:x2}{(int)rgb.B:x2}";
    }

    // ---------------------------------------------------------------- OKLab

    /// <summary>
    /// OKLab: a space where equal steps look equal, so shifting or mixing colours in it keeps them believable.
    /// Used by the virtual preview, not for reporting measurements.
    /// </summary>
    public static (double L, double A, double B) RgbToOklab(Rgb rgb)
    {
        double r = ToLinear(rgb.R), g = ToLinear(rgb.G), b = ToLinear(rgb.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public static Rgb OklabToRgb((double L, double A, double B) ok)
    {
        var l = ok.L + 0.3963377774 * ok.A + 0.2158037573 * ok.B;
        var m = ok.L - 0.1055613458 * ok.A - 0.0638541728 * ok.B;
        var s = ok.L - 0.0894841775 * ok.A - 1.2914855480 * ok.B;
        l *= l * l; m *= m * m; s *= s * s;
        return new Rgb(
            ToSrgb(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
            ToSrgb(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
            ToSrgb(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    // ---------------------------------------------------------------- colour difference

    /// <summary>ΔE*ab (CIE76) — the plain distance between two readings.</summary>
    public static double DeltaE76(Lab a, Lab b) =>
        Math.Sqrt(Sq(a.L - b.L) + Sq(a.A - b.A) + Sq(a.B - b.B));

    /// <summary>
    /// ΔE00 (CIEDE2000) — the difference as an eye judges it, and what the match grades are read against.
    /// Implemented from Sharma, Wu &amp; Dalal (2005), including the hue-rotation term for blues.
    /// </summary>
    public static double DeltaE2000(Lab s, Lab t, double kL = 1, double kC = 1, double kH = 1)
    {
        var lBar = (s.L + t.L) / 2;
        var c1 = Math.Sqrt(Sq(s.A) + Sq(s.B));
        var c2 = Math.Sqrt(Sq(t.A) + Sq(t.B));
        var cBar = (c1 + c2) / 2;

        var g = 0.5 * (1 - Math.Sqrt(Math.Pow(cBar, 7) / (Math.Pow(cBar, 7) + Math.Pow(25, 7))));
        var a1 = (1 + g) * s.A;
        var a2 = (1 + g) * t.A;
        var c1p = Math.Sqrt(Sq(a1) + Sq(s.B));
        var c2p = Math.Sqrt(Sq(a2) + Sq(t.B));
        var cBarP = (c1p + c2p) / 2;

        var h1p = Hue(s.B, a1);
        var h2p = Hue(t.B, a2);

        var dLp = t.L - s.L;
        var dCp = c2p - c1p;
        double dhp;
        if (c1p * c2p == 0) dhp = 0;
        else if (Math.Abs(h2p - h1p) <= 180) dhp = h2p - h1p;
        else if (h2p - h1p > 180) dhp = h2p - h1p - 360;
        else dhp = h2p - h1p + 360;
        var dHp = 2 * Math.Sqrt(c1p * c2p) * Math.Sin(Rad(dhp) / 2);

        double hBarP;
        if (c1p * c2p == 0) hBarP = h1p + h2p;
        else if (Math.Abs(h1p - h2p) <= 180) hBarP = (h1p + h2p) / 2;
        else if (h1p + h2p < 360) hBarP = (h1p + h2p + 360) / 2;
        else hBarP = (h1p + h2p - 360) / 2;

        var t2 = 1 - 0.17 * Math.Cos(Rad(hBarP - 30)) + 0.24 * Math.Cos(Rad(2 * hBarP))
                   + 0.32 * Math.Cos(Rad(3 * hBarP + 6)) - 0.20 * Math.Cos(Rad(4 * hBarP - 63));
        var sL = 1 + 0.015 * Sq(lBar - 50) / Math.Sqrt(20 + Sq(lBar - 50));
        var sC = 1 + 0.045 * cBarP;
        var sH = 1 + 0.015 * cBarP * t2;
        var dTheta = 30 * Math.Exp(-Sq((hBarP - 275) / 25));
        var rC = 2 * Math.Sqrt(Math.Pow(cBarP, 7) / (Math.Pow(cBarP, 7) + Math.Pow(25, 7)));
        var rT = -rC * Math.Sin(Rad(2 * dTheta));

        return Math.Sqrt(Sq(dLp / (kL * sL)) + Sq(dCp / (kC * sC)) + Sq(dHp / (kH * sH))
                         + rT * (dCp / (kC * sC)) * (dHp / (kH * sH)));
    }

    /// <summary>How a finisher reads a ΔE00 number (the grades the formula screens already use).</summary>
    public static string MatchGrade(double deltaE) => deltaE switch
    {
        <= 0.5 => "Not visible",
        <= 1 => "Excellent match",
        <= 2 => "Commercial match",
        <= 3.5 => "Noticeable",
        _ => "Different colour",
    };

    private static double Sq(double v) => v * v;
    private static double Rad(double degrees) => degrees * Math.PI / 180;

    private static double Hue(double b, double aPrime)
    {
        if (b == 0 && aPrime == 0) return 0;
        var h = Math.Atan2(b, aPrime) * 180 / Math.PI;
        return h >= 0 ? h : h + 360;
    }
}
