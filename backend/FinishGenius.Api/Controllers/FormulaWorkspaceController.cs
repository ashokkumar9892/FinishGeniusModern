using System.Globalization;
using ClosedXML.Excel;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// The shop-floor side of the legacy "Edit Formula" page: scale / dispense machine / label printer selection, inventory
/// batch per ingredient, Amount to Dispense / Total Dispensed, Calc Batch (recalc), Reweigh Formula, Dispense (via the
/// network bridge), Undo Dispense, Record &amp; Reset (inventory deduction), can labels, View History and the Excel export.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Formulas)]
[Route("api/formulas")]
public class FormulaWorkspaceController(AppDbContext db, CurrentUser me, AuditService audit, DeviceCommandService commands) : ControllerBase
{
    public record DeviceChoice(int? DeviceId);
    public record BatchChoice(string? BatchNumber);
    public record RecalcRequest(int IngredientId, decimal Grams);
    public record ReweighRequest(decimal TotalGrams);
    public record RecordRequest(int? LocationId);
    public record DispenseRequest(int? DeviceId);
    public record PrintLabelRequest(int? PrinterDeviceId, bool CustomerLabel);

    private static readonly MaterialType[] LocationTypes = [MaterialType.Base, MaterialType.Pigment, MaterialType.Dye];
    private const string DispensedFirst = "Please Record & Reset or revert your dispense operation.";

    // ------------------------------------------------------------------ workspace

    /// <summary>GET /api/formulas/{id}/workspace — devices, the user's last scale/printer, batches, locations, nozzle state.</summary>
    [HttpGet("{id:int}/workspace")]
    public async Task<IActionResult> Workspace(int id)
    {
        var f = await LoadAsync(id);
        var devices = await db.Devices.AsNoTracking().Include(d => d.Canisters)
            .Where(d => d.GroupId == f.GroupId && !d.IsArchived).OrderBy(d => d.Name).ToListAsync();
        var byId = devices.ToDictionary(d => d.Id);
        var cutoff = DateTime.UtcNow - DevicesController.OnlineWindow;
        Device? BridgeOf(Device d) => d.NetworkBridgeId is { } b && byId.TryGetValue(b, out var x) && x.DeviceType == DeviceType.NetworkBridge ? x : null;

        var scales = devices.Where(d => d.DeviceType is DeviceType.ScaleGrams or DeviceType.ScaleKilograms).Select(d => new
        {
            d.Id, d.Name, Unit = d.DeviceType == DeviceType.ScaleKilograms ? "kg" : "g",
            BridgeName = BridgeOf(d)?.Name, BridgeOnline = BridgeOf(d)?.LastSeenAt >= cutoff,
        }).ToList();
        var dispensers = devices.Where(d => d.DeviceType == DeviceType.DispenseMachine).Select(d => new
        {
            d.Id, d.Name, BridgeId = BridgeOf(d)?.Id, BridgeName = BridgeOf(d)?.Name, BridgeOnline = BridgeOf(d)?.LastSeenAt >= cutoff,
            Canisters = d.Canisters.Where(c => c.MaterialId != null).OrderBy(c => c.CanisterNo).Select(c => new { c.CanisterNo, c.MaterialId }).ToList(),
        }).ToList();
        var printers = devices.Where(d => d.DeviceType == DeviceType.LabelPrinter).Select(d => new
        {
            d.Id, d.Name, BridgeName = BridgeOf(d)?.Name, BridgeOnline = BridgeOf(d)?.LastSeenAt >= cutoff,
        }).ToList();

        var pref = me.Id == 0 ? null : await db.FormulaDevicePreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == me.Id && p.FormulaId == id);
        var batches = await InventoryBatches.ForMaterialsAsync(db, f.Ingredients.Select(i => i.MaterialId));
        var locations = await db.MaterialLocations.AsNoTracking()
            .Where(l => l.GroupId == f.GroupId && !l.IsDeleted && LocationTypes.Contains(l.MaterialType))
            .OrderBy(l => l.Name).Select(l => new { l.Id, l.Name }).ToListAsync();
        var setting = await db.DispenseSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == f.GroupId);
        var last = await DispensingController.LastDispensedAtAsync(db, f.GroupId, setting);
        var bridgeIds = dispensers.Where(d => d.BridgeId != null).Select(d => d.BridgeId!.Value).Distinct().ToList();
        var failures = await db.PurgeFailures.AsNoTracking().Where(p => bridgeIds.Contains(p.BridgeDeviceId) && p.IsActive)
            .OrderByDescending(p => p.CreatedAt).Take(50)
            .Select(p => new { p.BridgeDeviceId, p.CanisterNumber, p.Message, p.CreatedAt }).ToListAsync();

        var open = await db.DeviceCommands.Where(c => c.FormulaId == id && c.CommandType == DeviceCommandTypes.Dispense
                && (c.Status == DeviceCommandStatuses.Pending || c.Status == DeviceCommandStatuses.Sent))
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        if (open != null && DeviceCommandService.Expire(open))
        {
            await db.SaveChangesAsync();
            open = null;
        }

        return Ok(new
        {
            Scales = scales,
            Dispensers = dispensers,
            Printers = printers,
            ScaleDeviceId = pref?.ScaleDeviceId is { } s && scales.Any(x => x.Id == s) ? s : (int?)null,
            PrinterDeviceId = pref?.PrinterDeviceId is { } p && printers.Any(x => x.Id == p) ? p : (int?)null,
            DispenserId = f.DispenserId is { } d && dispensers.Any(x => x.Id == d) ? d : (int?)null,
            UsesBatches = await InventoryBatches.GroupUsesBatchesAsync(db, f.GroupId),
            Batches = batches.ToDictionary(k => k.Key.ToString(CultureInfo.InvariantCulture), v => v.Value),
            Locations = locations,
            Nozzle = new
            {
                CleanNozzleHours = setting?.CleanNozzleHours ?? 0, IsNozzleCleaned = setting?.IsNozzleCleaned ?? false, LastDispensedAt = last,
                CleaningRequired = DispensingController.CleaningRequired(setting, last),
            },
            PurgeFailures = failures,
            HasUndoDispense = await db.FormulaDispenseSnapshots.AnyAsync(x => x.FormulaId == id),
            OpenDispenseCommandId = open?.Id,
        });
    }

    /// <summary>PUT /api/formulas/{id}/preferences/scale { deviceId } — "Please select a scale..." (remembered per user).</summary>
    [HttpPut("{id:int}/preferences/scale")]
    public async Task<IActionResult> SetScale(int id, [FromBody] DeviceChoice req)
    {
        var f = await LoadAsync(id, ingredients: false);
        Device? d = null;
        if (req.DeviceId is > 0)
            d = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.DeviceId && x.GroupId == f.GroupId && !x.IsArchived
                    && (x.DeviceType == DeviceType.ScaleGrams || x.DeviceType == DeviceType.ScaleKilograms))
                ?? throw ApiException.Bad("Please select a scale of this group.");
        var pref = await PreferenceAsync(f.Id);
        pref.ScaleDeviceId = d?.Id;
        pref.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = d == null ? "Default scale removed." : $"You have set your default scale for this process to {d.Name}" });
    }

    /// <summary>PUT /api/formulas/{id}/preferences/printer { deviceId } — "Please select a Label Printer..." (remembered per user).</summary>
    [HttpPut("{id:int}/preferences/printer")]
    public async Task<IActionResult> SetPrinter(int id, [FromBody] DeviceChoice req)
    {
        var f = await LoadAsync(id, ingredients: false);
        Device? d = null;
        if (req.DeviceId is > 0)
            d = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.DeviceId && x.GroupId == f.GroupId && !x.IsArchived && x.DeviceType == DeviceType.LabelPrinter)
                ?? throw ApiException.Bad("Please Select a Printer!");
        var pref = await PreferenceAsync(f.Id);
        pref.PrinterDeviceId = d?.Id;
        pref.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = d == null ? "Default printer removed." : $"You have set your default printer for this process to {d.Name}" });
    }

    /// <summary>PUT /api/formulas/{id}/dispenser { deviceId } — "Please select a Dispense Machine" (stored on the formula).</summary>
    [HttpPut("{id:int}/dispenser")]
    public async Task<IActionResult> SetDispenser(int id, [FromBody] DeviceChoice req)
    {
        var f = await LoadAsync(id, ingredients: false);
        Device? d = null;
        if (req.DeviceId is > 0)
            d = await db.Devices.AsNoTracking().FirstOrDefaultAsync(x => x.Id == req.DeviceId && x.GroupId == f.GroupId && !x.IsArchived && x.DeviceType == DeviceType.DispenseMachine)
                ?? throw ApiException.Bad("Please select a dispenser device to continue.");
        f.DispenserId = d?.Id;
        await db.SaveChangesAsync();
        return Ok(new { message = d == null ? "Default dispenser removed." : $"You have set your default dispenser for this process to {d.Name}" });
    }

    // ------------------------------------------------------------------ batches and per-ingredient dispense state

    /// <summary>PUT /api/formulas/{id}/ingredients/{ingredientId}/batch { batchNumber } — "Batch #" selection (saved immediately).</summary>
    [HttpPut("{id:int}/ingredients/{ingredientId:int}/batch")]
    public async Task<IActionResult> SetBatch(int id, int ingredientId, [FromBody] BatchChoice req)
    {
        var (f, i) = await IngredientAsync(id, ingredientId);
        var batch = Text.Clean(req.BatchNumber);
        if (batch != null && !await db.InventoryTransactions.AnyAsync(t => t.MaterialId == i.MaterialId && t.BatchNumber == batch))
            throw ApiException.Bad("The selected batch no longer exists.");
        if (batch == i.BatchNumber) return Ok(new { message = "Batch selection saved!" });
        FormulaHistory.Add(db, me, f.Id, f.GroupId, batch == null ? "Batch removed" : "Batch changed",
            $"{MaterialTypes.Label(i.Material!.MaterialType)} {Describe(i.Material)}", i.BatchNumber ?? "-", batch ?? "-");
        i.BatchNumber = batch;
        await db.SaveChangesAsync();
        return Ok(new { message = "Batch selection saved!" });
    }

    /// <summary>POST …/mark-dispensed — the orange ⊕: Amount to Dispense is added to Total Dispensed.</summary>
    [HttpPost("{id:int}/ingredients/{ingredientId:int}/mark-dispensed")]
    public async Task<IActionResult> MarkDispensed(int id, int ingredientId)
    {
        var (f, i) = await IngredientAsync(id, ingredientId);
        if (i.DispenseAmount <= 0) throw ApiException.Bad("There is nothing left to dispense for this material.");
        var before = i.DispensedGrams;
        i.DispensedGrams = FormulaCalc.R(i.DispensedGrams + i.DispenseAmount, 4);
        i.DispenseAmount = 0;
        i.IsDispensed = true;
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Color dispensed", Describe(i.Material!), G(before), G(i.DispensedGrams));
        await db.SaveChangesAsync();
        return Ok(new { message = $"{i.Material!.ProductName}: {G(i.DispensedGrams)} g dispensed.", totalDispensed = i.DispensedGrams });
    }

    /// <summary>POST …/revert-dispensed — the orange ⊖: the dispensed grams go back to Amount to Dispense.</summary>
    [HttpPost("{id:int}/ingredients/{ingredientId:int}/revert-dispensed")]
    public async Task<IActionResult> RevertDispensed(int id, int ingredientId)
    {
        var (f, i) = await IngredientAsync(id, ingredientId);
        if (i.DispensedGrams <= 0 && !i.IsDispensed) throw ApiException.Bad("Nothing has been dispensed for this material.");
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Color dispense undone", Describe(i.Material!), G(i.DispensedGrams), "0");
        i.DispenseAmount = FormulaCalc.R(i.DispenseAmount + i.DispensedGrams, 4);
        i.DispensedGrams = 0;
        i.IsDispensed = false;
        await db.SaveChangesAsync();
        return Ok(new { message = "Dispense reverted.", dispenseAmount = i.DispenseAmount });
    }

    /// <summary>POST …/reset-dispense — the grey ⊟ (legacy DispensedRevert): clears Amount to Dispense and Total Dispensed.</summary>
    [HttpPost("{id:int}/ingredients/{ingredientId:int}/reset-dispense")]
    public async Task<IActionResult> ResetDispense(int id, int ingredientId)
    {
        var (f, i) = await IngredientAsync(id, ingredientId);
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Color dispense revert",
            $"Product type {MaterialTypes.Label(i.Material!.MaterialType)} Product Name {i.Material.ProductName} with Product code {i.Material.ProductCode} color dispense revert",
            $"{G(i.DispenseAmount)},{G(i.DispensedGrams)}", "-");
        i.DispenseAmount = 0;
        i.DispensedGrams = 0;
        i.IsDispensed = false;
        await db.SaveChangesAsync();
        return Ok(new { message = "Dispense amounts cleared." });
    }

    // ------------------------------------------------------------------ recalc / reweigh

    /// <summary>
    /// POST /api/formulas/{id}/recalc { ingredientId, grams } — "Calc Batch" on a base: the weighed grams of that base rescale
    /// every ingredient; the base itself is marked dispensed with its new weight (legacy processBatch + dispense).
    /// </summary>
    [HttpPost("{id:int}/recalc")]
    public async Task<IActionResult> Recalc(int id, [FromBody] RecalcRequest req)
    {
        var f = await LoadAsync(id);
        if (!FormulaRules.CanCalcBatch(me))
            throw new ApiException(StatusCodes.Status403Forbidden, "Calc Batch is only available to System Administrators, FG Pro and FG Pro Plus users.");
        FormulaRules.EnsureCanChangeIngredients(me, f);
        if (f.Ingredients.Any(i => i.DispensedGrams > 0)) throw ApiException.Bad(DispensedFirst);
        var row = f.Ingredients.FirstOrDefault(i => i.Id == req.IngredientId) ?? throw ApiException.NotFound("Ingredient");
        if (row.Material!.MaterialType != MaterialType.Base) throw ApiException.Bad("Calc Batch is only available for Base materials.");
        if (req.Grams <= 0 || req.Grams > 100_000_000) throw ApiException.Bad("Please enter a valid recalc amount in grams!");
        if (row.Grams <= 0) throw ApiException.Bad("This material has no grams to recalculate from.");
        if (row.Material.Density <= 0) throw ApiException.Bad("No density for material");
        var batch = row.BatchNumber ?? InventoryBatches.Chosen((await InventoryBatches.ForMaterialsAsync(db, [row.MaterialId])).GetValueOrDefault(row.MaterialId), null)?.BatchNumber;
        if (batch != null)
        {
            var onHand = await InventoryBatches.OnHandAsync(db, row.MaterialId, batch);
            if (FormulaCalc.Gallons(req.Grams, row.Material.Density) > onHand) throw ApiException.Bad("Not enough material in the batches");
        }

        var oldTotal = f.Ingredients.Sum(i => i.Grams);
        var coef = req.Grams / row.Grams;
        foreach (var i in f.Ingredients)
        {
            i.Grams = FormulaCalc.R(i.Grams * coef, 4);
            i.DispenseAmount = i.Grams;
        }
        row.Grams = req.Grams;
        row.DispensedGrams = req.Grams;
        row.DispenseAmount = 0;
        row.IsDispensed = true;
        UpdateTotals(f);
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Recalc batch",
            $"{Describe(row.Material)} weighed {G(req.Grams)} g (factor × {coef.ToString("0.####", CultureInfo.InvariantCulture)})", G(oldTotal), G(f.BatchSize));
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula was successfully recalculated." });
    }

    /// <summary>POST /api/formulas/{id}/reweigh { totalGrams } — "Reweigh Formula (g)" → GO: every ingredient is rescaled to the new total.</summary>
    [HttpPost("{id:int}/reweigh")]
    public async Task<IActionResult> Reweigh(int id, [FromBody] ReweighRequest req)
    {
        var f = await LoadAsync(id);
        FormulaRules.EnsureCanChangeIngredients(me, f);
        if (f.Ingredients.Any(i => i.DispensedGrams > 0)) throw ApiException.Bad(DispensedFirst);
        if (req.TotalGrams <= 0 || req.TotalGrams > 100_000_000) throw ApiException.Bad("No valid weight entered for batch");
        var total = f.Ingredients.Sum(i => i.Grams);
        if (total <= 0) throw ApiException.Bad("Add materials to the formula before reweighing it.");

        var oldValue = f.BatchValue;
        var coef = req.TotalGrams / total;
        foreach (var i in f.Ingredients)
        {
            i.Grams = FormulaCalc.R(i.Grams * coef, 4);
            i.DispenseAmount = i.Grams;
        }
        // Rounding remainder goes to the largest ingredient so the total is exact.
        var diff = req.TotalGrams - f.Ingredients.Sum(i => i.Grams);
        if (diff != 0)
        {
            var big = f.Ingredients.OrderByDescending(i => i.Grams).First();
            big.Grams += diff;
            big.DispenseAmount = big.Grams;
        }
        UpdateTotals(f);
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Reweight batch", $"Rewighted batch value with {G(f.BatchValue)}", G(oldValue), G(f.BatchValue));
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula Updated" });
    }

    // ------------------------------------------------------------------ record & reset

    /// <summary>
    /// POST /api/formulas/{id}/record { locationId } — "Record &amp; Reset" (legacy RecordColorFormula): for every ingredient with a
    /// batch (the saved one, else the latest batch) the Amount to Dispense — or, when that is 0, the Total Dispensed — is deducted
    /// from inventory (gallons, at the selected location); then every row is reset (nothing dispensed, nothing to dispense).
    /// </summary>
    [HttpPost("{id:int}/record")]
    public async Task<IActionResult> Record(int id, [FromBody] RecordRequest req)
    {
        var f = await LoadAsync(id);
        if (!f.Ingredients.Any(i => i.DispensedGrams > 0)) throw ApiException.Bad("No materials have Dispensed Amounts.");

        var locations = await db.MaterialLocations.AsNoTracking()
            .Where(l => l.GroupId == f.GroupId && !l.IsDeleted && LocationTypes.Contains(l.MaterialType)).Select(l => new { l.Id, l.Name }).ToListAsync();
        if (locations.Count > 0 && req.LocationId == null)
            throw ApiException.Bad("Location is required when recording a dispense. Please select a location from the dropdown above!");
        if (req.LocationId != null && locations.All(l => l.Id != req.LocationId)) throw ApiException.Bad("The selected location no longer exists.");

        var rows = f.Ingredients.OrderBy(i => i.Sequence).ToList();
        var batches = await InventoryBatches.ForMaterialsAsync(db, rows.Select(i => i.MaterialId));
        var noBatch = new List<string>();
        var shortOf = new List<string>();
        var moves = new List<(FormulaIngredient Ingredient, string Batch, decimal Grams, decimal Gallons)>();
        foreach (var i in rows)
        {
            var batch = !string.IsNullOrEmpty(i.BatchNumber) ? i.BatchNumber : InventoryBatches.Chosen(batches.GetValueOrDefault(i.MaterialId), null)?.BatchNumber;
            if (batch == null)
            {
                noBatch.Add(i.Material!.ProductName);
                continue;
            }
            var grams = i.DispenseAmount > 0 ? i.DispenseAmount : i.DispensedGrams;
            var gallons = FormulaCalc.R(FormulaCalc.Gallons(grams, i.Material!.Density), 4);
            if (gallons <= 0) continue; // nothing to deduct, or no density: inventory cannot be updated for this material
            var onHand = await InventoryBatches.OnHandAsync(db, i.MaterialId, batch);
            if (gallons > onHand) shortOf.Add(i.Material.ProductName);
            else moves.Add((i, batch, grams, gallons));
        }
        if (shortOf.Count > 0)
            throw ApiException.Bad($"Please verify you have enough material in batches to complete this operation!\nMaterial(s): {string.Join(", ", shortOf)}");

        var location = locations.FirstOrDefault(l => l.Id == req.LocationId)?.Name;
        foreach (var (i, batch, grams, gallons) in moves)
        {
            db.InventoryTransactions.Add(new InventoryTransaction
            {
                GroupId = f.GroupId, MaterialId = i.MaterialId, LocationId = req.LocationId, BatchNumber = batch, Quantity = -gallons,
                Reason = $"Dispensed: formula #{f.Id} {f.Name}".Length > 400 ? $"Dispensed: formula #{f.Id}" : $"Dispensed: formula #{f.Id} {f.Name}",
                CustomerName = f.CustomerName, FormulaId = f.Id, CreatedBy = me.Id,
            });
            audit.Log(LinkEntityTypes.Material, i.MaterialId, "Inventory adjusted",
                $"-{gallons.ToString("0.####", CultureInfo.InvariantCulture)} gal ({G(grams)} g) dispensed for formula #{f.Id} {f.Name}\nBatch: {batch}"
                + (location == null ? "" : $"\nLocation: {location}"), f.GroupId);
        }

        var recorded = moves.ToDictionary(m => m.Ingredient.Id);
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Record and Reset",
            "Record and Reset Pressed\n" + string.Join("\n", rows.Select(i =>
                $"Product type {MaterialTypes.Label(i.Material!.MaterialType)} Product Name {i.Material.ProductName} Product Code {i.Material.ProductCode} Dispensed amount {G(i.DispensedGrams)}"
                + (recorded.TryGetValue(i.Id, out var mv) ? $" recorded {G(mv.Grams)} g from batch {mv.Batch}" : " (not recorded)"))));

        // Legacy reset: nothing dispensed and nothing left to dispense.
        foreach (var i in f.Ingredients)
        {
            i.IsDispensed = false;
            i.DispensedGrams = 0;
            i.DispenseAmount = 0;
        }
        db.FormulaDispenseSnapshots.RemoveRange(await db.FormulaDispenseSnapshots.Where(s => s.FormulaId == f.Id).ToListAsync());
        await db.SaveChangesAsync();
        return Ok(new
        {
            message = "Successfully recorded.",
            warning = noBatch.Count == 0 ? null : $"No batch selected for {string.Join(", ", noBatch)}... so no batch/inventory will be updated for that material.",
            recorded = moves.Count,
        });
    }

    // ------------------------------------------------------------------ dispense machine

    /// <summary>
    /// POST /api/formulas/{id}/dispense { deviceId } — "Dispense": validates exactly like the legacy page and queues the job for the
    /// dispense machine's network bridge. Poll <c>GET /api/dispensing/commands/{commandId}</c>; on success the amounts to dispense
    /// move into Total Dispensed (and "Undo Dispense" becomes available).
    /// </summary>
    [HttpPost("{id:int}/dispense")]
    public async Task<IActionResult> Dispense(int id, [FromBody] DispenseRequest req)
    {
        var f = await LoadAsync(id);
        if (req.DeviceId is not { } deviceId) throw ApiException.Bad("Please select a dispenser device to continue.");
        var device = await db.Devices.Include(d => d.Canisters)
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.GroupId == f.GroupId && !d.IsArchived && d.DeviceType == DeviceType.DispenseMachine)
            ?? throw ApiException.Bad("Please select a dispenser device to continue.");

        var setting = await db.DispenseSettings.AsNoTracking().FirstOrDefaultAsync(s => s.GroupId == f.GroupId);
        if (DispensingController.CleaningRequired(setting, await DispensingController.LastDispensedAtAsync(db, f.GroupId, setting)))
            throw new ApiException(StatusCodes.Status409Conflict, "Please confirm that the nozzle cleaning is completed before dispensing.");
        if (await db.DeviceCommands.AnyAsync(c => c.FormulaId == f.Id && c.CommandType == DeviceCommandTypes.Dispense
                && (c.Status == DeviceCommandStatuses.Pending || c.Status == DeviceCommandStatuses.Sent) && c.ExpiresAt > DateTime.UtcNow))
            throw ApiException.Bad("A dispense is already in progress for this formula.");

        // Only pigments and dyes are dispensed by the machine (bases are weighed manually).
        var items = f.Ingredients.Where(i => i.DispenseAmount > 0 && i.Material!.MaterialType is MaterialType.Pigment or MaterialType.Dye)
            .OrderBy(i => i.Sequence).ToList();
        var batches = await InventoryBatches.ForMaterialsAsync(db, items.Select(i => i.MaterialId));
        var notConfigured = new List<string>();
        var canisters = new List<object>();
        decimal maxGrams = 0;
        foreach (var i in items)
        {
            var m = i.Material!;
            var label = $"{m.ProductName} ({m.ProductCode})";
            if (!batches.TryGetValue(i.MaterialId, out var list) || list.Count == 0) throw ApiException.Bad($"No Inventory Setup for {label}");
            // The saved batch, otherwise the latest batch (legacy default).
            var batch = InventoryBatches.Chosen(list, i.BatchNumber);
            if (batch == null) throw ApiException.Bad($"Not Enough Material for {label}");
            var gallons = FormulaCalc.Gallons(i.DispenseAmount, m.Density);
            if ((m.MinQuantity > 0 && batch.OnHand < m.MinQuantity) || gallons > batch.OnHand) throw ApiException.Bad($"Not Enough Material for {label}");
            var can = device.Canisters.FirstOrDefault(c => c.MaterialId == i.MaterialId);
            if (can == null)
            {
                notConfigured.Add(label);
                continue;
            }
            maxGrams = Math.Max(maxGrams, i.DispenseAmount);
            canisters.Add(new
            {
                can.CanisterNo, MaterialId = m.Id, m.ProductCode, m.ProductName, Grams = FormulaCalc.R(i.DispenseAmount, 4), m.Density,
                ColorCode = string.IsNullOrWhiteSpace(m.ColorCode) ? "#000000" : m.ColorCode, BatchNumber = batch.BatchNumber,
            });
        }
        if (notConfigured.Count > 0)
            throw ApiException.Bad("The following material(s) are not configured in the selected dispenser machine:\n" + string.Join("\n", notConfigured.Select(n => $" · {n}")));
        if (canisters.Count == 0) throw ApiException.Bad("There is nothing to Dispense.");

        // Legacy UDCP timeout: one unit per started 50 g of the largest canister amount.
        var timeout = (int)Math.Ceiling(maxGrams / 50m);
        var cmd = await commands.QueueAsync(device, DeviceCommandTypes.Dispense, new
        {
            FormulaId = f.Id, FormulaName = f.Name, FormulaNumber = f.Number, DispenserId = device.Id, DispenserName = device.Name,
            device.IpAddress, TimeOut = timeout, Canisters = canisters,
        }, f.Id, me.Id, TimeSpan.FromSeconds(Math.Max(120, timeout * 10 + 120)));
        f.DispenserId = device.Id;
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Dispense requested", $"{device.Name}: {canisters.Count} canister(s)");
        await db.SaveChangesAsync();
        return Ok(new { message = $"Dispense sent to {device.Name}.", commandId = cmd.Id });
    }

    /// <summary>POST /api/formulas/{id}/undo-dispense — "Undo Dispense": restores the amounts from before the last machine dispense.</summary>
    [HttpPost("{id:int}/undo-dispense")]
    public async Task<IActionResult> UndoDispense(int id)
    {
        var f = await LoadAsync(id);
        var snaps = await db.FormulaDispenseSnapshots.Where(s => s.FormulaId == id).ToListAsync();
        if (snaps.Count == 0) throw ApiException.Bad("There is no dispense to undo.");
        foreach (var s in snaps)
        {
            var i = f.Ingredients.FirstOrDefault(x => x.Id == s.IngredientId);
            if (i == null) continue;
            i.Grams = s.Grams;
            i.DispenseAmount = s.DispenseAmount;
            i.DispensedGrams = s.DispensedGrams;
            i.IsDispensed = s.IsDispensed;
        }
        db.FormulaDispenseSnapshots.RemoveRange(snaps);
        UpdateTotals(f);
        FormulaHistory.Add(db, me, f.Id, f.GroupId, "Undo Dispense", "Amounts restored to the state before the last machine dispense");
        await db.SaveChangesAsync();
        return Ok(new { message = "Dispense reverted successfully." });
    }

    // ------------------------------------------------------------------ labels

    /// <summary>
    /// POST /api/formulas/{id}/print-label { printerDeviceId, customerLabel } — "Print Formula Can Label" / "Print Customer Can Label"
    /// on the network label printer (the browser can always print the same layout itself).
    /// </summary>
    [HttpPost("{id:int}/print-label")]
    public async Task<IActionResult> PrintLabel(int id, [FromBody] PrintLabelRequest req)
    {
        var f = await LoadAsync(id);
        var pref = me.Id == 0 ? null : await db.FormulaDevicePreferences.FirstOrDefaultAsync(p => p.UserId == me.Id && p.FormulaId == id);
        var printerId = req.PrinterDeviceId is > 0 ? req.PrinterDeviceId : pref?.PrinterDeviceId;
        if (printerId == null) throw ApiException.Bad(req.CustomerLabel ? "Printer is not selected" : "Please Select a Printer!");
        var printer = await db.Devices.FirstOrDefaultAsync(d => d.Id == printerId && d.GroupId == f.GroupId && !d.IsArchived && d.DeviceType == DeviceType.LabelPrinter)
            ?? throw ApiException.Bad("Please Select a Printer!");
        if (req.CustomerLabel && !f.IsComplete) throw ApiException.Bad("Formula can't be printed because it has \"incomplete\" status.");

        var groupName = await db.Groups.Where(g => g.Id == f.GroupId).Select(g => g.Name).FirstAsync();
        var category = f.CategoryId == null ? null : await db.MaterialCategories.Where(c => c.Id == f.CategoryId).Select(c => c.Name).FirstOrDefaultAsync();
        var cmd = await commands.QueueAsync(printer, DeviceCommandTypes.PrintLabel, LabelData(f, groupName, category, req.CustomerLabel), f.Id, me.Id, TimeSpan.FromMinutes(5));
        if (me.Id != 0 && req.PrinterDeviceId is > 0)
        {
            pref ??= db.FormulaDevicePreferences.Add(new FormulaDevicePreference { UserId = me.Id, FormulaId = f.Id }).Entity;
            pref.PrinterDeviceId = printer.Id;
            pref.UpdatedAt = DateTime.UtcNow;
        }
        FormulaHistory.Add(db, me, f.Id, f.GroupId, req.CustomerLabel ? "Customer can label printed" : "Formula can label printed", printer.Name);
        await db.SaveChangesAsync();
        return Ok(new { message = "Label Printing...", commandId = cmd.Id });
    }

    /// <summary>Content of the legacy PrintLabel view (4×6 in label).</summary>
    private static object LabelData(Formula f, string groupName, string? category, bool customer)
    {
        var lines = f.Ingredients.OrderBy(i => TypeOrder(i.Material!.MaterialType)).ThenBy(i => i.Sequence).Select(i => new
        {
            Type = MaterialTypes.Label(i.Material!.MaterialType), Name = i.Material.ProductName, Number = i.Material.ProductCode,
            Grams = FormulaCalc.R(i.Grams, 1), Lbs = FormulaCalc.R(i.Grams / FormulaCalc.GramsPerPound, 1),
            FlOz = i.Material.Density > 0 ? FormulaCalc.R(FormulaUnits.FlOz(i.Grams, i.Material.Density), 1) : (decimal?)null,
        }).ToList();
        var totalFlOz = f.Ingredients.Sum(i => FormulaUnits.FlOz(i.Grams, i.Material!.Density));
        return new
        {
            GroupName = groupName, FormulaName = f.Name, FormulaNumber = f.Number,
            BatchNumbers = f.Ingredients.Where(i => i.Material!.MaterialType == MaterialType.Base && !string.IsNullOrEmpty(i.BatchNumber) && i.BatchNumber != "0")
                .Select(i => i.BatchNumber!).Distinct().ToList(),
            PurchaseOrderNumber = customer ? f.PurchaseOrderNumber : null, MixedBy = f.EmployeeName, MixedOn = f.MixedOn?.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
            MaterialType = category, MixedQuantityGallons = FormulaCalc.R(totalFlOz / FormulaUnits.FlOzPerGallon, 3), CustomerLabel = customer,
            Lines = customer ? [] : lines,
        };
    }

    private static int TypeOrder(MaterialType t) => t switch { MaterialType.Base => 0, MaterialType.Pigment => 1, MaterialType.Dye => 2, _ => 3 };

    // ------------------------------------------------------------------ history / export

    /// <summary>GET /api/formulas/{id}/history — "View History": UserName, Field, Description, OldValue, ValueAdded, NewValue, Employee Name, Date.</summary>
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> History(int id)
    {
        var f = await LoadAsync(id, ingredients: false);
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == FormulaHistory.EntityType && a.EntityId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(2000)
            .Select(a => new { a.Id, a.UserName, a.Action, a.Details, a.OldValue, a.NewValue, a.CreatedAt }).ToListAsync();
        static decimal? Num(string? v) => decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        return Ok(rows.Select(r => new
        {
            r.Id, UserName = r.UserName ?? "", Field = r.Action, Description = r.Details, r.OldValue,
            ValueAdded = r.OldValue == null && r.NewValue == null ? null
                : ((Num(r.NewValue) ?? 0) - (Num(r.OldValue) ?? 0)).ToString("0.####", CultureInfo.InvariantCulture),
            r.NewValue, EmployeeName = f.EmployeeName, Date = r.CreatedAt,
        }));
    }

    /// <summary>GET /api/formulas/{id}/export — the Excel icon: the colour formula as an .xlsx workbook.</summary>
    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> Export(int id)
    {
        var f = await LoadAsync(id);
        var groupName = await db.Groups.Where(g => g.Id == f.GroupId).Select(g => g.Name).FirstAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Formula");
        ws.Cell(1, 1).Value = "Formula";
        ws.Cell(1, 2).Value = f.Name;
        ws.Cell(2, 1).Value = "Formula #";
        ws.Cell(2, 2).Value = f.Number ?? "";
        ws.Cell(3, 1).Value = "Group";
        ws.Cell(3, 2).Value = groupName;
        ws.Cell(4, 1).Value = "Customer Name";
        ws.Cell(4, 2).Value = f.CustomerName ?? "";
        ws.Range(1, 1, 4, 1).Style.Font.Bold = true;

        string[] headers = ["T", "Material Category", "Prod Name", "Prod #", "Batch #", "Grams", "Fl Oz", "Amount to Dispense (g)", "Total Dispensed (g)"];
        for (var c = 0; c < headers.Length; c++) ws.Cell(6, c + 1).Value = headers[c];
        ws.Range(6, 1, 6, headers.Length).Style.Font.Bold = true;
        ws.Range(6, 1, 6, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
        var r = 7;
        foreach (var i in f.Ingredients.OrderBy(i => TypeOrder(i.Material!.MaterialType)).ThenBy(i => i.Sequence))
        {
            var m = i.Material!;
            ws.Cell(r, 1).Value = MaterialTypes.Label(m.MaterialType)[..1];
            ws.Cell(r, 2).Value = await db.MaterialCategories.Where(c => c.Id == m.CategoryId).Select(c => c.Name).FirstOrDefaultAsync() ?? "";
            ws.Cell(r, 3).Value = m.ProductName;
            ws.Cell(r, 4).Value = m.ProductCode ?? "";
            ws.Cell(r, 5).Value = i.BatchNumber ?? "";
            ws.Cell(r, 6).Value = (double)i.Grams;
            if (m.Density > 0) ws.Cell(r, 7).Value = (double)FormulaCalc.R(FormulaUnits.FlOz(i.Grams, m.Density), 4);
            else ws.Cell(r, 7).Value = "Error";
            ws.Cell(r, 8).Value = (double)i.DispenseAmount;
            ws.Cell(r, 9).Value = (double)i.DispensedGrams;
            r++;
        }
        ws.Cell(r, 5).Value = "Total";
        ws.Cell(r, 6).Value = (double)f.Ingredients.Sum(i => i.Grams);
        ws.Range(r, 5, r, 6).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safe = string.Concat((f.Number ?? f.Name).Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Formula-{safe}.xlsx");
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Formula> LoadAsync(int id, bool ingredients = true)
    {
        IQueryable<Formula> q = db.Formulas;
        if (ingredients) q = q.Include(f => f.Ingredients).ThenInclude(i => i.Material);
        var f = await q.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(f.GroupId);
        return f;
    }

    private async Task<(Formula F, FormulaIngredient I)> IngredientAsync(int id, int ingredientId)
    {
        var f = await LoadAsync(id);
        var i = f.Ingredients.FirstOrDefault(x => x.Id == ingredientId) ?? throw ApiException.NotFound("Ingredient");
        return (f, i);
    }

    private async Task<FormulaDevicePreference> PreferenceAsync(int formulaId)
    {
        if (me.Id == 0) throw ApiException.Bad("Device preferences are saved per user; please sign in again.");
        var p = await db.FormulaDevicePreferences.FirstOrDefaultAsync(x => x.UserId == me.Id && x.FormulaId == formulaId);
        if (p != null) return p;
        p = new FormulaDevicePreference { UserId = me.Id, FormulaId = formulaId };
        db.FormulaDevicePreferences.Add(p);
        return p;
    }

    private static void UpdateTotals(Formula f)
    {
        decimal grams = 0, gallons = 0;
        foreach (var i in f.Ingredients)
        {
            grams += i.Grams;
            gallons += FormulaCalc.Gallons(i.Grams, i.Material?.Density ?? 0);
        }
        f.BatchSize = FormulaCalc.R(grams, 4);
        f.BatchValue = FormulaCalc.R(FormulaUnits.BatchValue(f.BatchType, grams, gallons), 4);
        f.UpdatedAt = DateTime.UtcNow;
    }

    private static string Describe(Material m) => FormulasController.Describe(m.ProductName, m.ProductCode);

    private static string G(decimal v) => FormulaUnits.G(v);
}
