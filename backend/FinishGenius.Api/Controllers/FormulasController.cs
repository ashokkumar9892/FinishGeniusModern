using System.Globalization;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Formulas ("Formulations"): a header (customer, container, mark-up, colour deltas, employee, PO) plus an ingredient list in
/// grams. Totals are computed by <see cref="FormulaCalc"/>; the React editor mirrors the same maths live. The dispense /
/// batch / device workflow of the legacy Edit page lives in <see cref="FormulaWorkspaceController"/>.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Formulas)]
[Route("api/formulas")]
public class FormulasController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier) : ControllerBase
{
    private const string AuditType = FormulaHistory.EntityType;
    private const int MaxIngredients = 250;

    /// <summary>Material types that can be added to a formula.</summary>
    public static readonly MaterialType[] IngredientTypes = [MaterialType.Base, MaterialType.Pigment, MaterialType.Dye, MaterialType.Product];

    public class IngredientInput
    {
        public int MaterialId { get; set; }
        public decimal Grams { get; set; }
        public int? Sequence { get; set; }
        /// <summary>Batch for a newly added ingredient (existing rows change batch via the workspace endpoint).</summary>
        public string? BatchNumber { get; set; }
    }

    public class FormulaInput
    {
        public int GroupId { get; set; }
        public int? CategoryId { get; set; }
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? CustomerName { get; set; }
        public bool IsComplete { get; set; }
        /// <summary>Ignored: the batch size is the total ingredient weight (legacy GramsInBatch).</summary>
        public decimal BatchSize { get; set; }
        /// <summary>1 Gallons, 2 Grams, 3 Litres, 4 Kilograms, 5 Quarts.</summary>
        public int? BatchType { get; set; }
        public string? ContainerType { get; set; }
        public decimal ContainerPrice { get; set; }
        public decimal MarkUp { get; set; }
        public string? Substrate { get; set; }
        public string? Notes { get; set; }
        public string? EmployeeName { get; set; }
        public string? PurchaseOrderNumber { get; set; }
        public decimal? SpinDeltaL { get; set; }
        public decimal? SpinDeltaA { get; set; }
        public decimal? SpinDeltaB { get; set; }
        public decimal? SpinDeltaE { get; set; }
        public decimal? SpexDeltaL { get; set; }
        public decimal? SpexDeltaA { get; set; }
        public decimal? SpexDeltaB { get; set; }
        public decimal? SpexDeltaE { get; set; }
        public List<IngredientInput>? Ingredients { get; set; }
    }

    public record CopyRequest(string? NewName, string? NewNumber);
    public record BulkCopyRequest(List<int>? Ids, int DestinationGroupId);
    private record IngredientChange(string Action, string Description, string? Old, string? New);

    // ------------------------------------------------------------------ queries

    /// <summary>GET /api/formulas?groupId=2&amp;status=complete&amp;search=oak — list rows with cost and price.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? groupId, [FromQuery] string? status, [FromQuery] string? search)
    {
        var scope = await me.ScopeAsync(groupId);
        var q = db.Formulas.AsNoTracking().Where(f => scope.Contains(f.GroupId) && !f.IsDeleted);
        switch (status?.Trim().ToLowerInvariant())
        {
            case null or "" or "all": break;
            case "complete": q = q.Where(f => f.IsComplete); break;
            case "incomplete": q = q.Where(f => !f.IsComplete); break;
            default: throw ApiException.Bad("Status must be \"complete\" or \"incomplete\".");
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(f => f.Name.Contains(s) || (f.Number != null && f.Number.Contains(s)) || (f.CustomerName != null && f.CustomerName.Contains(s)));
        }

        var rows = await q.OrderByDescending(f => f.Id).Select(f => new
        {
            f.Id, f.GroupId, GroupName = f.Group!.Name, f.CategoryId, CategoryName = f.Category != null ? f.Category.Name : null,
            f.Name, f.Number, f.CustomerName, f.IsComplete, f.MarkUp, f.ContainerPrice, f.BatchSize, f.EmployeeName, f.MixedOn,
            f.CreatedAt, f.UpdatedAt,
            Lines = f.Ingredients.Select(i => new { i.Grams, i.Material!.Density, i.Material.Price }).ToList(),
        }).AsSplitQuery().ToListAsync();

        // "Documents" is highlighted for formulas that have linked documents (legacy HasDocs).
        var withDocs = (await db.DocumentLinks.AsNoTracking().Where(l => l.EntityType == LinkEntityTypes.Formula)
            .Select(l => l.EntityId).Distinct().ToListAsync()).ToHashSet();

        return Ok(rows.Select(r =>
        {
            var t = FormulaCalc.Compute(r.Lines.Select(l => new FormulaCalc.Line(l.Grams, l.Density, l.Price, 0, 0, 0)), r.MarkUp, r.ContainerPrice);
            return new
            {
                r.Id, r.GroupId, r.GroupName, r.CategoryId, r.CategoryName, r.Name, r.Number, r.CustomerName, r.IsComplete,
                IngredientCount = r.Lines.Count, t.TotalGrams, t.TotalGallons, Cost = t.MaterialCost, t.Price, r.BatchSize,
                GramsInBatch = t.TotalGrams, r.EmployeeName, r.MixedOn, HasDocs = withDocs.Contains(r.Id), r.CreatedAt, r.UpdatedAt,
            };
        }));
    }

    /// <summary>GET /api/formulas/{id} — full formula with ingredients and computed totals.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var f = await db.Formulas.AsNoTracking()
            .Include(x => x.Group).Include(x => x.Category)
            .Include(x => x.Ingredients).ThenInclude(i => i.Material).ThenInclude(m => m!.Category)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(f.GroupId);

        var createdBy = f.CreatedBy == null ? null : await db.Users.AsNoTracking().Where(u => u.Id == f.CreatedBy)
            .Select(u => new { u.FirstName, u.LastName, u.Username }).FirstOrDefaultAsync();

        var ordered = f.Ingredients.OrderBy(i => i.Sequence).ThenBy(i => i.Id).ToList();
        var totals = FormulaCalc.Compute(ordered.Select(ToLine), f.MarkUp, f.ContainerPrice);
        var ingredients = ordered.Select(i =>
        {
            var m = i.Material!;
            var gallons = FormulaCalc.Gallons(i.Grams, m.Density);
            return new
            {
                i.Id, i.MaterialId, ProductName = m.ProductName, m.ProductCode, MaterialType = (int)m.MaterialType,
                MaterialTypeLabel = MaterialTypes.Label(m.MaterialType), CategoryName = m.Category?.Name, MaterialDeleted = m.IsDeleted,
                m.Density, m.Price, m.Voc, m.Hap, m.Tap, m.MinQuantity, m.ColorCode, i.Grams, i.Sequence,
                i.DispenseAmount, i.DispensedGrams, i.IsDispensed, i.BatchNumber,
                Gallons = FormulaCalc.R(gallons, 6),
                FlOz = FormulaCalc.R(gallons * FormulaUnits.FlOzPerGallon, 4),
                Cost = FormulaCalc.R(gallons * m.Price, 4),
                Percent = totals.TotalGrams > 0 ? FormulaCalc.R(i.Grams / totals.TotalGrams * 100, 4) : 0,
            };
        }).ToList();

        return Ok(new
        {
            f.Id, f.GroupId, GroupName = f.Group!.Name, GroupLogoFile = f.Group.LogoFile, f.CategoryId, CategoryName = f.Category?.Name,
            f.Name, f.Number, f.CustomerName, f.IsComplete, f.BatchSize, BatchType = (int)f.BatchType, BatchTypeLabel = FormulaUnits.Label(f.BatchType),
            f.BatchValue, f.ContainerType, f.ContainerPrice, f.MarkUp, f.Substrate, f.Notes, f.EmployeeName, f.PurchaseOrderNumber, f.MixedOn,
            f.DispenserId, f.SpinDeltaL, f.SpinDeltaA, f.SpinDeltaB, f.SpinDeltaE, f.SpexDeltaL, f.SpexDeltaA, f.SpexDeltaB, f.SpexDeltaE,
            f.CreatedAt, f.UpdatedAt,
            CreatedByName = createdBy == null ? null : Text.Clean($"{createdBy.FirstName} {createdBy.LastName}") ?? createdBy.Username,
            UsesBatches = await InventoryBatches.GroupUsesBatchesAsync(db, f.GroupId),
            HasUndoDispense = await db.FormulaDispenseSnapshots.AnyAsync(s => s.FormulaId == f.Id),
            Ingredients = ingredients,
            Totals = totals,
        });
    }

    // ------------------------------------------------------------------ create / update

    /// <summary>POST /api/formulas — "Create New Formula" (Group, Product Category, Name, Number) or a full formula.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FormulaInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var f = new Formula { GroupId = input.GroupId, CreatedBy = me.Id == 0 ? null : me.Id };
        await ApplyAsync(f, input, isNew: true);
        f.MixedOn = DateTime.UtcNow;
        db.Formulas.Add(f);
        await db.SaveChangesAsync();

        var names = await MaterialNamesAsync(f.Ingredients.Select(i => i.MaterialId));
        audit.Log(AuditType, f.Id, "Created",
            $"Name: {f.Name}" + (f.Number == null ? "" : $"\nNumber: {f.Number}") + $"\nStatus: {StatusText(f.IsComplete)}" +
            $"\nIngredients ({f.Ingredients.Count}): " + (f.Ingredients.Count == 0 ? "none" :
                string.Join(", ", f.Ingredients.OrderBy(i => i.Sequence).Select(i => $"{names.GetValueOrDefault(i.MaterialId, $"#{i.MaterialId}")} {G(i.Grams)} g"))),
            f.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula Successfully Created.", id = f.Id });
    }

    /// <summary>PUT /api/formulas/{id} — header + full ingredient list replace.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FormulaInput input)
    {
        var f = await db.Formulas.Include(x => x.Ingredients).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(f.GroupId);
        var (changes, ingredientChanges) = await ApplyAsync(f, input, isNew: false);
        f.UpdatedAt = DateTime.UtcNow;
        f.MixedOn = f.UpdatedAt; // legacy: every save stamps "Mixed On"
        if (changes.Count > 0 || ingredientChanges.Count == 0)
            audit.Log(AuditType, f.Id, "Updated", changes.Count == 0 ? "No changes" : string.Join("\n", changes), f.GroupId);
        foreach (var c in ingredientChanges)
            FormulaHistory.Add(db, me, f.Id, f.GroupId, c.Action, c.Description, c.Old, c.New);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula updated.", id = f.Id });
    }

    /// <summary>Validates <paramref name="input"/> and applies it to <paramref name="f"/>; returns readable change lists.</summary>
    private async Task<(List<string> Header, List<IngredientChange> Ingredients)> ApplyAsync(Formula f, FormulaInput input, bool isNew)
    {
        var changes = new List<string>();
        var ingredientChanges = new List<IngredientChange>();
        var errors = new List<string>();

        var name = Text.Clean(input.Name);
        if (name == null) errors.Add("Formula Name is required.");
        else if (name.Length > 200) errors.Add("Formula Name must be 200 characters or fewer.");
        var number = Text.Clean(input.Number);
        if (number == null && (isNew || !string.IsNullOrWhiteSpace(f.Number))) errors.Add("Formula Number is required.");
        if (number is { Length: > 100 }) errors.Add("Formula Number must be 100 characters or fewer.");
        var customer = Text.Clean(input.CustomerName);
        if (customer is { Length: > 200 }) errors.Add("Customer Name must be 200 characters or fewer.");
        var container = Text.Clean(input.ContainerType);
        if (container is { Length: > 200 }) errors.Add("Container Type must be 200 characters or fewer.");
        var substrate = Text.Clean(input.Substrate);
        if (substrate is { Length: > 200 }) errors.Add("Substrate must be 200 characters or fewer.");
        var notes = Text.Clean(input.Notes);
        if (notes is { Length: > 4000 }) errors.Add("Notes must be 4000 characters or fewer.");
        var employee = Text.Clean(input.EmployeeName);
        if (employee is { Length: > 200 }) errors.Add("Employee Name must be 200 characters or fewer.");
        // Legacy step 2 ("Save & Print Formula") required the employee when the formula became Complete.
        if (input.IsComplete && (isNew || !f.IsComplete) && employee == null) errors.Add("Employee Name is required.");
        var po = Text.Clean(input.PurchaseOrderNumber);
        if (po is { Length: > 200 }) errors.Add("Purchase Order # must be 200 characters or fewer.");
        if (input.BatchType is { } bt && !Enum.IsDefined(typeof(FormulaBatchType), bt)) errors.Add("Batch Type is not valid.");
        var batchType = input.BatchType is { } b && Enum.IsDefined(typeof(FormulaBatchType), b) ? (FormulaBatchType)b : isNew ? FormulaBatchType.Grams : f.BatchType;
        if (input.ContainerPrice < 0 || input.ContainerPrice > 1_000_000) errors.Add("Container price must be between $0 and $1,000,000.");
        if (input.MarkUp < 0 || input.MarkUp > 10_000) errors.Add("Mark-up must be between 0 and 10,000 %.");
        foreach (var (label, v) in new (string, decimal?)[]
                 {
                     ("Spin ΔL", input.SpinDeltaL), ("Spin Δa", input.SpinDeltaA), ("Spin Δb", input.SpinDeltaB), ("Spin ΔE", input.SpinDeltaE),
                     ("Spex ΔL", input.SpexDeltaL), ("Spex Δa", input.SpexDeltaA), ("Spex Δb", input.SpexDeltaB), ("Spex ΔE", input.SpexDeltaE),
                 })
            if (v is < -1000 or > 1000) errors.Add($"{label} must be between -1000 and 1000.");
        if (input.SpinDeltaE < 0 || input.SpexDeltaE < 0) errors.Add("ΔE cannot be negative.");

        // Formula number: unique inside the group (legacy "Formula number already exists.").
        if (number != null && (isNew || !string.Equals(number, f.Number?.Trim(), StringComparison.OrdinalIgnoreCase))
            && await NumberTakenAsync(f.GroupId, number, f.Id))
            errors.Add("Formula number already exists.");

        // Category: a Base, Formula or Product category (legacy: MaterialType in 1, 6, 7) of the same group or shared;
        // an unchanged category is always accepted (imported legacy formulas may use other types).
        string? categoryName = null;
        if (input.CategoryId is > 0)
        {
            var unchanged = input.CategoryId == f.CategoryId;
            categoryName = await db.MaterialCategories
                .Where(c => c.Id == input.CategoryId && (c.GroupId == f.GroupId || c.GroupId == null)
                            && (unchanged || c.MaterialType == MaterialType.Formula || c.MaterialType == MaterialType.Base || c.MaterialType == MaterialType.Product))
                .Select(c => c.Name).FirstOrDefaultAsync();
            if (categoryName == null) errors.Add("The selected Category does not belong to this group.");
        }

        // Ingredients
        var lines = (input.Ingredients ?? []).ToList();
        if (lines.Count > MaxIngredients) errors.Add($"A formula can have at most {MaxIngredients} ingredients.");
        if (lines.Any(l => l.Grams <= 0)) errors.Add("A valid quantity (grams) is required for every material.");
        if (lines.Any(l => l.Grams > 100_000_000)) errors.Add("Ingredient quantities must be 100,000,000 g or less.");
        if (lines.GroupBy(l => l.MaterialId).Any(g => g.Count() > 1)) errors.Add("A material can only be added to a formula once.");
        if (input.IsComplete && lines.Count == 0) errors.Add("A formula without ingredients cannot be marked Complete.");

        var materialIds = lines.Select(l => l.MaterialId).Distinct().ToList();
        var existingIds = f.Ingredients.Select(i => i.MaterialId).ToHashSet();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GroupId, m.ProductName, m.ProductCode, m.MaterialType, m.IsDeleted, m.Density })
            .ToDictionaryAsync(m => m.Id);
        foreach (var mid in materialIds)
        {
            if (!materials.TryGetValue(mid, out var m) || m.GroupId != f.GroupId)
            {
                errors.Add("One or more ingredients are not materials of this group.");
                break;
            }
            // Materials already in the formula stay valid even if later deleted or re-typed.
            if (existingIds.Contains(mid)) continue;
            if (m.IsDeleted) errors.Add($"\"{m.ProductName}\" has been deleted and cannot be added.");
            else if (!IngredientTypes.Contains(m.MaterialType))
                errors.Add($"\"{m.ProductName}\" ({MaterialTypes.Label(m.MaterialType)}) cannot be added; only Base, Pigment, Dye and Product materials can be added to a formula.");
        }
        if (errors.Count > 0) throw ApiException.Bad(string.Join("\n", errors.Distinct()));

        // Header diff (for the audit trail)
        if (!isNew)
        {
            void Diff(string label, object? before, object? after)
            {
                var a = Show(before);
                var c = Show(after);
                if (a != c) changes.Add($"{label}: {(a == "" ? "(empty)" : a)} → {(c == "" ? "(empty)" : c)}");
            }
            var oldCategory = f.CategoryId == null ? null : await db.MaterialCategories.Where(c => c.Id == f.CategoryId).Select(c => c.Name).FirstOrDefaultAsync();
            Diff("Name", f.Name, name);
            Diff("Number", f.Number, number);
            Diff("Customer Name", f.CustomerName, customer);
            Diff("Category", oldCategory, categoryName);
            Diff("Status", StatusText(f.IsComplete), StatusText(input.IsComplete));
            Diff("Batch Type", FormulaUnits.Label(f.BatchType), FormulaUnits.Label(batchType));
            Diff("Container Type", f.ContainerType, container);
            Diff("Container price", f.ContainerPrice, input.ContainerPrice);
            Diff("Mark-up (%)", f.MarkUp, input.MarkUp);
            Diff("Substrate", f.Substrate, substrate);
            Diff("Employee Name", f.EmployeeName, employee);
            Diff("Purchase Order #", f.PurchaseOrderNumber, po);
            if ((f.Notes ?? "") != (notes ?? "")) changes.Add("Notes updated");
            Diff("Spin ΔL", f.SpinDeltaL, input.SpinDeltaL);
            Diff("Spin Δa", f.SpinDeltaA, input.SpinDeltaA);
            Diff("Spin Δb", f.SpinDeltaB, input.SpinDeltaB);
            Diff("Spin ΔE", f.SpinDeltaE, input.SpinDeltaE);
            Diff("Spex ΔL", f.SpexDeltaL, input.SpexDeltaL);
            Diff("Spex Δa", f.SpexDeltaA, input.SpexDeltaA);
            Diff("Spex Δb", f.SpexDeltaB, input.SpexDeltaB);
            Diff("Spex ΔE", f.SpexDeltaE, input.SpexDeltaE);
        }

        f.Name = name!;
        f.Number = number;
        f.CustomerName = customer;
        f.CategoryId = input.CategoryId is > 0 ? input.CategoryId : null;
        f.IsComplete = input.IsComplete;
        f.BatchType = batchType;
        f.ContainerType = container;
        f.ContainerPrice = input.ContainerPrice;
        f.MarkUp = input.MarkUp;
        f.Substrate = substrate;
        f.Notes = notes;
        f.EmployeeName = employee;
        f.PurchaseOrderNumber = po;
        f.SpinDeltaL = input.SpinDeltaL;
        f.SpinDeltaA = input.SpinDeltaA;
        f.SpinDeltaB = input.SpinDeltaB;
        f.SpinDeltaE = input.SpinDeltaE;
        f.SpexDeltaL = input.SpexDeltaL;
        f.SpexDeltaA = input.SpexDeltaA;
        f.SpexDeltaB = input.SpexDeltaB;
        f.SpexDeltaE = input.SpexDeltaE;

        // Ingredient replace: keep rows whose material is still present (preserves dispense state), drop the rest, add new ones.
        string Mat(int mid) => materials.TryGetValue(mid, out var m) ? Describe(m.ProductName, m.ProductCode) : $"#{mid}";
        var ordered = lines.Select((l, idx) => (l, seq: l.Sequence ?? idx + 1)).OrderBy(x => x.seq).Select((x, idx) => (x.l, seq: idx + 1)).ToList();
        var removed = f.Ingredients.Where(i => !materialIds.Contains(i.MaterialId)).ToList();
        var removedNames = await db.Materials.AsNoTracking().Where(m => removed.Select(r => r.MaterialId).Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => Describe(m.ProductName, m.ProductCode));
        foreach (var gone in removed)
        {
            f.Ingredients.Remove(gone);
            db.FormulaIngredients.Remove(gone);
            if (!isNew) ingredientChanges.Add(new("Material Removed", removedNames.GetValueOrDefault(gone.MaterialId, $"#{gone.MaterialId}"), G(gone.Grams), "-"));
        }
        var reordered = false;
        foreach (var (l, seq) in ordered)
        {
            var row = f.Ingredients.FirstOrDefault(i => i.MaterialId == l.MaterialId);
            if (row == null)
            {
                // Legacy: a new material starts with Amount to Dispense = its grams.
                f.Ingredients.Add(new FormulaIngredient
                {
                    MaterialId = l.MaterialId, Grams = l.Grams, DispenseAmount = l.Grams, Sequence = seq,
                    BatchNumber = Text.Clean(l.BatchNumber),
                });
                if (!isNew) ingredientChanges.Add(new("Original formula", Mat(l.MaterialId), "-", G(l.Grams)));
                continue;
            }
            if (row.Grams != l.Grams)
            {
                // Legacy: the added (or removed) grams are added to the Amount to Dispense.
                ingredientChanges.Add(new(l.Grams > row.Grams ? "Material added" : "Material modified", Mat(l.MaterialId), G(row.Grams), G(l.Grams)));
                row.DispenseAmount = Math.Max(0, row.DispenseAmount + (l.Grams - row.Grams));
            }
            if (row.Sequence != seq) reordered = true;
            row.Grams = l.Grams;
            row.Sequence = seq;
        }
        if (reordered && !isNew) changes.Add("Ingredients reordered");

        // Batch size = total formula weight; Batch Value = the same batch in the selected Batch Type unit.
        decimal grams = 0, gallons = 0;
        foreach (var i in f.Ingredients)
        {
            grams += i.Grams;
            if (materials.TryGetValue(i.MaterialId, out var m)) gallons += FormulaCalc.Gallons(i.Grams, m.Density);
        }
        f.BatchSize = FormulaCalc.R(grams, 4);
        f.BatchValue = FormulaCalc.R(FormulaUnits.BatchValue(batchType, grams, gallons), 4);
        return (changes, ingredientChanges);
    }

    private Task<bool> NumberTakenAsync(int groupId, string number, int exceptId) =>
        db.Formulas.AnyAsync(x => x.GroupId == groupId && !x.IsDeleted && x.Id != exceptId && x.Number != null && x.Number.Trim() == number);

    // ------------------------------------------------------------------ delete / copy

    /// <summary>DELETE /api/formulas/{id} — soft delete (administrators).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        me.EnsureAdmin();
        var f = await db.Formulas.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(f.GroupId);
        f.IsDeleted = true;
        f.UpdatedAt = DateTime.UtcNow;
        // Imported formulations also exist as materials (type Formula, same id) so process steps can pick them; they go too.
        var mirror = await db.Materials.FirstOrDefaultAsync(m => m.Id == f.Id && m.GroupId == f.GroupId && m.MaterialType == MaterialType.Formula && !m.IsDeleted);
        if (mirror != null)
        {
            mirror.IsDeleted = true;
            mirror.UpdatedAt = f.UpdatedAt;
        }
        audit.Log(AuditType, f.Id, "Deleted", $"Name: {f.Name}" + (f.Number == null ? "" : $" (#{f.Number})"), f.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula deleted." });
    }

    /// <summary>POST /api/formulas/{id}/copy { newName, newNumber } — "Copy to New" inside the same group (documents stay linked).</summary>
    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id, [FromBody] CopyRequest req)
    {
        var src = await db.Formulas.AsNoTracking().Include(x => x.Ingredients).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(src.GroupId);
        var errors = new List<string>();
        var newName = Text.Clean(req.NewName);
        var newNumber = Text.Clean(req.NewNumber);
        if (newName == null) errors.Add("New Name is required.");
        else if (newName.Length > 200) errors.Add("New Name must be 200 characters or fewer.");
        if (newNumber == null) errors.Add("New Number is required.");
        else if (newNumber.Length > 100) errors.Add("New Number must be 100 characters or fewer.");
        else if (await NumberTakenAsync(src.GroupId, newNumber, 0)) errors.Add("Formula number already exists.");
        if (errors.Count > 0) throw ApiException.Bad(string.Join("\n", errors));

        var copy = new Formula
        {
            GroupId = src.GroupId, CategoryId = src.CategoryId, Name = newName!, Number = newNumber, CustomerName = src.CustomerName,
            IsComplete = src.IsComplete, BatchSize = src.BatchSize, BatchType = src.BatchType, BatchValue = src.BatchValue,
            ContainerType = src.ContainerType, ContainerPrice = src.ContainerPrice, MarkUp = src.MarkUp, Substrate = src.Substrate, Notes = src.Notes,
            EmployeeName = src.EmployeeName, PurchaseOrderNumber = src.PurchaseOrderNumber, MixedOn = DateTime.UtcNow, DispenserId = src.DispenserId,
            SpinDeltaL = src.SpinDeltaL, SpinDeltaA = src.SpinDeltaA, SpinDeltaB = src.SpinDeltaB, SpinDeltaE = src.SpinDeltaE,
            SpexDeltaL = src.SpexDeltaL, SpexDeltaA = src.SpexDeltaA, SpexDeltaB = src.SpexDeltaB, SpexDeltaE = src.SpexDeltaE,
            CreatedBy = me.Id == 0 ? null : me.Id,
            Ingredients = src.Ingredients.OrderBy(i => i.Sequence).Select(i => new FormulaIngredient
            {
                MaterialId = i.MaterialId, Grams = i.Grams, DispenseAmount = i.Grams, Sequence = i.Sequence, BatchNumber = i.BatchNumber,
            }).ToList(),
        };
        db.Formulas.Add(copy);
        await db.SaveChangesAsync();

        // Legacy copied the formula's documents too; documents are shared per group, so the copy is simply linked to them.
        var docIds = await db.DocumentLinks.Where(l => l.EntityType == LinkEntityTypes.Formula && l.EntityId == src.Id).Select(l => l.DocumentId).Distinct().ToListAsync();
        foreach (var docId in docIds)
            db.DocumentLinks.Add(new DocumentLink { DocumentId = docId, EntityType = LinkEntityTypes.Formula, EntityId = copy.Id });

        audit.Log(AuditType, copy.Id, "Copied", $"Copied from #{src.Id} {src.Name}" + (docIds.Count > 0 ? $" ({docIds.Count} document(s) linked)" : ""), copy.GroupId);
        audit.Log(AuditType, src.Id, "Copied", $"Copied to #{copy.Id} {copy.Name}", src.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formulation Copied.", id = copy.Id });
    }

    /// <summary>
    /// POST /api/formulas/bulk-copy { ids, destinationGroupId } — administrators. Formulas whose number already exists in the
    /// destination group are skipped (legacy "Unable to copy N duplicate …").
    /// </summary>
    [HttpPost("bulk-copy")]
    public async Task<IActionResult> BulkCopy([FromBody] BulkCopyRequest req)
    {
        me.EnsureAdmin();
        var ids = (req.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Please select at least one formula to copy.");
        if (ids.Count > 1000) throw ApiException.Bad("You can copy at most 1,000 formulas at a time.");
        await me.EnsureGroupAsync(req.DestinationGroupId);
        var dest = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == req.DestinationGroupId && !g.IsDeleted)
            ?? throw ApiException.NotFound("Destination group");

        var sources = await db.Formulas.AsNoTracking().Where(f => ids.Contains(f.Id) && !f.IsDeleted)
            .Select(f => new { f.Id, f.GroupId, f.Name, f.Number }).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.NotFound("One or more selected formulas were");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        static string Key(string? n) => (n ?? "").Trim().ToLowerInvariant();
        var taken = (await db.Formulas.AsNoTracking().Where(f => f.GroupId == dest.Id && !f.IsDeleted && f.Number != null)
            .Select(f => f.Number!).ToListAsync()).Select(Key).ToHashSet();
        var toCopy = new List<int>();
        var duplicates = 0;
        foreach (var s in sources.OrderBy(s => s.Id))
        {
            if (!string.IsNullOrWhiteSpace(s.Number) && !taken.Add(Key(s.Number))) duplicates++;
            else toCopy.Add(s.Id);
        }

        var copied = 0;
        if (toCopy.Count > 0)
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await db.Database.BeginTransactionAsync();
                copied = await copier.CopyFormulasAsync(toCopy, dest.Id);
                foreach (var s in sources.Where(s => toCopy.Contains(s.Id)))
                    audit.Log(AuditType, s.Id, "Copied", $"Bulk copied to the {dest.Name} group", s.GroupId);
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            });
        }
        var message = $"Successfully copied {copied} {(copied == 1 ? "formula" : "formulas")} to the {dest.Name} group.";
        if (duplicates > 0)
            message += $"\nUnable to copy {duplicates} duplicate {(duplicates == 1 ? "formula" : "formulas")} (Formula number already exists).";
        return Ok(new { message, copied, duplicates });
    }

    // ------------------------------------------------------------------ helpers

    private static FormulaCalc.Line ToLine(FormulaIngredient i) =>
        new(i.Grams, i.Material?.Density ?? 0, i.Material?.Price ?? 0, i.Material?.Voc ?? 0, i.Material?.Hap ?? 0, i.Material?.Tap ?? 0);

    public static string Describe(string name, string? code) => string.IsNullOrWhiteSpace(code) ? name : $"{name} - {code}";

    private async Task<Dictionary<int, string>> MaterialNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Materials.AsNoTracking().Where(m => list.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ProductName);
    }

    private static string StatusText(bool complete) => complete ? "Complete" : "Incomplete";

    private static string G(decimal grams) => FormulaUnits.G(grams);

    private static string Show(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };
}

/// <summary>
/// Formula maths (mirrored in frontend/src/pages/formulas/formulaMath.ts):
/// gallons = grams / (density lb/gal × 453.59237); cost = gallons × $/gal; weighted VOC/HAP/TAP = Σ(gallons × x) / Σ gallons;
/// price = cost × (1 + markUp / 100) + container price.
/// </summary>
public static class FormulaCalc
{
    public const decimal GramsPerPound = 453.59237m;

    public record Line(decimal Grams, decimal Density, decimal Price, decimal Voc, decimal Hap, decimal Tap);

    public record Totals(
        decimal TotalGrams, decimal TotalPounds, decimal TotalGallons, decimal MaterialCost, decimal Price,
        decimal Voc, decimal Hap, decimal Tap, decimal CostPerGallon, decimal PricePerGallon, int IngredientCount);

    /// <summary>Density is lb/gal; values below 3 are legacy g/cc data (the legacy column was "GramsPerCubicCentiMetres").</summary>
    public static decimal Gallons(decimal grams, decimal density) =>
        density > 0 ? grams / (density * (density < 3 ? 3785.41m : GramsPerPound)) : 0;

    public static decimal R(decimal v, int digits) => Math.Round(v, digits, MidpointRounding.AwayFromZero);

    public static Totals Compute(IEnumerable<Line> lines, decimal markUp, decimal containerPrice)
    {
        decimal grams = 0, gallons = 0, cost = 0, voc = 0, hap = 0, tap = 0;
        var count = 0;
        foreach (var l in lines)
        {
            var gal = Gallons(l.Grams, l.Density);
            grams += l.Grams;
            gallons += gal;
            cost += gal * l.Price;
            voc += gal * l.Voc;
            hap += gal * l.Hap;
            tap += gal * l.Tap;
            count++;
        }
        var price = cost * (1 + markUp / 100m) + containerPrice;
        return new Totals(
            R(grams, 4), R(grams / GramsPerPound, 4), R(gallons, 6), R(cost, 4), R(price, 4),
            gallons > 0 ? R(voc / gallons, 4) : 0, gallons > 0 ? R(hap / gallons, 4) : 0, gallons > 0 ? R(tap / gallons, 4) : 0,
            gallons > 0 ? R(cost / gallons, 4) : 0, gallons > 0 ? R(price / gallons, 4) : 0, count);
    }
}
