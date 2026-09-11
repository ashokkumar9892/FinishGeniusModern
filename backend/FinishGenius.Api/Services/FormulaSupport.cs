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
/// Inventory batches: a batch is a BatchNumber of a material in the inventory ledger; on hand (gallons) = SUM(Quantity).
/// Legacy MaterialBatches (BatchNum + BatchQty in gallons) were imported as opening balances with the batch number.
/// </summary>
public static class InventoryBatches
{
    public record Batch(string BatchNumber, decimal OnHand);

    public static async Task<Dictionary<int, List<Batch>>> ForMaterialsAsync(AppDbContext db, IEnumerable<int> materialIds)
    {
        var ids = materialIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.InventoryTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.MaterialId) && t.BatchNumber != null && t.BatchNumber != "")
            .GroupBy(t => new { t.MaterialId, t.BatchNumber })
            .Select(g => new { g.Key.MaterialId, g.Key.BatchNumber, OnHand = g.Sum(t => t.Quantity) })
            .ToListAsync();
        return rows.Where(r => r.OnHand > 0)
            .GroupBy(r => r.MaterialId)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.BatchNumber).Select(r => new Batch(r.BatchNumber!, r.OnHand)).ToList());
    }

    public static async Task<decimal> OnHandAsync(AppDbContext db, int materialId, string batchNumber) =>
        await db.InventoryTransactions.Where(t => t.MaterialId == materialId && t.BatchNumber == batchNumber).SumAsync(t => (decimal?)t.Quantity) ?? 0;

    /// <summary>Legacy: batch columns and the dispense workflow appear only for groups that track inventory batches.</summary>
    public static Task<bool> GroupUsesBatchesAsync(AppDbContext db, int groupId) =>
        db.InventoryTransactions.AnyAsync(t => t.GroupId == groupId && t.BatchNumber != null && t.BatchNumber != "");
}
