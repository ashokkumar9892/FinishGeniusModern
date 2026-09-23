using FinishGenius.Api.Domain;

namespace FinishGenius.Api.Services;

/// <summary>A recorded sample scored against a target colour.</summary>
public record ColorCandidate(
    ColorSample Sample,
    double DeltaE,
    /// <summary>The colour this recommendation expects to land on: the sample's own, or an interpolated one.</summary>
    Lab Predicted,
    /// <summary>Strength to use, when samples at different strengths let it be worked out. Null = as recorded.</summary>
    double? SuggestedConcentration,
    /// <summary>0-1. How much the recommendation rests on: how many samples, how consistent, how well conditions match.</summary>
    double Confidence,
    /// <summary>Plain sentence saying what the number is based on, so nobody mistakes one sample for a proven result.</summary>
    string Basis);

/// <summary>What a prediction was worked out from.</summary>
public record ColorPrediction(Lab Predicted, double Confidence, string Basis, int SamplesUsed);

/// <summary>
/// Finds the formula that lands closest to a wanted colour, and predicts where a formula will land — both worked out
/// from the samples that have actually been measured, never from an assumed model of how stain behaves.
///
/// It does three things, in order of how much data they need:
///  1. Nearest recorded colour (needs one sample): rank samples by ΔE00 against the target.
///  2. Strength interpolation (needs two samples of one formula at different strengths): read off the strength whose
///     colour lands nearest the target, within the range actually measured — never extrapolated past it.
///  3. Wood correction (needs the unfinished wood measured too): a sample records how much the stain moved the wood,
///     so on a different board the same shift is applied to that board's own colour.
/// Everything reports how many samples it used and how far apart they were, because that is what says whether the
/// answer can be trusted yet.
/// </summary>
public class ColorMatchService
{
    /// <summary>Samples this far apart in ΔE are treated as disagreeing about the same conditions.</summary>
    private const double AgreementTolerance = 2.0;

    /// <summary>Ranks recorded samples against a wanted colour. `woodLab` = the board being matched, when measured.</summary>
    public List<ColorCandidate> Match(Lab target, IReadOnlyList<ColorSample> samples, Lab? woodLab, int take = 10)
    {
        var byFormula = samples
            .Where(s => s.FormulaId != null || !string.IsNullOrWhiteSpace(s.FormulaName))
            .GroupBy(s => s.FormulaId?.ToString() ?? s.FormulaName.Trim().ToLowerInvariant());

        var candidates = new List<ColorCandidate>();
        foreach (var group in byFormula)
        {
            var list = group.OrderBy(s => s.Concentration ?? 0).ToList();
            var best = list.Select(s => (Sample: s, Lab: Expected(s, woodLab)))
                .Select(x => (x.Sample, x.Lab, DeltaE: ColorScience.DeltaE2000(x.Lab, target)))
                .OrderBy(x => x.DeltaE).First();

            var (predicted, strength, interpolated) = Interpolate(list, target, woodLab, best.Sample, best.Lab);
            var deltaE = ColorScience.DeltaE2000(predicted, target);
            var (confidence, basis) = Score(list, best.Sample, deltaE, interpolated, woodLab);
            candidates.Add(new ColorCandidate(best.Sample, deltaE, predicted, strength, confidence, basis));
        }
        return candidates.OrderBy(c => c.DeltaE).Take(take).ToList();
    }

    /// <summary>
    /// Where a formula is expected to land on a particular board: the colour as recorded, or — when both the sample's
    /// wood and this board were measured — the sample's own shift applied to this board.
    /// </summary>
    public static Lab Expected(ColorSample sample, Lab? woodLab)
    {
        var final = new Lab(sample.FinalL, sample.FinalA, sample.FinalB);
        if (woodLab is not { } wood || sample.WoodL == null || sample.WoodA == null || sample.WoodB == null) return final;
        return new Lab(
            wood.L + (final.L - sample.WoodL.Value),
            wood.A + (final.A - sample.WoodA.Value),
            wood.B + (final.B - sample.WoodB.Value));
    }

    /// <summary>
    /// Reads a strength off two samples of the same formula that sit either side of the target. Only between measured
    /// points: a strength nobody has ever mixed is not a prediction, it is a guess.
    /// </summary>
    private static (Lab Predicted, double? Concentration, bool Interpolated) Interpolate(
        List<ColorSample> list, Lab target, Lab? woodLab, ColorSample best, Lab bestLab)
    {
        var points = list.Where(s => s.Concentration is > 0)
            .Select(s => (Concentration: s.Concentration!.Value, Lab: Expected(s, woodLab)))
            .OrderBy(p => p.Concentration).ToList();
        if (points.Count < 2) return (bestLab, best.Concentration, false);

        var bestResult = (Lab: bestLab, Concentration: (double?)best.Concentration, DeltaE: ColorScience.DeltaE2000(bestLab, target), Found: false);
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var (c1, lab1) = points[i];
            var (c2, lab2) = points[i + 1];
            if (c2 <= c1) continue;
            // Walk the straight line between the two measured colours and keep the closest point to the target.
            for (var step = 0; step <= 20; step++)
            {
                var t = step / 20.0;
                var lab = new Lab(lab1.L + (lab2.L - lab1.L) * t, lab1.A + (lab2.A - lab1.A) * t, lab1.B + (lab2.B - lab1.B) * t);
                var de = ColorScience.DeltaE2000(lab, target);
                if (de < bestResult.DeltaE - 0.0001)
                    bestResult = (lab, Math.Round(c1 + (c2 - c1) * t, 2), de, true);
            }
        }
        return (bestResult.Lab, bestResult.Concentration, bestResult.Found);
    }

    /// <summary>How much weight the answer deserves, and the sentence that explains it.</summary>
    private static (double Confidence, string Basis) Score(
        List<ColorSample> list, ColorSample best, double deltaE, bool interpolated, Lab? woodLab)
    {
        var measured = list.Count;
        var device = list.Count(s => s.Source == ColorSource.Device);
        var strengths = list.Where(s => s.Concentration is > 0).Select(s => s.Concentration!.Value).Distinct().Count();

        // More samples, read on a device, that agree with each other, on a board whose own colour is known.
        var evidence = Math.Min(1, measured / 5.0) * 0.4
                       + (device > 0 ? 0.2 * Math.Min(1, device / 3.0) : 0)
                       + (strengths >= 2 ? 0.15 : 0)
                       + (woodLab != null && best.WoodL != null ? 0.15 : 0)
                       + Agreement(list) * 0.1;
        // A recommendation that is already far from the target cannot be a confident one.
        var closeness = deltaE <= 1 ? 1 : deltaE <= 2 ? 0.8 : deltaE <= 3.5 ? 0.55 : 0.3;
        var confidence = Math.Clamp(evidence * closeness, 0.05, 0.95);

        var parts = new List<string>
        {
            measured == 1 ? "1 recorded sample" : $"{measured} recorded samples",
        };
        if (device > 0) parts.Add($"{device} read on a spectrophotometer");
        if (interpolated) parts.Add("strength read between two measured points");
        else if (strengths >= 2) parts.Add($"{strengths} strengths measured");
        if (woodLab != null && best.WoodL != null) parts.Add("corrected for this board's own colour");
        else if (woodLab != null) parts.Add("this board's colour known, but the sample's unfinished wood was not measured");
        return (confidence, string.Join("; ", parts) + ".");
    }

    /// <summary>1 when samples of a formula agree with each other, falling to 0 as they scatter.</summary>
    private static double Agreement(List<ColorSample> list)
    {
        if (list.Count < 2) return 0.5;
        var labs = list.Select(s => new Lab(s.FinalL, s.FinalA, s.FinalB)).ToList();
        var mean = new Lab(labs.Average(l => l.L), labs.Average(l => l.A), labs.Average(l => l.B));
        var spread = labs.Average(l => ColorScience.DeltaE2000(l, mean));
        return Math.Clamp(1 - spread / AgreementTolerance, 0, 1);
    }

    /// <summary>
    /// Where a formula is expected to land, on a board of this species at this strength. Used by the preview and by
    /// the "what will this do" answer, and it says how little it may be standing on.
    /// </summary>
    public ColorPrediction Predict(IReadOnlyList<ColorSample> samples, int? formulaId, string? formulaName,
        double? concentration, string? species, Lab? woodLab)
    {
        var forFormula = samples.Where(s => formulaId != null
            ? s.FormulaId == formulaId
            : !string.IsNullOrWhiteSpace(formulaName) && s.FormulaName.Equals(formulaName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (forFormula.Count == 0)
            return new ColorPrediction(default, 0, "No sample has ever been recorded for this formula, so there is nothing to predict from.", 0);

        // Samples of the same species describe this board far better than samples of another wood.
        var sameSpecies = string.IsNullOrWhiteSpace(species)
            ? forFormula
            : forFormula.Where(s => s.WoodSpecies.Equals(species, StringComparison.OrdinalIgnoreCase)).ToList();
        var used = sameSpecies.Count > 0 ? sameSpecies : forFormula;
        var speciesNote = string.IsNullOrWhiteSpace(species)
            ? "on every wood recorded for it"
            : sameSpecies.Count > 0 ? $"on {species}" : $"on other woods — nothing has been recorded on {species}";

        var withStrength = used.Where(s => s.Concentration is > 0).OrderBy(s => s.Concentration).ToList();
        Lab predicted;
        string how;
        if (concentration is > 0 && withStrength.Count >= 2)
        {
            var lower = withStrength.LastOrDefault(s => s.Concentration <= concentration);
            var upper = withStrength.FirstOrDefault(s => s.Concentration >= concentration);
            if (lower != null && upper != null && lower != upper)
            {
                var t = (concentration.Value - lower.Concentration!.Value) / (upper.Concentration!.Value - lower.Concentration.Value);
                var a = Expected(lower, woodLab);
                var b = Expected(upper, woodLab);
                predicted = new Lab(a.L + (b.L - a.L) * t, a.A + (b.A - a.A) * t, a.B + (b.B - a.B) * t);
                how = $"read between the {lower.Concentration}% and {upper.Concentration}% samples";
            }
            else
            {
                var nearest = withStrength.OrderBy(s => Math.Abs(s.Concentration!.Value - concentration.Value)).First();
                predicted = Expected(nearest, woodLab);
                how = $"taken from the nearest strength measured ({nearest.Concentration}%) — {concentration}% itself has not been mixed";
            }
        }
        else
        {
            var labs = used.Select(s => Expected(s, woodLab)).ToList();
            predicted = new Lab(labs.Average(l => l.L), labs.Average(l => l.A), labs.Average(l => l.B));
            how = used.Count == 1 ? "the single sample recorded" : $"the average of {used.Count} samples";
        }

        var confidence = Math.Clamp(Math.Min(1, used.Count / 5.0) * 0.5
                                    + (used.Any(s => s.Source == ColorSource.Device) ? 0.2 : 0)
                                    + (woodLab != null && used.Any(s => s.WoodL != null) ? 0.15 : 0)
                                    + Agreement(used) * 0.15, 0.05, 0.95);
        return new ColorPrediction(predicted, confidence, $"{how}, {speciesNote}.", used.Count);
    }
}
