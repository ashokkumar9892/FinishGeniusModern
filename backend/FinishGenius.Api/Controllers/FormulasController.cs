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
/// Formulas ("Formulations"): a header (customer, container, mark-up, colour deltas) plus an ingredient list in grams.
/// Totals are computed by <see cref="FormulaCalc"/>; the React editor mirrors the same maths live.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Formulas)]
[Route("api/formulas")]
public class FormulasController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier) : ControllerBase
{
    private const string AuditType = "Formula";
    private const int MaxIngredients = 250;

    /// <summary>Material types that can be added to a formula.</summary>
    public static readonly MaterialType[] IngredientTypes = [MaterialType.Base, MaterialType.Pigment, MaterialType.Dye, MaterialType.Product];

    public class IngredientInput
    {
        public int MaterialId { get; set; }
        public decimal Grams { get; set; }
        public int? Sequence { get; set; }
    }

    public class FormulaInput
    {
        public int GroupId { get; set; }
        public int? CategoryId { get; set; }
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? CustomerName { get; set; }
        public bool IsComplete { get; set; }
        public decimal BatchSize { get; set; }
        public string? ContainerType { get; set; }
        public decimal ContainerPrice { get; set; }
        public decimal MarkUp { get; set; }
        public string? Substrate { get; set; }
        public string? Notes { get; set; }
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

    public record CopyRequest(string? NewName);
    public record BulkCopyRequest(List<int>? Ids, int DestinationGroupId);

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
            f.Name, f.Number, f.CustomerName, f.IsComplete, f.MarkUp, f.ContainerPrice, f.BatchSize, f.CreatedAt, f.UpdatedAt,
            Lines = f.Ingredients.Select(i => new { i.Grams, i.Material!.Density, i.Material.Price }).ToList(),
        }).AsSplitQuery().ToListAsync();

        return Ok(rows.Select(r =>
        {
            var t = FormulaCalc.Compute(r.Lines.Select(l => new FormulaCalc.Line(l.Grams, l.Density, l.Price, 0, 0, 0)), r.MarkUp, r.ContainerPrice);
            return new
            {
                r.Id, r.GroupId, r.GroupName, r.CategoryId, r.CategoryName, r.Name, r.Number, r.CustomerName, r.IsComplete,
                IngredientCount = r.Lines.Count, t.TotalGrams, t.TotalGallons, Cost = t.MaterialCost, t.Price, r.BatchSize,
                r.CreatedAt, r.UpdatedAt,
            };
        }));
    }

    /// <summary>GET /api/formulas/{id} — full formula with ingredients and computed totals.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var f = await db.Formulas.AsNoTracking()
            .Include(x => x.Group).Include(x => x.Category)
            .Include(x => x.Ingredients).ThenInclude(i => i.Material)
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
                MaterialTypeLabel = MaterialTypes.Label(m.MaterialType), MaterialDeleted = m.IsDeleted,
                m.Density, m.Price, m.Voc, m.Hap, m.Tap, i.Grams, i.Sequence, i.DispensedGrams, i.IsDispensed,
                Gallons = FormulaCalc.R(gallons, 6),
                Cost = FormulaCalc.R(gallons * m.Price, 4),
                Percent = totals.TotalGrams > 0 ? FormulaCalc.R(i.Grams / totals.TotalGrams * 100, 4) : 0,
            };
        }).ToList();

        return Ok(new
        {
            f.Id, f.GroupId, GroupName = f.Group!.Name, f.CategoryId, CategoryName = f.Category?.Name, f.Name, f.Number,
            f.CustomerName, f.IsComplete, f.BatchSize, f.ContainerType, f.ContainerPrice, f.MarkUp, f.Substrate, f.Notes,
            f.SpinDeltaL, f.SpinDeltaA, f.SpinDeltaB, f.SpinDeltaE, f.SpexDeltaL, f.SpexDeltaA, f.SpexDeltaB, f.SpexDeltaE,
            f.CreatedAt, f.UpdatedAt,
            CreatedByName = createdBy == null ? null : Text.Clean($"{createdBy.FirstName} {createdBy.LastName}") ?? createdBy.Username,
            Ingredients = ingredients,
            Totals = totals,
        });
    }

    // ------------------------------------------------------------------ create / update

    /// <summary>POST /api/formulas — header + ingredient list.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FormulaInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var f = new Formula { GroupId = input.GroupId, CreatedBy = me.Id == 0 ? null : me.Id };
        await ApplyAsync(f, input, isNew: true);
        db.Formulas.Add(f);
        await db.SaveChangesAsync();

        var names = await MaterialNamesAsync(f.Ingredients.Select(i => i.MaterialId));
        audit.Log(AuditType, f.Id, "Created",
            $"Name: {f.Name}" + (f.Number == null ? "" : $"\nNumber: {f.Number}") + $"\nStatus: {StatusText(f.IsComplete)}" +
            $"\nIngredients ({f.Ingredients.Count}): " + (f.Ingredients.Count == 0 ? "none" :
                string.Join(", ", f.Ingredients.OrderBy(i => i.Sequence).Select(i => $"{names.GetValueOrDefault(i.MaterialId, $"#{i.MaterialId}")} {G(i.Grams)} g"))),
            f.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula created.", id = f.Id });
    }

    /// <summary>PUT /api/formulas/{id} — header + full ingredient list replace.</summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FormulaInput input)
    {
        var f = await db.Formulas.Include(x => x.Ingredients).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(f.GroupId);
        var changes = await ApplyAsync(f, input, isNew: false);
        f.UpdatedAt = DateTime.UtcNow;
        audit.Log(AuditType, f.Id, "Updated", changes.Count == 0 ? "No changes" : string.Join("\n", changes), f.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula updated.", id = f.Id });
    }

    /// <summary>Validates <paramref name="input"/> and applies it to <paramref name="f"/>; returns a readable change list.</summary>
    private async Task<List<string>> ApplyAsync(Formula f, FormulaInput input, bool isNew)
    {
        var changes = new List<string>();
        var errors = new List<string>();

        var name = Text.Clean(input.Name);
        if (name == null) errors.Add("Formula Name is required.");
        else if (name.Length > 200) errors.Add("Formula Name must be 200 characters or fewer.");
        var number = Text.Clean(input.Number);
        if (number is { Length: > 100 }) errors.Add("Formula Number must be 100 characters or fewer.");
        var customer = Text.Clean(input.CustomerName);
        if (customer is { Length: > 200 }) errors.Add("Customer Name must be 200 characters or fewer.");
        var container = Text.Clean(input.ContainerType);
        if (container is { Length: > 200 }) errors.Add("Container Type must be 200 characters or fewer.");
        var substrate = Text.Clean(input.Substrate);
        if (substrate is { Length: > 200 }) errors.Add("Substrate must be 200 characters or fewer.");
        var notes = Text.Clean(input.Notes);
        if (notes is { Length: > 4000 }) errors.Add("Notes must be 4000 characters or fewer.");
        if (input.BatchSize < 0 || input.BatchSize > 100_000_000) errors.Add("Batch Size must be between 0 and 100,000,000 g.");
        if (input.ContainerPrice < 0 || input.ContainerPrice > 1_000_000) errors.Add("Container price must be between $0 and $1,000,000.");
        if (input.MarkUp < 0 || input.MarkUp > 10_000) errors.Add("Mark-up must be between 0 and 10,000 %.");
        foreach (var (label, v) in new (string, decimal?)[]
                 {
                     ("Spin ΔL", input.SpinDeltaL), ("Spin Δa", input.SpinDeltaA), ("Spin Δb", input.SpinDeltaB), ("Spin ΔE", input.SpinDeltaE),
                     ("Spex ΔL", input.SpexDeltaL), ("Spex Δa", input.SpexDeltaA), ("Spex Δb", input.SpexDeltaB), ("Spex ΔE", input.SpexDeltaE),
                 })
            if (v is < -1000 or > 1000) errors.Add($"{label} must be between -1000 and 1000.");
        if (input.SpinDeltaE < 0 || input.SpexDeltaE < 0) errors.Add("ΔE cannot be negative.");

        // Category: must be a Formula-type category of the same group.
        string? categoryName = null;
        if (input.CategoryId is > 0)
        {
            categoryName = await db.MaterialCategories.Where(c => c.Id == input.CategoryId && c.GroupId == f.GroupId && c.MaterialType == MaterialType.Formula)
                .Select(c => c.Name).FirstOrDefaultAsync();
            if (categoryName == null) errors.Add("The selected Category does not belong to this group.");
        }

        // Ingredients
        var lines = (input.Ingredients ?? []).ToList();
        if (lines.Count > MaxIngredients) errors.Add($"A formula can have at most {MaxIngredients} ingredients.");
        if (lines.Any(l => l.Grams <= 0)) errors.Add("Every ingredient needs a quantity greater than 0 g.");
        if (lines.Any(l => l.Grams > 100_000_000)) errors.Add("Ingredient quantities must be 100,000,000 g or less.");
        if (lines.GroupBy(l => l.MaterialId).Any(g => g.Count() > 1)) errors.Add("Each material can only be added to a formula once.");
        if (input.IsComplete && lines.Count == 0) errors.Add("A formula without ingredients cannot be marked Complete.");

        var materialIds = lines.Select(l => l.MaterialId).Distinct().ToList();
        var existingIds = f.Ingredients.Select(i => i.MaterialId).ToHashSet();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.GroupId, m.ProductName, m.MaterialType, m.IsDeleted }).ToDictionaryAsync(m => m.Id);
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
                var b = Show(after);
                if (a != b) changes.Add($"{label}: {(a == "" ? "(empty)" : a)} → {(b == "" ? "(empty)" : b)}");
            }
            var oldCategory = f.CategoryId == null ? null : await db.MaterialCategories.Where(c => c.Id == f.CategoryId).Select(c => c.Name).FirstOrDefaultAsync();
            Diff("Name", f.Name, name);
            Diff("Number", f.Number, number);
            Diff("Customer Name", f.CustomerName, customer);
            Diff("Category", oldCategory, categoryName);
            Diff("Status", StatusText(f.IsComplete), StatusText(input.IsComplete));
            Diff("Batch Size (g)", f.BatchSize, input.BatchSize);
            Diff("Container Type", f.ContainerType, container);
            Diff("Container price", f.ContainerPrice, input.ContainerPrice);
            Diff("Mark-up (%)", f.MarkUp, input.MarkUp);
            Diff("Substrate", f.Substrate, substrate);
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
        f.BatchSize = input.BatchSize;
        f.ContainerType = container;
        f.ContainerPrice = input.ContainerPrice;
        f.MarkUp = input.MarkUp;
        f.Substrate = substrate;
        f.Notes = notes;
        f.SpinDeltaL = input.SpinDeltaL;
        f.SpinDeltaA = input.SpinDeltaA;
        f.SpinDeltaB = input.SpinDeltaB;
        f.SpinDeltaE = input.SpinDeltaE;
        f.SpexDeltaL = input.SpexDeltaL;
        f.SpexDeltaA = input.SpexDeltaA;
        f.SpexDeltaB = input.SpexDeltaB;
        f.SpexDeltaE = input.SpexDeltaE;

        // Ingredient replace: keep rows whose material is still present (preserves dispense state), drop the rest, add new ones.
        string Mat(int mid) => materials.TryGetValue(mid, out var m) ? m.ProductName : $"#{mid}";
        var ordered = lines.Select((l, idx) => (l, seq: l.Sequence ?? idx + 1)).OrderBy(x => x.seq).Select((x, idx) => (x.l, seq: idx + 1)).ToList();
        var removedNames = await MaterialNamesAsync(f.Ingredients.Where(i => !materialIds.Contains(i.MaterialId)).Select(i => i.MaterialId));
        foreach (var gone in f.Ingredients.Where(i => !materialIds.Contains(i.MaterialId)).ToList())
        {
            f.Ingredients.Remove(gone);
            db.FormulaIngredients.Remove(gone);
            if (!isNew) changes.Add($"Removed ingredient {removedNames.GetValueOrDefault(gone.MaterialId, $"#{gone.MaterialId}")} ({G(gone.Grams)} g)");
        }
        var reordered = false;
        foreach (var (l, seq) in ordered)
        {
            var row = f.Ingredients.FirstOrDefault(i => i.MaterialId == l.MaterialId);
            if (row == null)
            {
                f.Ingredients.Add(new FormulaIngredient { MaterialId = l.MaterialId, Grams = l.Grams, Sequence = seq });
                if (!isNew) changes.Add($"Added ingredient {Mat(l.MaterialId)} ({G(l.Grams)} g)");
                continue;
            }
            if (row.Grams != l.Grams) changes.Add($"{Mat(l.MaterialId)}: {G(row.Grams)} g → {G(l.Grams)} g");
            if (row.Sequence != seq) reordered = true;
            row.Grams = l.Grams;
            row.Sequence = seq;
        }
        if (reordered && !isNew) changes.Add("Ingredients reordered");
        return changes;
    }

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
        audit.Log(AuditType, f.Id, "Deleted", $"Name: {f.Name}" + (f.Number == null ? "" : $" (#{f.Number})"), f.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula deleted." });
    }

    /// <summary>POST /api/formulas/{id}/copy { newName } — duplicate inside the same group.</summary>
    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id, [FromBody] CopyRequest req)
    {
        var src = await db.Formulas.AsNoTracking().Include(x => x.Ingredients).FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted)
            ?? throw ApiException.NotFound("Formula");
        await me.EnsureGroupAsync(src.GroupId);
        var newName = Text.Req(req.NewName, "New Name");
        if (newName.Length > 200) throw ApiException.Bad("New Name must be 200 characters or fewer.");

        var copy = new Formula
        {
            GroupId = src.GroupId, CategoryId = src.CategoryId, Name = newName, Number = src.Number, CustomerName = src.CustomerName,
            IsComplete = src.IsComplete, BatchSize = src.BatchSize, ContainerType = src.ContainerType, ContainerPrice = src.ContainerPrice,
            MarkUp = src.MarkUp, Substrate = src.Substrate, Notes = src.Notes,
            SpinDeltaL = src.SpinDeltaL, SpinDeltaA = src.SpinDeltaA, SpinDeltaB = src.SpinDeltaB, SpinDeltaE = src.SpinDeltaE,
            SpexDeltaL = src.SpexDeltaL, SpexDeltaA = src.SpexDeltaA, SpexDeltaB = src.SpexDeltaB, SpexDeltaE = src.SpexDeltaE,
            CreatedBy = me.Id == 0 ? null : me.Id,
            Ingredients = src.Ingredients.OrderBy(i => i.Sequence).Select(i => new FormulaIngredient { MaterialId = i.MaterialId, Grams = i.Grams, Sequence = i.Sequence }).ToList(),
        };
        db.Formulas.Add(copy);
        await db.SaveChangesAsync();
        audit.Log(AuditType, copy.Id, "Copied", $"Copied from #{src.Id} {src.Name}", copy.GroupId);
        audit.Log(AuditType, src.Id, "Copied", $"Copied to #{copy.Id} {copy.Name}", src.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Formula copied.", id = copy.Id });
    }

    /// <summary>POST /api/formulas/bulk-copy { ids, destinationGroupId } — administrators.</summary>
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
            .Select(f => new { f.Id, f.GroupId, f.Name }).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.NotFound("One or more selected formulas were");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        var copied = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            copied = await copier.CopyFormulasAsync(ids, dest.Id);
            foreach (var s in sources)
                audit.Log(AuditType, s.Id, "Copied", $"Bulk copied to the {dest.Name} group", s.GroupId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
        return Ok(new { message = $"Successfully copied {copied} formulas to the {dest.Name} group.", copied });
    }

    // ------------------------------------------------------------------ helpers

    private static FormulaCalc.Line ToLine(FormulaIngredient i) =>
        new(i.Grams, i.Material?.Density ?? 0, i.Material?.Price ?? 0, i.Material?.Voc ?? 0, i.Material?.Hap ?? 0, i.Material?.Tap ?? 0);

    private async Task<Dictionary<int, string>> MaterialNamesAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.Materials.AsNoTracking().Where(m => list.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ProductName);
    }

    private static string StatusText(bool complete) => complete ? "Complete" : "Incomplete";

    private static string G(decimal grams) => grams.ToString("0.####", CultureInfo.InvariantCulture);

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

    public static decimal Gallons(decimal grams, decimal density) => density > 0 ? grams / (density * GramsPerPound) : 0;

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
