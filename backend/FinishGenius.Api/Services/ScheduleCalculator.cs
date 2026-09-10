using System.Globalization;
using System.Text.RegularExpressions;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Services;

/// <summary>One filled value of a schedule, with schedule-level overrides already applied.</summary>
public record ScheduleValue(
    int ScheduleStepId, int StepNumber, string StepName, int SubStepSequence, string SubStepLabel, string SubStepName,
    string? SubStepInstruction, int Pass, int ProcessStepValueId, int CharacteristicSequence, string Characteristic,
    string? Unit, string InputType, string? CalcVariable, string? Value, int? MaterialId, string? MaterialName,
    decimal? MinValue, decimal? MaxValue);

public record MaterialQuantityLine(int MaterialId, string Name, string? ProductCode, decimal Quantity, string Unit, decimal Price, decimal Cost);

public record QuantityResult(decimal SquareFootage, decimal ProductionHours, List<MaterialQuantityLine> Materials,
    decimal MaterialCostPerSqFt, decimal HoursPerSqFt, decimal OtherCostPerSqFt);

public class PricingInput
{
    public decimal LaborRate { get; set; }
    public decimal MarkUp { get; set; }
    public decimal PremiumMarkUp { get; set; }
    public decimal OneSidedComplexity { get; set; }
    public decimal OneSidedArea { get; set; }
    public decimal TwoSidedComplexity { get; set; }
    public decimal TwoSidedArea { get; set; }
    public decimal HighComplexity { get; set; }
    public decimal HighComplexityArea { get; set; }
}

public record PricingRow(string Label, decimal Area, decimal ComplexityPercent, decimal Sides, decimal Cost);

public record PricingResult(decimal TotalPrice, decimal TotalSquareFootage, decimal BaseCostPerSqFt,
    decimal MaterialCostPerSqFt, decimal LaborCostPerSqFt, decimal OtherCostPerSqFt, List<PricingRow> Rows, decimal Subtotal);

/// <summary>
/// Turns a process schedule (steps → sub step entries → characteristic values) into
/// material quantities, production time, prices and My Work checklist lines.
///
/// Calculation rules (characteristics are tagged with a CalcVariable in Material Categories):
///  * Square footage          = One Sided + 2 × Two Sided.
///  * Coverage (sq ft/gal)    applies to every material chosen in the same sub step entry.
///  * MixPercent              belongs to the material chosen just before it (default 100 %).
///  * Material gallons        = SqFt ÷ Coverage × Mix% / 100, summed per material.
///  * ProductionRate (sq ft/h) → Production hours = Σ SqFt ÷ rate.
///  * CostPerSqFt             → extra $ per sq ft (process costing).
///  * Pricing: base $/sq ft = material $/sq ft + hours/sq ft × labor rate + other $/sq ft;
///    each area row costs base × area × sides × (1 + complexity %); one/two sided rows take Mark-Up,
///    the high complexity row takes Mark-Up and Premium Mark-Up.
/// </summary>
public class ScheduleCalculator(AppDbContext db)
{
    public async Task<List<ScheduleValue>> LoadValuesAsync(int scheduleId)
    {
        var steps = await db.ProcessScheduleSteps.AsNoTracking()
            .Where(s => s.ScheduleId == scheduleId)
            .Include(s => s.ProcessStep)
            .Include(s => s.Overrides)
            .OrderBy(s => s.Ordering).ThenBy(s => s.Id)
            .ToListAsync();
        var stepIds = steps.Select(s => s.ProcessStepId).Distinct().ToList();
        var entries = await db.ProcessStepEntries.AsNoTracking()
            .Where(e => stepIds.Contains(e.ProcessStepId))
            .Include(e => e.SubStep)
            .Include(e => e.Values).ThenInclude(v => v.Characteristic)
            .Include(e => e.Values).ThenInclude(v => v.Material)
            .ToListAsync();

        var result = new List<ScheduleValue>();
        var number = 0;
        foreach (var s in steps)
        {
            number++;
            var overrides = s.Overrides.ToDictionary(o => o.ProcessStepValueId);
            foreach (var e in entries.Where(e => e.ProcessStepId == s.ProcessStepId)
                         .OrderBy(e => e.SubStep!.Sequence).ThenBy(e => e.Pass))
            {
                var sub = e.SubStep!;
                var label = SubStepLabel(sub.Sequence, sub.PassThroughs, e.Pass);
                foreach (var v in e.Values.OrderBy(v => v.Characteristic?.Sequence ?? 0).ThenBy(v => v.Id))
                {
                    var c = v.Characteristic;
                    if (c == null) continue;
                    overrides.TryGetValue(v.Id, out var ov);
                    var value = ov?.Value ?? v.Value;
                    var materialId = v.MaterialId;
                    var materialName = v.Material?.ProductName;
                    if (c.InputType == CharacteristicInputTypes.Material && ov?.Value != null && int.TryParse(ov.Value, out var mid))
                    {
                        materialId = mid;
                        materialName = null; // resolved below
                    }
                    result.Add(new ScheduleValue(s.Id, number, s.NameOverride ?? s.ProcessStep?.Name ?? "", sub.Sequence, label,
                        sub.Name, sub.Instruction, e.Pass, v.Id, c.Sequence, c.Name, c.Unit, c.InputType, c.CalcVariable,
                        value, materialId, materialName, ov?.MinValue, ov?.MaxValue));
                }
            }
        }

        // Resolve names for materials substituted by overrides.
        var missing = result.Where(r => r.MaterialId != null && r.MaterialName == null).Select(r => r.MaterialId!.Value).Distinct().ToList();
        if (missing.Count > 0)
        {
            var names = await db.Materials.Where(m => missing.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ProductName);
            result = result.Select(r => r.MaterialId != null && r.MaterialName == null && names.TryGetValue(r.MaterialId.Value, out var n)
                ? r with { MaterialName = n } : r).ToList();
        }
        return result;
    }

    public static string SubStepLabel(int sequence, int passes, int pass) =>
        passes > 1 ? $"{sequence}{(char)('A' + Math.Clamp(pass - 1, 0, 25))}" : sequence.ToString(CultureInfo.InvariantCulture);

    public static decimal? Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var cleaned = new string(s.Trim().Where(ch => char.IsDigit(ch) || ch is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static readonly Regex MiscVar = new(@"^Misc(\d+)_(ID|Qty)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool IsLegacyCalc(string? c) =>
        c is CalcVariables.MaterialCoverage or CalcVariables.MaterialQty || (c != null && MiscVar.IsMatch(c));

    /// <summary>"55%", "55" or "0.55" → 0.55; null when missing or not positive.</summary>
    private static decimal? Fraction(string? s)
    {
        var n = Num(s);
        if (n is not > 0) return null;
        return n > 1 ? n / 100m : n;
    }

    private static decimal Positive(string? s) => Num(s) is decimal d && d > 0 ? d : 0;

    public async Task<QuantityResult> QuantitiesAsync(int scheduleId, decimal oneSided, decimal twoSided)
    {
        var sqft = Math.Max(0, oneSided) + 2 * Math.Max(0, twoSided);
        var values = await LoadValuesAsync(scheduleId);
        var recipes = await LoadRecipesAsync(values.Where(v => v.MaterialId != null).Select(v => v.MaterialId!.Value).Distinct().ToList());

        // quantity per single sq ft, keyed by material + unit ("Gallons" / "Pieces")
        var perSqFt = new Dictionary<(int materialId, string unit), decimal>();
        void AddRaw(int materialId, string unit, decimal q)
        {
            var key = (materialId, unit);
            perSqFt[key] = perSqFt.GetValueOrDefault(key) + q;
        }
        // Gallons of a formula are split into its ingredients by volume (the legacy report lists ingredients).
        void Add(int? materialId, string unit, decimal q)
        {
            if (materialId == null || q <= 0) return;
            if (unit == "Gallons" && recipes.TryGetValue(materialId.Value, out var recipe))
            {
                var batch = recipe.Sum(r => r.gallons);
                foreach (var (ingredient, gallons) in recipe) AddRaw(ingredient, unit, q * gallons / batch);
                return;
            }
            AddRaw(materialId.Value, unit, q);
        }
        decimal? RecipeGallons(int? id) => id != null && recipes.TryGetValue(id.Value, out var r) ? r.Sum(x => x.gallons) : null;
        decimal hoursPerSqFt = 0, otherPerSqFt = 0;

        foreach (var step in values.GroupBy(v => v.ScheduleStepId))
        {
            // Legacy Gun_TE: only this share of the sprayed material reaches the part.
            var te = step.Where(v => v.CalcVariable == CalcVariables.GunTransferEfficiency)
                .Select(v => Fraction(v.Value)).FirstOrDefault(f => f is > 0 and <= 1) ?? 1m;
            // Legacy labour / setup minutes per sq ft.
            hoursPerSqFt += step.Where(v => v.CalcVariable is CalcVariables.StepLabor or CalcVariables.StepSetup).Sum(v => Positive(v.Value)) / 60m;

            foreach (var entry in step.GroupBy(v => (v.SubStepSequence, v.Pass)))
            {
                var ordered = entry.OrderBy(v => v.CharacteristicSequence).ToList();
                var rate = ordered.Where(v => v.CalcVariable == CalcVariables.ProductionRate).Select(v => Num(v.Value)).FirstOrDefault(n => n > 0);
                if (rate is > 0) hoursPerSqFt += 1m / rate.Value;
                otherPerSqFt += ordered.Where(v => v.CalcVariable == CalcVariables.CostPerSqFt).Sum(v => Num(v.Value) ?? 0);

                if (ordered.Any(v => IsLegacyCalc(v.CalcVariable))) AddLegacyEntry(ordered, te, Add, RecipeGallons);
                else AddModernEntry(ordered, Add);
            }
        }

        var ids = perSqFt.Keys.Select(k => k.materialId).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var lines = perSqFt
            .Where(kv => materials.ContainsKey(kv.Key.materialId))
            .Select(kv =>
            {
                var m = materials[kv.Key.materialId];
                var qty = Math.Round(kv.Value * sqft, 3);
                var name = string.IsNullOrWhiteSpace(m.ProductCode) ? m.ProductName : $"{m.ProductName} ({m.ProductCode})";
                return new MaterialQuantityLine(m.Id, name, m.ProductCode, qty, kv.Key.unit, m.Price, Math.Round(qty * m.Price, 2));
            })
            .OrderBy(l => l.Unit).ThenBy(l => l.Name).ToList();
        var materialCostPerSqFt = perSqFt.Where(kv => materials.ContainsKey(kv.Key.materialId)).Sum(kv => kv.Value * materials[kv.Key.materialId].Price);

        return new QuantityResult(sqft, Math.Round(sqft * hoursPerSqFt, 2), lines, materialCostPerSqFt, hoursPerSqFt, otherPerSqFt);
    }

    /// <summary>Formula → its ingredients' gallons in one batch.</summary>
    private async Task<Dictionary<int, List<(int materialId, decimal gallons)>>> LoadRecipesAsync(List<int> ids)
    {
        if (ids.Count == 0) return new();
        var rows = await db.FormulaIngredients.AsNoTracking()
            .Where(i => ids.Contains(i.FormulaId) && i.Grams > 0)
            .Select(i => new { i.FormulaId, i.MaterialId, i.Grams, Density = i.Material!.Density })
            .ToListAsync();
        return rows.Where(r => r.Density > 0).GroupBy(r => r.FormulaId)
            .ToDictionary(g => g.Key, g => g.Select(r => (r.MaterialId, GramsToGallons(r.Grams, r.Density))).ToList());
    }

    /// <summary>
    /// Grams → gallons. Material density is lb/gal, but part of the legacy data stores g/cc (the legacy column is
    /// "GramsPerCubicCentiMetres"); real coatings are 6–20 lb/gal or 0.6–2.5 g/cc, so values below 3 are treated as g/cc.
    /// </summary>
    public static decimal GramsToGallons(decimal grams, decimal density) =>
        density <= 0 ? 0 : grams / (density * (density < 3 ? 3785.41m : 453.59237m));

    /// <summary>Coverage applies to the entry's materials; MixPercent belongs to the material chosen just before it.</summary>
    private static void AddModernEntry(List<ScheduleValue> ordered, Action<int?, string, decimal> add)
    {
        var coverage = ordered.Where(v => v.CalcVariable == CalcVariables.Coverage).Select(v => Num(v.Value)).FirstOrDefault(n => n > 0);
        if (coverage is not > 0) return;
        var mix = new List<(int materialId, decimal percent)>();
        foreach (var v in ordered)
        {
            if (v.CalcVariable == CalcVariables.MaterialId && v.MaterialId != null)
                mix.Add((v.MaterialId.Value, 100m));
            else if (v.CalcVariable == CalcVariables.MixPercent && mix.Count > 0 && Num(v.Value) is { } pct)
                mix[^1] = (mix[^1].materialId, pct);
        }
        foreach (var (materialId, percent) in mix)
            add(materialId, "Gallons", 1m / coverage.Value * percent / 100m);
    }

    /// <summary>
    /// Legacy rules (verified against the AWFI "Bamboo" test: 60 sq ft → 0.064 / 0.007 / 0.006 gal, 0.50 h):
    /// per sq ft, a formula base uses (1 ÷ coverage ÷ gun TE) batches, split into ingredient gallons; a plain material uses
    /// (1 ÷ coverage ÷ gun TE) × Material_Qty gallons. Coverage ≤ 1 is the legacy "not set" placeholder and is skipped.
    /// MiscN additive in fl oz/gal → base × oz ÷ 128; in "sq ft per piece" → pieces = 1 ÷ value; in "sq ft / qt" →
    /// gallons = 1 ÷ value ÷ 4; in % → base × %.
    /// </summary>
    private static void AddLegacyEntry(List<ScheduleValue> ordered, decimal te, Action<int?, string, decimal> add, Func<int?, decimal?> recipeGallons)
    {
        var coverageValue = ordered.FirstOrDefault(v => v.CalcVariable == CalcVariables.MaterialCoverage);
        var coverage = Num(coverageValue?.Value);
        var coverageIsArea = coverageValue?.Unit is not { Length: > 0 } u || u.Contains("sq", StringComparison.OrdinalIgnoreCase);
        decimal? baseGal = null;
        if (coverage is > 1 && coverageIsArea)
        {
            var baseMaterial = ordered.FirstOrDefault(v => v.CalcVariable == CalcVariables.MaterialId && v.MaterialId != null)?.MaterialId;
            var perUnit = 1m / coverage.Value / te;
            var batch = recipeGallons(baseMaterial);
            var materialQty = Num(ordered.FirstOrDefault(v => v.CalcVariable == CalcVariables.MaterialQty)?.Value);
            baseGal = batch != null ? perUnit * batch.Value : perUnit * (materialQty is > 0 ? materialQty.Value : 1m);
            add(baseMaterial, "Gallons", baseGal.Value);
        }

        var pairs = ordered
            .Select(v => (v, m: v.CalcVariable == null ? Match.Empty : MiscVar.Match(v.CalcVariable)))
            .Where(x => x.m.Success)
            .GroupBy(x => x.m.Groups[1].Value);
        foreach (var pair in pairs)
        {
            var material = pair.FirstOrDefault(x => x.m.Groups[2].Value.Equals("ID", StringComparison.OrdinalIgnoreCase)).v?.MaterialId;
            var qtyValue = pair.FirstOrDefault(x => x.m.Groups[2].Value.Equals("Qty", StringComparison.OrdinalIgnoreCase)).v;
            if (material == null || qtyValue == null || Num(qtyValue.Value) is not > 0) continue;
            var qty = Num(qtyValue.Value)!.Value;
            var unit = (qtyValue.Unit ?? "").ToLowerInvariant();
            if (unit.Contains("oz")) { if (baseGal != null) add(material, "Gallons", baseGal.Value * qty / 128m); }
            else if (unit.Contains("qt")) add(material, "Gallons", 1m / qty / 4m);
            else if (unit.Contains("sq")) add(material, "Pieces", 1m / qty);
            else if (unit.Contains('%') && baseGal != null) add(material, "Gallons", baseGal.Value * qty / 100m);
        }
    }

    public async Task<PricingResult> PriceAsync(int scheduleId, PricingInput p)
    {
        var unit = await QuantitiesAsync(scheduleId, 1, 0); // per single-sided sq ft
        var labor = unit.HoursPerSqFt * Math.Max(0, p.LaborRate);
        var basePerSqFt = unit.MaterialCostPerSqFt + labor + unit.OtherCostPerSqFt;

        PricingRow Row(string label, decimal area, decimal complexity, decimal sides) =>
            new(label, Math.Max(0, area), complexity, sides,
                Math.Round(basePerSqFt * Math.Max(0, area) * sides * (1 + complexity / 100m), 4));

        var one = Row("One Sided Finishing", p.OneSidedArea, p.OneSidedComplexity, 1);
        var two = Row("Two Sided Finishing", p.TwoSidedArea, p.TwoSidedComplexity, 2);
        var high = Row("High Complexity", p.HighComplexityArea, p.HighComplexity, 1);

        var markup = 1 + p.MarkUp / 100m;
        var subtotal = one.Cost + two.Cost + high.Cost;
        var total = (one.Cost + two.Cost) * markup + high.Cost * markup * (1 + p.PremiumMarkUp / 100m);
        var totalArea = one.Area + two.Area + high.Area;

        return new PricingResult(Math.Round(total, 2), totalArea, Math.Round(basePerSqFt, 4),
            Math.Round(unit.MaterialCostPerSqFt, 4), Math.Round(labor, 4), Math.Round(unit.OtherCostPerSqFt, 4),
            [one, two, high], Math.Round(subtotal, 2));
    }

    /// <summary>Builds the My Work checklist for a new execution.</summary>
    public async Task<List<WorkExecutionLine>> BuildChecklistAsync(int scheduleId)
    {
        var values = await LoadValuesAsync(scheduleId);
        var stepRows = await db.ProcessScheduleSteps.AsNoTracking().Where(s => s.ScheduleId == scheduleId)
            .Include(s => s.ProcessStep).OrderBy(s => s.Ordering).ThenBy(s => s.Id).ToListAsync();

        var lines = new List<WorkExecutionLine>();
        var number = 0;
        foreach (var s in stepRows)
        {
            number++;
            var seq = 0;
            var stepValues = values.Where(v => v.ScheduleStepId == s.Id).ToList();
            foreach (var v in stepValues)
            {
                var display = v.InputType == CharacteristicInputTypes.Material ? v.MaterialName : v.Value;
                if (string.IsNullOrWhiteSpace(display)) continue;
                if (v.InputType == CharacteristicInputTypes.Number && !string.IsNullOrWhiteSpace(v.Unit) && v.Unit != "-")
                    display = $"{display} {v.Unit}";
                lines.Add(new WorkExecutionLine
                {
                    StepNumber = number,
                    StepName = s.NameOverride ?? s.ProcessStep?.Name ?? "",
                    Sequence = ++seq,
                    Description = $"{v.SubStepLabel} · {v.Characteristic}",
                    Value = display,
                    Unit = v.Unit,
                    MinValue = v.MinValue,
                    MaxValue = v.MaxValue,
                });
            }
            if (seq == 0)
            {
                lines.Add(new WorkExecutionLine
                {
                    StepNumber = number,
                    StepName = s.NameOverride ?? s.ProcessStep?.Name ?? "",
                    Sequence = 1,
                    Description = "Complete this process step",
                });
            }
        }
        return lines;
    }
}
