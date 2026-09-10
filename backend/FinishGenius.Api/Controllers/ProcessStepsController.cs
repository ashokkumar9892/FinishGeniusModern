using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

public record StepValueView(int ValueId, int CharacteristicId, string Characteristic, string? Unit, string InputType,
    string? CalcVariable, string? Value, int? MaterialId, string? MaterialName, string Display);

public record StepEntryView(int EntryId, int? PullDownId, string? Header, int? CategoryId, string? CategoryName, List<StepValueView> Values);

public record StepPassView(int SubStepId, int Sequence, int Pass, string Label, string Name, string ShortName, string UserRole,
    bool Filled, List<StepEntryView> Entries);

/// <summary>Process Steps: list, the sub step "builder", copy / bulk copy and the "View Step" read model.</summary>
[ApiController]
[Authorize(Roles = Access.Process)]
[Route("api/process-steps")]
public class ProcessStepsController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier) : ControllerBase
{
    private const string Entity = "ProcessStep";

    // ---------- Shared read helpers (also used by ProcessSchedulesController) ----------

    public static string Display(string inputType, string? value, string? unit, string? materialName)
    {
        if (inputType == CharacteristicInputTypes.Material) return materialName ?? "";
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (inputType == CharacteristicInputTypes.Number && !string.IsNullOrWhiteSpace(unit) && unit != "-") return $"{value} {unit}";
        return value;
    }

    /// <summary>Every sub step pass (User and Admin) of the step's sector in order, with the saved values.</summary>
    public static async Task<List<StepPassView>> PassesAsync(AppDbContext db, int stepId, int sectorId)
    {
        var subs = await db.SubSteps.AsNoTracking().Include(s => s.PullDowns)
            .Where(s => s.IndustrySectorId == sectorId).OrderBy(s => s.Sequence).ToListAsync();
        var entries = await db.ProcessStepEntries.AsNoTracking().Where(e => e.ProcessStepId == stepId)
            .Include(e => e.Category)
            .Include(e => e.Values).ThenInclude(v => v.Characteristic)
            .Include(e => e.Values).ThenInclude(v => v.Material)
            .ToListAsync();

        var result = new List<StepPassView>();
        foreach (var s in subs)
        {
            var pullDowns = s.PullDowns.ToDictionary(p => p.Id);
            for (var pass = 1; pass <= Math.Max(1, s.PassThroughs); pass++)
            {
                var list = entries.Where(e => e.SubStepId == s.Id && e.Pass == pass)
                    .OrderBy(e => e.PullDownId != null && pullDowns.TryGetValue(e.PullDownId.Value, out var p) ? p.Sequence : 0).ThenBy(e => e.Id)
                    .Select(e => new StepEntryView(e.Id, e.PullDownId,
                        e.PullDownId != null && pullDowns.TryGetValue(e.PullDownId.Value, out var pd) ? pd.Header : null,
                        e.CategoryId, e.Category?.Name,
                        e.Values.Where(v => v.Characteristic != null)
                            .OrderBy(v => v.Characteristic!.Sequence).ThenBy(v => v.Id)
                            .Select(v => new StepValueView(v.Id, v.CharacteristicId, v.Characteristic!.Name, v.Characteristic.Unit,
                                v.Characteristic.InputType, v.Characteristic.CalcVariable, v.Value, v.MaterialId, v.Material?.ProductName,
                                Display(v.Characteristic.InputType, v.Value, v.Characteristic.Unit, v.Material?.ProductName)))
                            .ToList()))
                    .ToList();
                result.Add(new StepPassView(s.Id, s.Sequence, pass, ScheduleCalculator.SubStepLabel(s.Sequence, s.PassThroughs, pass),
                    s.Name, s.ShortName, s.UserRole, list.Any(e => e.CategoryId != null), list));
            }
        }
        return result;
    }

    private static bool Matches(SubStepPullDown p, MaterialCategory c) =>
        (p.MaterialType == null || p.MaterialType == c.MaterialType) &&
        (string.IsNullOrWhiteSpace(p.CategoryFilter1) || string.Equals(p.CategoryFilter1.Trim(), c.Filter1?.Trim(), StringComparison.OrdinalIgnoreCase)) &&
        (string.IsNullOrWhiteSpace(p.CategoryFilter2) || string.Equals(p.CategoryFilter2.Trim(), c.Filter2?.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<ProcessStep> LoadStepAsync(int id)
    {
        var s = await db.ProcessSteps.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Process Step");
        await me.EnsureGroupAsync(s.GroupId);
        return s;
    }

    private async Task EnsureUniqueNameAsync(int groupId, string name, int? exceptId)
    {
        if (await db.ProcessSteps.AnyAsync(s => s.GroupId == groupId && !s.IsDeleted && s.Name == name && s.Id != (exceptId ?? 0)))
            throw ApiException.Bad($"A process step named \"{name}\" already exists in this group.");
    }

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> work)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await work();
            await tx.CommitAsync();
            return result;
        });
    }

    // ---------- List / detail ----------

    /// <summary>GET /api/process-steps?groupId=2&amp;industrySectorId=7&amp;search=spray</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] int? industrySectorId, [FromQuery] string? search)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.ProcessSteps.AsNoTracking().Where(s => s.GroupId == groupId && !s.IsDeleted);
        if (industrySectorId is > 0) q = q.Where(s => s.IndustrySectorId == industrySectorId);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.Name.Contains(search.Trim()));
        var rows = await q.OrderByDescending(s => s.Id).Select(s => new
        {
            s.Id, s.GroupId, GroupName = s.Group!.Name, s.Name, s.IndustrySectorId, IndustrySectorName = s.IndustrySector!.Name,
            FilledCount = s.Entries.Where(e => e.CategoryId != null).Select(e => e.SubStepId * 100 + e.Pass).Distinct().Count(),
            ScheduleCount = db.ProcessSchedules.Count(ps => !ps.IsArchived && ps.Steps.Any(x => x.ProcessStepId == s.Id)),
            s.CreatedAt, s.UpdatedAt,
        }).ToListAsync();
        return Ok(rows);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var s = await LoadStepAsync(id);
        var groupName = await db.Groups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstAsync();
        var sector = await db.IndustrySectors.Where(x => x.Id == s.IndustrySectorId).Select(x => x.Name).FirstOrDefaultAsync();
        return Ok(new { s.Id, s.GroupId, GroupName = groupName, s.Name, s.IndustrySectorId, IndustrySectorName = sector, s.CreatedAt, s.UpdatedAt });
    }

    // ---------- Builder ----------

    /// <summary>
    /// GET /api/process-steps/builder?groupId=2&amp;industrySectorId=7&amp;stepId=5 — everything the step builder needs in one call.
    /// </summary>
    [HttpGet("builder")]
    public async Task<IActionResult> Builder([FromQuery] int groupId, [FromQuery] int? industrySectorId, [FromQuery] int? stepId)
    {
        ProcessStep? step = null;
        if (stepId is > 0)
        {
            step = await LoadStepAsync(stepId.Value);
            groupId = step.GroupId;
        }
        else await me.EnsureGroupAsync(groupId);

        var sectorId = step?.IndustrySectorId
                       ?? (industrySectorId is > 0 ? industrySectorId.Value : await db.IndustrySectors.Where(s => s.Name == "Wood").Select(s => s.Id).FirstOrDefaultAsync());
        var sector = await db.IndustrySectors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectorId) ?? throw ApiException.NotFound("Industry Sector");
        var groupName = await db.Groups.Where(g => g.Id == groupId).Select(g => g.Name).FirstAsync();

        var subs = await db.SubSteps.AsNoTracking().Include(s => s.PullDowns)
            .Where(s => s.IndustrySectorId == sectorId).OrderBy(s => s.Sequence).ToListAsync();
        var categories = await db.MaterialCategories.AsNoTracking().Include(c => c.Characteristics)
            .Where(c => c.GroupId == groupId || c.GroupId == null).OrderBy(c => c.Name).ToListAsync();
        var entries = step == null
            ? []
            : await db.ProcessStepEntries.AsNoTracking().Where(e => e.ProcessStepId == step.Id)
                .Include(e => e.Values).ThenInclude(v => v.Material)
                .ToListAsync();

        object CategoryDto(MaterialCategory c) => new
        {
            c.Id, c.Name, MaterialType = (int)c.MaterialType, MaterialTypeLabel = MaterialTypes.Label(c.MaterialType), c.Filter1, c.Filter2,
            Characteristics = c.Characteristics.OrderBy(x => x.Sequence).ThenBy(x => x.Id)
                .Select(x => new { x.Id, x.Name, x.Unit, x.InputType, x.CalcVariable, x.DefaultValue, x.Sequence }),
        };

        // Entries saved without a (valid) pull down are attached to the sub step's first pull down.
        int? EffectivePullDown(ProcessStepEntry e)
        {
            var sub = subs.FirstOrDefault(s => s.Id == e.SubStepId);
            if (sub == null) return e.PullDownId;
            if (e.PullDownId != null && sub.PullDowns.Any(p => p.Id == e.PullDownId)) return e.PullDownId;
            return sub.PullDowns.OrderBy(p => p.Sequence).ThenBy(p => p.Id).FirstOrDefault()?.Id;
        }
        var effective = entries.Select(e => new { Entry = e, PullDownId = EffectivePullDown(e) }).ToList();

        return Ok(new
        {
            GroupId = groupId,
            GroupName = groupName,
            IndustrySectorId = sector.Id,
            IndustrySectorName = sector.Name,
            Step = step == null ? null : new { step.Id, step.Name, step.GroupId, step.IndustrySectorId, step.CreatedAt, step.UpdatedAt },
            SubSteps = subs.Select(s => new
            {
                s.Id, s.UserRole, s.Name, s.ShortName, s.Sequence, s.PassThroughs, s.WebLink, s.Instruction,
                PullDowns = s.PullDowns.OrderBy(p => p.Sequence).ThenBy(p => p.Id).Select(p =>
                {
                    var candidates = categories.Where(c => Matches(p, c)).ToList();
                    // Keep categories already saved on this pull down selectable even if the filter no longer matches them.
                    var savedIds = effective.Where(x => x.PullDownId == p.Id && x.Entry.CategoryId != null).Select(x => x.Entry.CategoryId!.Value);
                    candidates.AddRange(categories.Where(c => savedIds.Contains(c.Id) && !candidates.Contains(c)));
                    return new
                    {
                        p.Id, p.Sequence, p.Header, p.ChoiceName,
                        MaterialType = p.MaterialType == null ? (int?)null : (int)p.MaterialType.Value,
                        p.CategoryFilter1, p.CategoryFilter2, Query = SubStepsController.QueryText(p),
                        Categories = candidates.Select(CategoryDto),
                    };
                }),
            }),
            Entries = effective.Select(x => new
            {
                x.Entry.Id, x.Entry.SubStepId, x.Entry.Pass, x.PullDownId, x.Entry.CategoryId,
                Values = x.Entry.Values.Select(v => new { ValueId = v.Id, v.CharacteristicId, v.Value, v.MaterialId, MaterialName = v.Material?.ProductName }),
            }),
        });
    }

    public class ValueInput
    {
        public int CharacteristicId { get; set; }
        public string? Value { get; set; }
        public int? MaterialId { get; set; }
    }

    public class SelectionInput
    {
        public int PullDownId { get; set; }
        public int? CategoryId { get; set; }
        public List<ValueInput> Values { get; set; } = [];
    }

    public class SaveSubStepInput
    {
        public int? StepId { get; set; }
        public int GroupId { get; set; }
        public int IndustrySectorId { get; set; }
        public string? Name { get; set; }
        public int SubStepId { get; set; }
        public int Pass { get; set; } = 1;
        public List<SelectionInput> Selections { get; set; } = [];
    }

    /// <summary>
    /// PUT /api/process-steps/save-substep — saves one pass of one sub step. The first save creates the step.
    /// Values are updated in place (ids stay stable) so schedule-level overrides that point at them survive.
    /// </summary>
    [HttpPut("save-substep")]
    public async Task<IActionResult> SaveSubStep([FromBody] SaveSubStepInput input)
    {
        ProcessStep? step = null;
        int groupId, sectorId;
        string? newName = Text.Clean(input.Name);
        if (input.StepId is > 0)
        {
            step = await LoadStepAsync(input.StepId.Value);
            groupId = step.GroupId;
            sectorId = step.IndustrySectorId;
            if (newName != null && newName != step.Name) await EnsureUniqueNameAsync(groupId, newName, step.Id);
        }
        else
        {
            groupId = input.GroupId;
            await me.EnsureGroupAsync(groupId);
            newName = Text.Req(input.Name, "Step Name");
            sectorId = input.IndustrySectorId;
            if (!await db.IndustrySectors.AnyAsync(s => s.Id == sectorId)) throw ApiException.Bad("Industry Sector is required.");
            await EnsureUniqueNameAsync(groupId, newName, null);
        }
        if (newName is { Length: > 400 }) throw ApiException.Bad("Step Name cannot be longer than 400 characters.");

        var sub = await db.SubSteps.AsNoTracking().Include(s => s.PullDowns).FirstOrDefaultAsync(s => s.Id == input.SubStepId)
                  ?? throw ApiException.NotFound("Sub Step");
        if (sub.IndustrySectorId != sectorId) throw ApiException.Bad("This sub step does not belong to the step's industry sector.");
        if (input.Pass < 1 || input.Pass > Math.Max(1, sub.PassThroughs)) throw ApiException.Bad("Invalid pass for this sub step.");
        var label = ScheduleCalculator.SubStepLabel(sub.Sequence, sub.PassThroughs, input.Pass);

        // ---- Validate the selections ----
        var selections = input.Selections ?? [];
        if (selections.Select(s => s.PullDownId).Distinct().Count() != selections.Count) throw ApiException.Bad("Each pull down can only be answered once.");
        var pullDowns = sub.PullDowns.ToDictionary(p => p.Id);
        foreach (var sel in selections)
            if (!pullDowns.ContainsKey(sel.PullDownId)) throw ApiException.Bad("Invalid pull down for this sub step.");

        var categoryIds = selections.Where(s => s.CategoryId != null).Select(s => s.CategoryId!.Value).Distinct().ToList();
        var categories = await db.MaterialCategories.AsNoTracking().Include(c => c.Characteristics)
            .Where(c => categoryIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
        var existing = step == null
            ? []
            : await db.ProcessStepEntries.Include(e => e.Values).Where(e => e.ProcessStepId == step.Id && e.SubStepId == sub.Id && e.Pass == input.Pass).ToListAsync();
        var existingMaterialIds = existing.SelectMany(e => e.Values).Where(v => v.MaterialId != null).Select(v => v.MaterialId!.Value).ToHashSet();
        var materialIds = selections.SelectMany(s => s.Values).Where(v => v.MaterialId != null).Select(v => v.MaterialId!.Value).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        var cleaned = new List<(SelectionInput sel, MaterialCategory? cat, List<ValueInput> values)>();
        foreach (var sel in selections)
        {
            if (sel.CategoryId == null)
            {
                cleaned.Add((sel, null, []));
                continue;
            }
            if (!categories.TryGetValue(sel.CategoryId.Value, out var cat) || (cat.GroupId != null && cat.GroupId != groupId))
                throw ApiException.Bad($"The selected category for \"{pullDowns[sel.PullDownId].Header}\" does not belong to this group.");
            var chars = cat.Characteristics.ToDictionary(c => c.Id);
            var values = new List<ValueInput>();
            foreach (var v in (sel.Values ?? []).GroupBy(v => v.CharacteristicId).Select(g => g.Last()))
            {
                if (!chars.TryGetValue(v.CharacteristicId, out var ch))
                    throw ApiException.Bad($"Invalid characteristic for category \"{cat.Name}\".");
                if (ch.InputType == CharacteristicInputTypes.Material)
                {
                    if (v.MaterialId == null) continue;
                    if (!materials.TryGetValue(v.MaterialId.Value, out var m) || m.GroupId != groupId)
                        throw ApiException.Bad($"The material selected for \"{ch.Name}\" does not belong to this group.");
                    if (m.IsDeleted && !existingMaterialIds.Contains(m.Id))
                        throw ApiException.Bad($"The material \"{m.ProductName}\" selected for \"{ch.Name}\" has been deleted.");
                    values.Add(new ValueInput { CharacteristicId = ch.Id, MaterialId = m.Id });
                    continue;
                }
                var text = Text.Clean(v.Value);
                if (text == null) continue;
                if (text.Length > 4000) throw ApiException.Bad($"\"{ch.Name}\" cannot be longer than 4000 characters.");
                if (ch.InputType == CharacteristicInputTypes.Number && ScheduleCalculator.Num(text) == null)
                    throw ApiException.Bad($"\"{ch.Name}\" must be a number.");
                if (ch.InputType == CharacteristicInputTypes.YesNo && text is not ("Yes" or "No"))
                    throw ApiException.Bad($"\"{ch.Name}\" must be Yes or No.");
                values.Add(new ValueInput { CharacteristicId = ch.Id, Value = text });
            }
            cleaned.Add((sel, cat, values));
        }

        var firstPullDownId = sub.PullDowns.OrderBy(p => p.Sequence).ThenBy(p => p.Id).FirstOrDefault()?.Id;
        int? EffectivePullDown(ProcessStepEntry e) => e.PullDownId != null && pullDowns.ContainsKey(e.PullDownId.Value) ? e.PullDownId : firstPullDownId;

        var created = step == null;
        var stepId = await InTransactionAsync(async () =>
        {
            if (step == null)
            {
                step = new ProcessStep { GroupId = groupId, Name = newName!, IndustrySectorId = sectorId };
                db.ProcessSteps.Add(step);
                await db.SaveChangesAsync();
                audit.Log(Entity, step.Id, "Created", $"{step.Name}", groupId);
            }
            else if (newName != null && newName != step.Name)
            {
                audit.Log(Entity, step.Id, "Renamed", $"{step.Name} → {newName}", groupId);
                step.Name = newName;
            }

            var removedValueIds = new List<int>();
            var remaining = existing.ToList();
            var summary = new List<string>();
            foreach (var (sel, cat, values) in cleaned)
            {
                var entry = remaining.FirstOrDefault(e => EffectivePullDown(e) == sel.PullDownId);
                if (entry != null) remaining.Remove(entry);
                if (cat == null)
                {
                    if (entry != null)
                    {
                        removedValueIds.AddRange(entry.Values.Select(v => v.Id));
                        db.ProcessStepEntries.Remove(entry);
                    }
                    continue;
                }
                if (entry == null)
                {
                    entry = new ProcessStepEntry { ProcessStepId = step.Id, SubStepId = sub.Id, Pass = input.Pass };
                    db.ProcessStepEntries.Add(entry);
                }
                entry.PullDownId = sel.PullDownId;
                if (entry.CategoryId != cat.Id)
                {
                    removedValueIds.AddRange(entry.Values.Where(v => v.Id > 0).Select(v => v.Id));
                    db.ProcessStepValues.RemoveRange(entry.Values);
                    entry.Values.Clear();
                    entry.CategoryId = cat.Id;
                }
                foreach (var old in entry.Values.Where(v => values.All(n => n.CharacteristicId != v.CharacteristicId)).ToList())
                {
                    removedValueIds.Add(old.Id);
                    entry.Values.Remove(old);
                    db.ProcessStepValues.Remove(old);
                }
                foreach (var n in values)
                {
                    var v = entry.Values.FirstOrDefault(x => x.CharacteristicId == n.CharacteristicId);
                    if (v == null) entry.Values.Add(new ProcessStepValue { CharacteristicId = n.CharacteristicId, Value = n.Value, MaterialId = n.MaterialId });
                    else
                    {
                        v.Value = n.Value;
                        v.MaterialId = n.MaterialId;
                    }
                }
                summary.Add($"{pullDowns[sel.PullDownId].Header}: {cat.Name} ({values.Count} value{(values.Count == 1 ? "" : "s")})");
            }
            foreach (var orphan in remaining)
            {
                removedValueIds.AddRange(orphan.Values.Select(v => v.Id));
                db.ProcessStepEntries.Remove(orphan);
            }
            if (removedValueIds.Count > 0)
            {
                var overrides = await db.ScheduleStepOverrides.Where(o => removedValueIds.Contains(o.ProcessStepValueId)).ToListAsync();
                db.ScheduleStepOverrides.RemoveRange(overrides);
            }

            step.UpdatedAt = DateTime.UtcNow;
            audit.Log(Entity, step.Id, "Sub step saved",
                $"{label} {sub.Name}" + (summary.Count == 0 ? " — cleared" : "\n" + string.Join("\n", summary)), groupId);
            await db.SaveChangesAsync();
            return step.Id;
        });

        return Ok(new { message = "SubStep Saved", stepId, created });
    }

    // ---------- Rename / delete / copy ----------

    public class RenameInput
    {
        public string? Name { get; set; }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Rename(int id, [FromBody] RenameInput input)
    {
        var s = await LoadStepAsync(id);
        var name = Text.Req(input.Name, "Step Name");
        if (name.Length > 400) throw ApiException.Bad("Step Name cannot be longer than 400 characters.");
        await EnsureUniqueNameAsync(s.GroupId, name, s.Id);
        if (name != s.Name)
        {
            audit.Log(Entity, s.Id, "Renamed", $"{s.Name} → {name}", s.GroupId);
            s.Name = name;
            s.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        return Ok(new { message = "Process step updated.", id = s.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        me.EnsureAdmin();
        var s = await LoadStepAsync(id);
        var schedules = await db.ProcessSchedules.AsNoTracking()
            .Where(ps => !ps.IsArchived && ps.Steps.Any(x => x.ProcessStepId == id))
            .OrderBy(ps => ps.Name).Select(ps => new { ps.Name, ps.Number }).ToListAsync();
        if (schedules.Count > 0)
            throw ApiException.Bad("This process step cannot be deleted because it is used by the following process schedule"
                                   + (schedules.Count == 1 ? "" : "s") + ": "
                                   + string.Join(", ", schedules.Select(x => $"{x.Name} (#{x.Number})"))
                                   + ". Remove it from those schedules first.");
        s.IsDeleted = true;
        s.UpdatedAt = DateTime.UtcNow;
        audit.Log(Entity, s.Id, "Deleted", s.Name, s.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Process step deleted." });
    }

    public class CopyInput
    {
        public string? NewName { get; set; }
    }

    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id, [FromBody] CopyInput input)
    {
        var s = await LoadStepAsync(id);
        var name = Text.Req(input.NewName, "New Name");
        if (name.Length > 400) throw ApiException.Bad("New Name cannot be longer than 400 characters.");
        await EnsureUniqueNameAsync(s.GroupId, name, null);

        var newId = await InTransactionAsync(async () =>
        {
            await copier.CopyStepsAsync([id], s.GroupId, name);
            var copyId = await db.ProcessSteps.Where(x => x.GroupId == s.GroupId && !x.IsDeleted && x.Name == name).MaxAsync(x => x.Id);
            audit.Log(Entity, copyId, "Created", $"Copied from \"{s.Name}\" (#{s.Id})", s.GroupId);
            audit.Log(Entity, s.Id, "Copied", $"Copied to \"{name}\" (#{copyId})", s.GroupId);
            await db.SaveChangesAsync();
            return copyId;
        });
        return Ok(new { message = "Process step copied.", id = newId });
    }

    public class BulkCopyInput
    {
        public List<int> Ids { get; set; } = [];
        public int DestinationGroupId { get; set; }
    }

    [HttpPost("bulk-copy")]
    public async Task<IActionResult> BulkCopy([FromBody] BulkCopyInput input)
    {
        var ids = (input.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Select at least one process step to copy.");
        await me.EnsureGroupAsync(input.DestinationGroupId);
        var dest = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == input.DestinationGroupId && !g.IsDeleted)
                   ?? throw ApiException.NotFound("Destination group");
        var sources = await db.ProcessSteps.AsNoTracking().Where(s => ids.Contains(s.Id) && !s.IsDeleted).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.NotFound("Process Step");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        await InTransactionAsync(async () =>
        {
            foreach (var s in sources.OrderBy(x => x.Id))
            {
                var name = s.Name;
                for (var n = 2; await db.ProcessSteps.AnyAsync(x => x.GroupId == dest.Id && !x.IsDeleted && x.Name == name); n++)
                    name = $"{s.Name} ({n})";
                await copier.CopyStepsAsync([s.Id], dest.Id, name);
                var copyId = await db.ProcessSteps.Where(x => x.GroupId == dest.Id && !x.IsDeleted && x.Name == name).MaxAsync(x => x.Id);
                audit.Log(Entity, copyId, "Created", $"Bulk copied from \"{s.Name}\" (#{s.Id})", dest.Id);
                audit.Log(Entity, s.Id, "Copied", $"Bulk copied to group \"{dest.Name}\" as \"{name}\" (#{copyId})", s.GroupId);
            }
            await db.SaveChangesAsync();
            return true;
        });
        var count = sources.Count;
        return Ok(new { message = $"Successfully copied {count} step{(count == 1 ? "" : "s")} to the {dest.Name} group.", count });
    }

    // ---------- View ----------

    /// <summary>GET /api/process-steps/5/view — every sub step pass (User + Admin) with "filled" and the saved values.</summary>
    [HttpGet("{id:int}/view")]
    public async Task<IActionResult> View(int id)
    {
        var s = await LoadStepAsync(id);
        var sector = await db.IndustrySectors.Where(x => x.Id == s.IndustrySectorId).Select(x => x.Name).FirstOrDefaultAsync();
        var groupName = await db.Groups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstAsync();
        return Ok(new
        {
            s.Id, s.Name, s.GroupId, GroupName = groupName, s.IndustrySectorId, IndustrySectorName = sector, s.UpdatedAt,
            Passes = await PassesAsync(db, s.Id, s.IndustrySectorId),
        });
    }
}
