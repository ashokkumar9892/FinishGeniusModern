using System.Globalization;
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
        var overrideMaterialIds = steps.SelectMany(s => s.Overrides).Select(o => o.Value).ToList();

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
        _ = overrideMaterialIds;
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

    public async Task<QuantityResult> QuantitiesAsync(int scheduleId, decimal oneSided, decimal twoSided)
    {
        var sqft = Math.Max(0, oneSided) + 2 * Math.Max(0, twoSided);
        var values = await LoadValuesAsync(scheduleId);

        var gallonsPerSqFt = new Dictionary<int, decimal>();
        decimal hoursPerSqFt = 0, otherPerSqFt = 0;

        foreach (var entry in values.GroupBy(v => (v.ScheduleStepId, v.SubStepSequence, v.Pass)))
        {
            var ordered = entry.OrderBy(v => v.CharacteristicSequence).ToList();
            var coverage = ordered.Where(v => v.CalcVariable == CalcVariables.Coverage).Select(v => Num(v.Value)).FirstOrDefault(n => n > 0);
            var rate = ordered.Where(v => v.CalcVariable == CalcVariables.ProductionRate).Select(v => Num(v.Value)).FirstOrDefault(n => n > 0);
            if (rate is > 0) hoursPerSqFt += 1m / rate.Value;
            otherPerSqFt += ordered.Where(v => v.CalcVariable == CalcVariables.CostPerSqFt).Sum(v => Num(v.Value) ?? 0);

            if (coverage is not > 0) continue;
            int? current = null;
            var mix = new List<(int materialId, decimal percent)>();
            foreach (var v in ordered)
            {
                if (v.CalcVariable == CalcVariables.MaterialId && v.MaterialId != null)
                {
                    current = v.MaterialId;
                    mix.Add((v.MaterialId.Value, 100m));
                }
                else if (v.CalcVariable == CalcVariables.MixPercent && current != null && Num(v.Value) is { } pct)
                {
                    mix[^1] = (mix[^1].materialId, pct);
                }
            }
            foreach (var (materialId, percent) in mix)
            {
                var g = 1m / coverage.Value * percent / 100m;
                gallonsPerSqFt[materialId] = gallonsPerSqFt.GetValueOrDefault(materialId) + g;
            }
        }

        var ids = gallonsPerSqFt.Keys.ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        var lines = gallonsPerSqFt
            .Where(kv => materials.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var m = materials[kv.Key];
                var qty = Math.Round(kv.Value * sqft, 3);
                var name = string.IsNullOrWhiteSpace(m.ProductCode) ? m.ProductName : $"{m.ProductName} ({m.ProductCode})";
                return new MaterialQuantityLine(m.Id, name, m.ProductCode, qty, "Gallons", m.Price, Math.Round(qty * m.Price, 2));
            })
            .OrderBy(l => l.Name).ToList();
        var materialCostPerSqFt = gallonsPerSqFt.Where(kv => materials.ContainsKey(kv.Key)).Sum(kv => kv.Value * materials[kv.Key].Price);

        return new QuantityResult(sqft, Math.Round(sqft * hoursPerSqFt, 2), lines, materialCostPerSqFt, hoursPerSqFt, otherPerSqFt);
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
