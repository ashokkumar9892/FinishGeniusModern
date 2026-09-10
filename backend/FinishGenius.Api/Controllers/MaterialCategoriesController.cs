using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Material categories and their characteristics (the inputs shown when a category is picked inside a process sub step).
/// Reads are open to every signed-in user (process builders need them); writes are Group/System admins.
/// </summary>
[ApiController]
[Authorize]
[Route("api/material-categories")]
public class MaterialCategoriesController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    public record CategoryInput(int GroupId, string? Name, MaterialType MaterialType, string? Filter1, string? Filter2);

    public record CharacteristicInput(string? Name, string? Unit, string? InputType, string? CalcVariable, string? DefaultValue);

    public record ReorderInput(List<int> Ids);

    private const string Entity = "MaterialCategory";

    /// <summary>GET /api/material-categories?groupId=1&amp;type=4 — categories with their characteristics (ordered by sequence).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] MaterialType? type)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.MaterialCategories.AsNoTracking().Where(c => c.GroupId == groupId);
        if (type != null) q = q.Where(c => c.MaterialType == type);
        var rows = await q.Include(c => c.Characteristics).OrderBy(c => c.Name).ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.Materials.AsNoTracking()
            .Where(m => m.CategoryId != null && ids.Contains(m.CategoryId.Value) && !m.IsDeleted)
            .GroupBy(m => m.CategoryId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return Ok(rows.Select(c => new
        {
            c.Id, c.GroupId, c.Name, MaterialType = (int)c.MaterialType, MaterialTypeLabel = MaterialTypes.Label(c.MaterialType),
            c.Filter1, c.Filter2,
            MaterialCount = counts.GetValueOrDefault(c.Id),
            Characteristics = c.Characteristics.OrderBy(x => x.Sequence).Select(x => new
            {
                x.Id, x.CategoryId, x.Name, x.Unit, x.InputType, x.CalcVariable, x.DefaultValue, x.Sequence,
            }),
        }));
    }

    // ---------------------------------------------------------------- categories

    [HttpPost]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> Create(CategoryInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var c = new MaterialCategory { GroupId = input.GroupId };
        await ApplyAsync(c, input);
        db.MaterialCategories.Add(c);
        await db.SaveChangesAsync();
        audit.Log(Entity, c.Id, "Created", $"{MaterialTypes.Label(c.MaterialType)} category \"{c.Name}\"", c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Category created.", id = c.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> Update(int id, CategoryInput input)
    {
        var c = await LoadCategory(id);
        if (input.MaterialType != c.MaterialType &&
            await db.Materials.AnyAsync(m => m.CategoryId == id && !m.IsDeleted))
            throw ApiException.Bad("The Material Type cannot be changed while materials use this category.");
        var old =(c.Name, Type: MaterialTypes.Label(c.MaterialType), c.Filter1, c.Filter2);
        await ApplyAsync(c, input with { GroupId = c.GroupId });
        var changes = new List<string>();
        if (old.Name != c.Name) changes.Add($"Name: {old.Name} → {c.Name}");
        if (old.Type != MaterialTypes.Label(c.MaterialType)) changes.Add($"Material Type: {old.Type} → {MaterialTypes.Label(c.MaterialType)}");
        if (old.Filter1 != c.Filter1) changes.Add($"Filter1: {old.Filter1 ?? "(blank)"} → {c.Filter1 ?? "(blank)"}");
        if (old.Filter2 != c.Filter2) changes.Add($"Filter2: {old.Filter2 ?? "(blank)"} → {c.Filter2 ?? "(blank)"}");
        audit.Log(Entity, c.Id, "Updated", changes.Count == 0 ? "No changes." : string.Join("\n", changes), c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Category updated.", id = c.Id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await LoadCategory(id);
        var materials = await db.Materials.CountAsync(m => m.CategoryId == id && !m.IsDeleted);
        if (materials > 0)
            throw ApiException.Bad($"The \"{c.Name}\" category is used by {materials} material{(materials == 1 ? "" : "s")} and cannot be deleted. Move or delete those materials first.");
        var formulas = await db.Formulas.CountAsync(f => f.CategoryId == id && !f.IsDeleted);
        if (formulas > 0)
            throw ApiException.Bad($"The \"{c.Name}\" category is used by {formulas} formula{(formulas == 1 ? "" : "s")} and cannot be deleted.");
        var steps = await db.ProcessStepEntries.CountAsync(e => e.CategoryId == id);
        if (steps > 0)
            throw ApiException.Bad($"The \"{c.Name}\" category is used in {steps} process step entr{(steps == 1 ? "y" : "ies")} and cannot be deleted.");

        // Soft-deleted materials / formulas may still point at the category: detach them so the FK allows the delete.
        await db.Materials.Where(m => m.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.CategoryId, (int?)null));
        await db.Formulas.Where(f => f.CategoryId == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.CategoryId, (int?)null));
        db.MaterialCategories.Remove(c);
        audit.Log(Entity, c.Id, "Deleted", $"\"{c.Name}\"", c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Category deleted." });
    }

    // ---------------------------------------------------------------- characteristics

    [HttpPost("{id:int}/characteristics")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> AddCharacteristic(int id, CharacteristicInput input)
    {
        var c = await LoadCategory(id, withCharacteristics: true);
        var ch = new Characteristic { CategoryId = c.Id, Sequence = c.Characteristics.Count == 0 ? 1 : c.Characteristics.Max(x => x.Sequence) + 1 };
        ApplyCharacteristic(ch, input, c.Characteristics);
        c.Characteristics.Add(ch);
        await db.SaveChangesAsync();
        audit.Log(Entity, c.Id, "Characteristic added", $"\"{ch.Name}\" ({ch.InputType}{(string.IsNullOrEmpty(ch.CalcVariable) ? "" : $", {ch.CalcVariable}")})", c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Characteristic added.", id = ch.Id });
    }

    [HttpPut("characteristics/{cid:int}")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> UpdateCharacteristic(int cid, CharacteristicInput input)
    {
        var (c, ch) = await LoadCharacteristic(cid);
        var oldName = ch.Name;
        ApplyCharacteristic(ch, input, c.Characteristics);
        audit.Log(Entity, c.Id, "Characteristic updated", oldName == ch.Name ? $"\"{ch.Name}\"" : $"\"{oldName}\" → \"{ch.Name}\"", c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Characteristic updated.", id = ch.Id });
    }

    [HttpDelete("characteristics/{cid:int}")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> DeleteCharacteristic(int cid)
    {
        var (c, ch) = await LoadCharacteristic(cid);
        var used = await db.ProcessStepValues.CountAsync(v => v.CharacteristicId == cid);
        if (used > 0)
            throw ApiException.Bad($"The \"{ch.Name}\" characteristic has values in {used} process step entr{(used == 1 ? "y" : "ies")} and cannot be deleted.");
        c.Characteristics.Remove(ch);
        db.Characteristics.Remove(ch);
        var seq = 1;
        foreach (var x in c.Characteristics.OrderBy(x => x.Sequence)) x.Sequence = seq++;
        audit.Log(Entity, c.Id, "Characteristic deleted", $"\"{ch.Name}\"", c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Characteristic deleted." });
    }

    /// <summary>PUT /api/material-categories/5/characteristics/order { ids: [..] } — sets sequence 1..n in the given order.</summary>
    [HttpPut("{id:int}/characteristics/order")]
    [Authorize(Roles = Access.MaterialCategories)]
    public async Task<IActionResult> Reorder(int id, ReorderInput input)
    {
        var c = await LoadCategory(id, withCharacteristics: true);
        var ids = input.Ids ?? [];
        if (ids.Count != c.Characteristics.Count || ids.Distinct().Count() != ids.Count || ids.Any(i => c.Characteristics.All(x => x.Id != i)))
            throw ApiException.Bad("The characteristic order is out of date. Refresh the page and try again.");
        for (var i = 0; i < ids.Count; i++) c.Characteristics.First(x => x.Id == ids[i]).Sequence = i + 1;
        audit.Log(Entity, c.Id, "Characteristics reordered",
            string.Join(", ", ids.Select(i => c.Characteristics.First(x => x.Id == i).Name)), c.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Order saved." });
    }

    // ---------------------------------------------------------------- helpers

    private async Task<MaterialCategory> LoadCategory(int id, bool withCharacteristics = false)
    {
        var q = db.MaterialCategories.AsQueryable();
        if (withCharacteristics) q = q.Include(c => c.Characteristics);
        var c = await q.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Category");
        await me.EnsureGroupAsync(c.GroupId);
        return c;
    }

    private async Task<(MaterialCategory c, Characteristic ch)> LoadCharacteristic(int cid)
    {
        var categoryId = await db.Characteristics.Where(x => x.Id == cid).Select(x => (int?)x.CategoryId).FirstOrDefaultAsync()
                         ?? throw ApiException.NotFound("Characteristic");
        var c = await LoadCategory(categoryId, withCharacteristics: true);
        return (c, c.Characteristics.First(x => x.Id == cid));
    }

    private async Task ApplyAsync(MaterialCategory c, CategoryInput input)
    {
        var name = Text.Req(input.Name, "Name");
        if (name.Length > 400) throw ApiException.Bad("Name is too long (400 characters max).");
        if (!Enum.IsDefined(input.MaterialType)) throw ApiException.Bad("Material Type is required.");
        if (await db.MaterialCategories.AnyAsync(x => x.GroupId == input.GroupId && x.MaterialType == input.MaterialType && x.Name == name && x.Id != c.Id))
            throw ApiException.Bad($"A {MaterialTypes.Label(input.MaterialType)} category named \"{name}\" already exists.");
        c.Name = name;
        c.MaterialType = input.MaterialType;
        c.Filter1 = Text.Clean(input.Filter1);
        c.Filter2 = Text.Clean(input.Filter2);
    }

    private static void ApplyCharacteristic(Characteristic ch, CharacteristicInput input, IEnumerable<Characteristic> siblings)
    {
        var name = Text.Req(input.Name, "Name");
        if (siblings.Any(x => x.Id != ch.Id && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw ApiException.Bad($"A characteristic named \"{name}\" already exists in this category.");
        var inputType = Text.Clean(input.InputType) ?? CharacteristicInputTypes.Text;
        if (!CharacteristicInputTypes.All.Contains(inputType)) throw ApiException.Bad("Invalid input type.");
        var calc = Text.Clean(input.CalcVariable) ?? CalcVariables.None;
        if (!CalcVariables.All.Contains(calc)) throw ApiException.Bad("Invalid calculation variable.");
        if (calc == CalcVariables.MaterialId && inputType != CharacteristicInputTypes.Material)
            throw ApiException.Bad("\"Material used\" can only be set on a characteristic whose input type is Material.");
        if (calc is CalcVariables.Coverage or CalcVariables.MixPercent or CalcVariables.ProductionRate or CalcVariables.CostPerSqFt
            && inputType != CharacteristicInputTypes.Number)
            throw ApiException.Bad("Numeric calculation variables require the Number input type.");
        ch.Name = name;
        ch.Unit = Text.Clean(input.Unit);
        ch.InputType = inputType;
        ch.CalcVariable = calc.Length == 0 ? null : calc;
        ch.DefaultValue = Text.Clean(input.DefaultValue);
    }
}
