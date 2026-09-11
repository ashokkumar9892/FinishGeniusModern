using System.Globalization;
using FinishGenius.Api.Controllers;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Services;

/// <summary>Unit conversions used by the formula workspace (legacy formulaInputLogic.js constants).</summary>
public static class FormulaUnits
{
    public const decimal FlOzPerGallon = 128m;
    public const decimal LitresPerGallon = 3.78541178m;
    public const decimal QuartsPerGallon = 4m;

    /// <summary>Legacy container types (DropDownHelper.GetContainerTypes).</summary>
    public static readonly string[] ContainerTypes = ["1 Gallon", "5 Gallons", "Drum", "Quartz"];

    public static decimal FlOz(decimal grams, decimal density) => FormulaCalc.Gallons(grams, density) * FlOzPerGallon;

    /// <summary>Batch size expressed in <paramref name="type"/> units: weight types from grams, volume types from gallons.</summary>
    public static decimal BatchValue(FormulaBatchType type, decimal grams, decimal gallons) => type switch
    {
        FormulaBatchType.Kilograms => grams / 1000m,
        FormulaBatchType.Gallons => gallons,
        FormulaBatchType.Litres => gallons * LitresPerGallon,
        FormulaBatchType.Quarts => gallons * QuartsPerGallon,
        _ => grams,
    };

    public static string Label(FormulaBatchType type) => type.ToString();

    public static string G(decimal grams) => grams.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>
/// Formula "View History" rows (legacy History table, HistoryCategory 0). Stored in the shared audit log so the generic
/// History modal shows them too; OldValue / NewValue feed the legacy Old Value / Value Added / New Value columns.
/// </summary>
public static class FormulaHistory
{
    public const string EntityType = LinkEntityTypes.Formula;

    public static void Add(AppDbContext db, CurrentUser me, int formulaId, int groupId, string action, string? details,
        string? oldValue = null, string? newValue = null) =>
        Add(db, formulaId, groupId, me.Id == 0 ? null : me.Id, string.IsNullOrEmpty(me.UserName) ? null : me.UserName, action, details, oldValue, newValue);

    public static void Add(AppDbContext db, int formulaId, int groupId, int? userId, string? userName, string action, string? details,
        string? oldValue = null, string? newValue = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            EntityType = EntityType,
            EntityId = formulaId,
            GroupId = groupId,
            UserId = userId,
            UserName = userName,
            Action = action,
            Details = details is { Length: > 4000 } ? details[..4000] : details,
            OldValue = Cut(oldValue),
            NewValue = Cut(newValue),
        });
    }

    private static string? Cut(string? v) => v is { Length: > 400 } ? v[..400] : v;
}

/// <summary>
/// The formula's mirror material (type Formula): process steps and schedules pick formulas through it, and
/// <see cref="ScheduleCalculator"/> maps it back to the formula's ingredients via <see cref="Formula.MaterialId"/>.
/// </summary>
public static class FormulaMirror
{
    /// <summary>Creates the mirror when missing and copies name / number / category / group / deleted state onto it (call before SaveChanges).</summary>
    public static async Task<Material> SyncAsync(AppDbContext db, Formula f)
    {
        // Mixed-batch density / price / VOC (informational: material quantities split a formula into its ingredients).
        var ids = f.Ingredients.Select(i => i.MaterialId).Distinct().ToList();
        var mats = ids.Count == 0 ? [] : await db.Materials.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.Density, x.Price, x.Voc, x.Hap, x.Tap }).ToDictionaryAsync(x => x.Id);
        var t = FormulaCalc.Compute(f.Ingredients.Where(i => mats.ContainsKey(i.MaterialId)).Select(i =>
        {
            var x = mats[i.MaterialId];
            return new FormulaCalc.Line(i.Grams, x.Density, x.Price, x.Voc, x.Hap, x.Tap);
        }), 0, 0);

        if (db is LegacyAppDbContext)
        {
            // Legacy database: the formula row IS its material (dbo.Materials, Discriminator 'Formulation'). Nothing is
            // created; the mixed-batch figures go onto the formula row itself (see Data/LegacyModel.Formulas.cs).
            var entry = db.Entry(f);
            if (entry.State != EntityState.Detached && t.TotalGallons > 0)
            {
                entry.Property("PricePerGallon").CurrentValue = (decimal?)t.CostPerGallon;
                entry.Property("VOC").CurrentValue = (double?)t.Voc;
                entry.Property("HAP").CurrentValue = (double?)t.Hap;
                entry.Property("TAP").CurrentValue = (double?)t.Tap;
            }
            return new Material
            {
                Id = f.Id, GroupId = f.GroupId, MaterialType = MaterialType.Formula, CategoryId = f.CategoryId,
                ProductName = f.Name, ProductCode = f.Number, IsDeleted = f.IsDeleted,
            };
        }

        var m = f.Mirror;
        if (m == null && f.MaterialId is { } mid) m = await db.Materials.FirstOrDefaultAsync(x => x.Id == mid);
        var created = m == null;
        if (m == null)
        {
            m = new Material { MaterialType = MaterialType.Formula, CreatedAt = DateTime.UtcNow };
            db.Materials.Add(m);
            f.Mirror = m;
        }
        else m.UpdatedAt = DateTime.UtcNow;
        m.GroupId = f.GroupId;
        m.CategoryId = f.CategoryId;
        m.ProductName = f.Name;
        m.ProductCode = f.Number;
        m.IsDeleted = f.IsDeleted;

        if (created || t.TotalGallons > 0)
        {
            m.Density = t.TotalGallons > 0 ? FormulaCalc.R(t.TotalPounds / t.TotalGallons, 4) : 0;
            m.Price = t.CostPerGallon;
            m.Voc = t.Voc;
            m.Hap = t.Hap;
            m.Tap = t.Tap;
        }
        return m;
    }
}

/// <summary>Legacy role rules of the formula editor.</summary>
public static class FormulaRules
{
    public const string CompleteLocked = "Complete formulas can only be changed by an administrator.";

    /// <summary>Legacy isGroupAdmin: an FG Pro (only) user cannot add / remove / change the ingredients of a Complete formula.</summary>
    public static void EnsureCanChangeIngredients(CurrentUser me, Formula f)
    {
        if (f.IsComplete && me.IsFgProOnly) throw new ApiException(StatusCodes.Status403Forbidden, CompleteLocked);
    }

    /// <summary>Legacy Edit.cshtml: "Calc Batch" is shown to Admin, FGProPlus and FGPro.</summary>
    public static bool CanCalcBatch(CurrentUser me) => me.IsSystemAdmin || me.IsInRole(Roles.FGPro) || me.IsInRole(Roles.FGProPlus);
}

/// <summary>
/// Inventory batches: a batch is a BatchNumber of a material in the inventory ledger; on hand (gallons) = SUM(Quantity).
/// Legacy MaterialBatches (BatchNum + BatchQty in gallons) were imported as opening balances with the batch number.
/// </summary>
public static class InventoryBatches
{
    public record Batch(string BatchNumber, decimal OnHand);

    /// <summary>Batches with stock per material, in creation order (first inventory transaction of the batch).</summary>
    public static async Task<Dictionary<int, List<Batch>>> ForMaterialsAsync(AppDbContext db, IEnumerable<int> materialIds)
    {
        var ids = materialIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.MaterialId) && t.BatchNumber != null && t.BatchNumber != "")
            .GroupBy(t => new { t.MaterialId, t.BatchNumber })
            .Select(g => new { g.Key.MaterialId, g.Key.BatchNumber, OnHand = g.Sum(t => t.Quantity), First = g.Min(t => t.Id) })
            .ToListAsync();
        return rows.Where(r => r.OnHand > 0)
            .GroupBy(r => r.MaterialId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.First).Select(r => new Batch(r.BatchNumber!, r.OnHand)).ToList());
    }

    /// <summary>
    /// The batch an ingredient uses: the saved batch, otherwise (legacy default) the most recently created batch with stock.
    /// Null when a saved batch no longer has stock or the material has no batches.
    /// </summary>
    public static Batch? Chosen(IReadOnlyList<Batch>? list, string? saved)
    {
        if (list == null || list.Count == 0) return null;
        return string.IsNullOrEmpty(saved) ? list[^1] : list.FirstOrDefault(b => b.BatchNumber == saved);
    }

    public static async Task<decimal> OnHandAsync(AppDbContext db, int materialId, string batchNumber) =>
        await db.InventoryTransactions.Where(t => t.MaterialId == materialId && t.BatchNumber == batchNumber).SumAsync(t => (decimal?)t.Quantity) ?? 0;

    /// <summary>Legacy: batch columns and the dispense workflow appear only for groups that track inventory batches.</summary>
    public static Task<bool> GroupUsesBatchesAsync(AppDbContext db, int groupId) =>
        db.InventoryTransactions.AnyAsync(t => t.GroupId == groupId && t.BatchNumber != null && t.BatchNumber != "");
}
