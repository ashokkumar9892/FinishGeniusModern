using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Process Schedules ("Process Systems"). The list endpoint is shared by Material Quantities, Pricing, My Work and Dashboard,
/// so the controller only requires authentication; every other action checks the Process roles.
/// </summary>
[ApiController]
[Authorize]
[Route("api/process-schedules")]
public class ProcessSchedulesController(AppDbContext db, CurrentUser me, AuditService audit, GroupCopyService copier, ScheduleCalculator calc) : ControllerBase
{
    private const string Entity = "ProcessSchedule";

    /// <summary>GET /api/process-schedules?groupId=1 — non-archived schedules of a group.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] bool includeArchived = false)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.ProcessSchedules.AsNoTracking()
            .Where(s => s.GroupId == groupId && (includeArchived || !s.IsArchived))
            .OrderByDescending(s => s.Id)
            .Select(s => new
            {
                s.Id, s.GroupId, GroupName = s.Group!.Name, s.Name, s.Number, s.CustomerName, s.DepartmentId,
                DepartmentName = s.Department != null ? s.Department.Name : null, s.IsArchived,
                StepCount = s.Steps.Count, s.CreatedAt, s.UpdatedAt,
            }).ToListAsync();
        return Ok(rows);
    }

    // ---------- Helpers ----------

    private async Task<ProcessSchedule> LoadAsync(int id, bool includeArchived = false)
    {
        var s = await db.ProcessSchedules.FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Process Schedule");
        await me.EnsureGroupAsync(s.GroupId);
        if (s.IsArchived && !includeArchived) throw ApiException.Bad("This process schedule is archived. Restore it first.");
        return s;
    }

    private async Task EnsureUniqueNumberAsync(int groupId, string number, int? exceptId)
    {
        if (await db.ProcessSchedules.AnyAsync(s => s.GroupId == groupId && !s.IsArchived && s.Number == number && s.Id != (exceptId ?? 0)))
            throw ApiException.Bad("Schedule # already exists in this group.");
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

    private static string Fmt(decimal d) => d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    // ---------- Detail / create / update ----------

    /// <summary>GET /api/process-schedules/5 — header + ordered assigned steps.</summary>
    [HttpGet("{id:int}")]
    [Authorize(Roles = Access.ProcessRead)]
    public async Task<IActionResult> Get(int id)
    {
        var s = await db.ProcessSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Process Schedule");
        await me.EnsureGroupAsync(s.GroupId);
        var groupName = await db.Groups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstAsync();
        var steps = await db.ProcessScheduleSteps.AsNoTracking().Where(x => x.ScheduleId == id)
            .OrderBy(x => x.Ordering).ThenBy(x => x.Id)
            .Select(x => new
            {
                ScheduleStepId = x.Id, x.ProcessStepId, OriginalName = x.ProcessStep!.Name, x.NameOverride, x.Ordering,
                OverrideCount = x.Overrides.Count, IndustrySectorName = x.ProcessStep.IndustrySector!.Name,
                ProcessStepDeleted = x.ProcessStep.IsDeleted,
            }).ToListAsync();
        return Ok(new
        {
            s.Id, s.GroupId, GroupName = groupName, s.Name, s.Number, s.CustomerName, s.DepartmentId, s.IsArchived, s.CreatedAt, s.UpdatedAt,
            Steps = steps.Select(x => new
            {
                x.ScheduleStepId, x.ProcessStepId, Name = x.NameOverride ?? x.OriginalName, x.OriginalName, x.NameOverride, x.Ordering,
                HasOverrides = x.OverrideCount > 0 || x.NameOverride != null, x.OverrideCount, x.IndustrySectorName, x.ProcessStepDeleted,
            }),
        });
    }

    public class ScheduleStepInput
    {
        public int? ScheduleStepId { get; set; }
        public int ProcessStepId { get; set; }
    }

    public class ScheduleInput
    {
        public int GroupId { get; set; }
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? CustomerName { get; set; }
        public List<ScheduleStepInput> Steps { get; set; } = [];
    }

    private static (string name, string number, string? customer) Clean(ScheduleInput input)
    {
        var name = Text.Req(input.Name, "Schedule Name");
        var number = Text.Req(input.Number, "Schedule #");
        var customer = Text.Clean(input.CustomerName);
        if (name.Length > 400 || number.Length > 400 || customer is { Length: > 400 }) throw ApiException.Bad("Values cannot be longer than 400 characters.");
        return (name, number, customer);
    }

    /// <summary>Validates that new (not preserved) steps are live process steps of the schedule's group.</summary>
    private async Task<Dictionary<int, string>> ValidateStepsAsync(int groupId, IEnumerable<int> newStepIds)
    {
        var ids = newStepIds.Distinct().ToList();
        var found = await db.ProcessSteps.AsNoTracking().Where(p => ids.Contains(p.Id) && p.GroupId == groupId && !p.IsDeleted)
            .ToDictionaryAsync(p => p.Id, p => p.Name);
        if (found.Count != ids.Count) throw ApiException.Bad("One or more selected process steps do not exist in this group.");
        return found;
    }

    [HttpPost]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Create([FromBody] ScheduleInput input)
    {
        await me.EnsureGroupAsync(input.GroupId);
        var (name, number, customer) = Clean(input);
        await EnsureUniqueNumberAsync(input.GroupId, number, null);
        var steps = input.Steps ?? [];
        var names = await ValidateStepsAsync(input.GroupId, steps.Select(x => x.ProcessStepId));

        var s = new ProcessSchedule { GroupId = input.GroupId, Name = name, Number = number, CustomerName = customer };
        var order = 0;
        foreach (var st in steps) s.Steps.Add(new ProcessScheduleStep { ProcessStepId = st.ProcessStepId, Ordering = ++order });
        await InTransactionAsync(async () =>
        {
            db.ProcessSchedules.Add(s);
            await db.SaveChangesAsync();
            audit.Log(Entity, s.Id, "Created", $"{s.Name} (#{s.Number})" + (steps.Count == 0 ? "" : "\nSteps: " + string.Join(", ", steps.Select(x => names[x.ProcessStepId]))), s.GroupId);
            await db.SaveChangesAsync();
            return true;
        });
        return Ok(new { message = "Process schedule created.", id = s.Id });
    }

    /// <summary>PUT /api/process-schedules/5 — replaces the ordered step list, preserving existing rows (and their edits) by scheduleStepId.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Update(int id, [FromBody] ScheduleInput input)
    {
        var s = await LoadAsync(id);
        var (name, number, customer) = Clean(input);
        await EnsureUniqueNumberAsync(s.GroupId, number, s.Id);
        var existing = await db.ProcessScheduleSteps.Include(x => x.ProcessStep).Where(x => x.ScheduleId == id).ToListAsync();
        var byId = existing.ToDictionary(x => x.Id);
        var steps = input.Steps ?? [];

        var preserved = new HashSet<int>();
        var newStepIds = new List<int>();
        foreach (var st in steps)
        {
            if (st.ScheduleStepId is > 0)
            {
                if (!byId.TryGetValue(st.ScheduleStepId.Value, out var row)) throw ApiException.Bad("An assigned step does not belong to this schedule. Reload the page and try again.");
                if (!preserved.Add(row.Id)) throw ApiException.Bad("An assigned step was listed twice.");
                if (row.ProcessStepId != st.ProcessStepId) throw ApiException.Bad("An assigned step does not match its process step. Reload the page and try again.");
            }
            else newStepIds.Add(st.ProcessStepId);
        }
        var names = await ValidateStepsAsync(s.GroupId, newStepIds);

        var changes = new List<string>();
        if (s.Name != name) changes.Add($"Name: {s.Name} → {name}");
        if (s.Number != number) changes.Add($"Number: {s.Number} → {number}");
        if (s.CustomerName != customer) changes.Add($"Customer: {s.CustomerName ?? "—"} → {customer ?? "—"}");
        var removed = existing.Where(x => !preserved.Contains(x.Id)).ToList();
        if (removed.Count > 0) changes.Add("Removed steps: " + string.Join(", ", removed.Select(x => x.NameOverride ?? x.ProcessStep?.Name)));
        if (newStepIds.Count > 0) changes.Add("Added steps: " + string.Join(", ", newStepIds.Select(x => names[x])));
        var oldOrder = existing.Where(x => preserved.Contains(x.Id)).OrderBy(x => x.Ordering).ThenBy(x => x.Id).Select(x => x.Id).ToList();
        var newOrder = steps.Where(x => x.ScheduleStepId is > 0).Select(x => x.ScheduleStepId!.Value).ToList();
        if (!oldOrder.SequenceEqual(newOrder)) changes.Add("Steps reordered");

        await InTransactionAsync(async () =>
        {
            s.Name = name;
            s.Number = number;
            s.CustomerName = customer;
            s.UpdatedAt = DateTime.UtcNow;
            db.ProcessScheduleSteps.RemoveRange(removed); // overrides cascade
            var order = 0;
            foreach (var st in steps)
            {
                order++;
                if (st.ScheduleStepId is > 0) byId[st.ScheduleStepId.Value].Ordering = order;
                else db.ProcessScheduleSteps.Add(new ProcessScheduleStep { ScheduleId = s.Id, ProcessStepId = st.ProcessStepId, Ordering = order });
            }
            audit.Log(Entity, s.Id, "Updated", changes.Count == 0 ? "No changes" : string.Join("\n", changes), s.GroupId);
            await db.SaveChangesAsync();
            return true;
        });
        return Ok(new { message = "Process schedule updated.", id = s.Id });
    }

    // ---------- Step edits (schedule-level overrides) ----------

    private async Task<(ProcessScheduleStep row, ProcessSchedule schedule, ProcessStep step)> LoadScheduleStepAsync(int scheduleStepId, bool tracked)
    {
        var q = tracked ? db.ProcessScheduleSteps : db.ProcessScheduleSteps.AsNoTracking();
        var row = await q.Include(x => x.Overrides).Include(x => x.ProcessStep).FirstOrDefaultAsync(x => x.Id == scheduleStepId)
                  ?? throw ApiException.NotFound("Schedule step");
        var schedule = await db.ProcessSchedules.FirstAsync(x => x.Id == row.ScheduleId);
        await me.EnsureGroupAsync(schedule.GroupId);
        return (row, schedule, row.ProcessStep!);
    }

    /// <summary>GET /api/process-schedules/steps/9/edit — name override + every value of the step with its override and range.</summary>
    [HttpGet("steps/{scheduleStepId:int}/edit")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> GetStepEdit(int scheduleStepId)
    {
        var (row, schedule, step) = await LoadScheduleStepAsync(scheduleStepId, false);
        var passes = await ProcessStepsController.PassesAsync(db, step.Id, step.IndustrySectorId);
        var overrides = row.Overrides.ToDictionary(o => o.ProcessStepValueId);
        var overrideMaterialIds = passes.SelectMany(p => p.Entries).SelectMany(e => e.Values)
            .Where(v => v.InputType == CharacteristicInputTypes.Material && overrides.ContainsKey(v.ValueId))
            .Select(v => int.TryParse(overrides[v.ValueId].Value, out var m) ? m : 0).Where(m => m > 0).Distinct().ToList();
        var materialNames = await db.Materials.AsNoTracking().Where(m => overrideMaterialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ProductName);

        var values = passes.SelectMany(p => p.Entries.SelectMany(e => e.Values.Select(v =>
        {
            overrides.TryGetValue(v.ValueId, out var o);
            string? overrideMaterialName = null;
            int? overrideMaterialId = null;
            if (v.InputType == CharacteristicInputTypes.Material && o?.Value != null && int.TryParse(o.Value, out var mid))
            {
                overrideMaterialId = mid;
                overrideMaterialName = materialNames.GetValueOrDefault(mid);
            }
            return new
            {
                v.ValueId, SubStepLabel = p.Label, SubStepName = p.Name, e.Header, e.CategoryName, CategoryId = e.CategoryId,
                v.Characteristic, v.Unit, v.InputType, v.CalcVariable,
                OriginalValue = v.Value, OriginalMaterialId = v.MaterialId, OriginalMaterialName = v.MaterialName, OriginalDisplay = v.Display,
                OverrideValue = o?.Value, OverrideMaterialId = overrideMaterialId, OverrideMaterialName = overrideMaterialName,
                MinValue = o?.MinValue, MaxValue = o?.MaxValue,
            };
        }))).ToList();

        return Ok(new
        {
            ScheduleStepId = row.Id, ScheduleId = schedule.Id, ScheduleName = schedule.Name, schedule.GroupId, ProcessStepId = step.Id,
            OriginalName = step.Name, row.NameOverride, Name = row.NameOverride ?? step.Name, Values = values, Passes = passes,
        });
    }

    public class ValueOverrideInput
    {
        public int ValueId { get; set; }
        public string? Value { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
    }

    public class StepEditInput
    {
        public string? NameOverride { get; set; }
        public List<ValueOverrideInput> Values { get; set; } = [];
    }

    [HttpPut("steps/{scheduleStepId:int}/edit")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> SaveStepEdit(int scheduleStepId, [FromBody] StepEditInput input)
    {
        var (row, schedule, step) = await LoadScheduleStepAsync(scheduleStepId, true);
        if (schedule.IsArchived) throw ApiException.Bad("This process schedule is archived. Restore it first.");
        var originals = await db.ProcessStepValues.AsNoTracking().Include(v => v.Characteristic).Include(v => v.Material)
            .Where(v => db.ProcessStepEntries.Any(e => e.Id == v.EntryId && e.ProcessStepId == step.Id))
            .ToDictionaryAsync(v => v.Id);
        var inputs = (input.Values ?? []).GroupBy(v => v.ValueId).Select(g => g.Last()).ToList();
        var materialIds = inputs.Select(v => int.TryParse(v.Value, out var m) ? m : 0).Where(m => m > 0).Distinct().ToList();
        var materials = await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

        var changes = new List<string>();
        var nameOverride = Text.Clean(input.NameOverride);
        if (nameOverride == step.Name) nameOverride = null;
        if (nameOverride is { Length: > 400 }) throw ApiException.Bad("Name cannot be longer than 400 characters.");
        if (nameOverride != row.NameOverride) changes.Add($"Name: {row.NameOverride ?? step.Name} → {nameOverride ?? step.Name}");
        row.NameOverride = nameOverride;

        foreach (var v in inputs)
        {
            if (!originals.TryGetValue(v.ValueId, out var orig) || orig.Characteristic == null)
                throw ApiException.Bad("A value does not belong to this step. Reload and try again.");
            var ch = orig.Characteristic;
            var value = Text.Clean(v.Value);
            if (value is { Length: > 4000 }) throw ApiException.Bad($"\"{ch.Name}\" cannot be longer than 4000 characters.");
            if (v.MinValue != null && v.MaxValue != null && v.MinValue > v.MaxValue)
                throw ApiException.Bad($"Min cannot be greater than Max for \"{ch.Name}\".");

            bool same;
            string display;
            if (ch.InputType == CharacteristicInputTypes.Material)
            {
                if (value != null)
                {
                    if (!int.TryParse(value, out var mid) || !materials.TryGetValue(mid, out var m) || m.GroupId != schedule.GroupId || m.IsDeleted)
                        throw ApiException.Bad($"The material selected for \"{ch.Name}\" is not valid.");
                    display = m.ProductName;
                }
                else display = orig.Material?.ProductName ?? "";
                same = value == null || value == orig.MaterialId?.ToString();
            }
            else
            {
                if (value != null && ch.InputType == CharacteristicInputTypes.Number && ScheduleCalculator.Num(value) == null)
                    throw ApiException.Bad($"\"{ch.Name}\" must be a number.");
                same = value == null || value == orig.Value;
                display = value ?? orig.Value ?? "";
            }

            var existing = row.Overrides.FirstOrDefault(o => o.ProcessStepValueId == v.ValueId);
            if (same && v.MinValue == null && v.MaxValue == null)
            {
                if (existing != null)
                {
                    db.ScheduleStepOverrides.Remove(existing);
                    changes.Add($"{ch.Name}: edit removed");
                }
                continue;
            }
            var newValue = same ? null : value;
            if (existing == null)
            {
                existing = new ScheduleStepOverride { ScheduleStepId = row.Id, ProcessStepValueId = v.ValueId };
                row.Overrides.Add(existing);
            }
            else if (existing.Value == newValue && existing.MinValue == v.MinValue && existing.MaxValue == v.MaxValue) continue;
            existing.Value = newValue;
            existing.MinValue = v.MinValue;
            existing.MaxValue = v.MaxValue;
            var range = v.MinValue == null && v.MaxValue == null ? "" : $" (range {(v.MinValue == null ? "—" : Fmt(v.MinValue.Value))} – {(v.MaxValue == null ? "—" : Fmt(v.MaxValue.Value))})";
            changes.Add($"{ch.Name}: {display}{range}");
        }

        schedule.UpdatedAt = DateTime.UtcNow;
        audit.Log(Entity, schedule.Id, "Step edited", $"{row.NameOverride ?? step.Name}" + (changes.Count == 0 ? " — no changes" : "\n" + string.Join("\n", changes)), schedule.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Step edits saved.", id = row.Id });
    }

    // ---------- Copy / archive ----------

    public class CopyInput
    {
        public string? NewName { get; set; }
        public string? NewNumber { get; set; }
    }

    private async Task<IActionResult> CopyAsync(int id, CopyInput input, bool withEdits)
    {
        var s = await LoadAsync(id);
        var name = Text.Req(input.NewName, "New Name");
        var number = Text.Req(input.NewNumber, "New Number");
        if (name.Length > 400 || number.Length > 400) throw ApiException.Bad("Values cannot be longer than 400 characters.");
        await EnsureUniqueNumberAsync(s.GroupId, number, null);
        var newId = await InTransactionAsync(async () =>
        {
            var created = await copier.CopySchedulesCoreAsync([id], s.GroupId, withEdits, name, number);
            var copy = created[0];
            var kind = withEdits ? "Copy Master" : "Clone";
            audit.Log(Entity, copy.Id, "Created", $"{kind} of \"{s.Name}\" (#{s.Number})", s.GroupId);
            audit.Log(Entity, s.Id, "Copied", $"{kind} → \"{name}\" (#{number})", s.GroupId);
            await db.SaveChangesAsync();
            return copy.Id;
        });
        return Ok(new { message = "Schedule Copied.", id = newId });
    }

    /// <summary>Clone: duplicate WITHOUT the schedule-level step edits.</summary>
    [HttpPost("{id:int}/clone")]
    [Authorize(Roles = Access.Process)]
    public Task<IActionResult> Clone(int id, [FromBody] CopyInput input) => CopyAsync(id, input, false);

    /// <summary>Copy Master: duplicate WITH the schedule-level step edits.</summary>
    [HttpPost("{id:int}/copy-master")]
    [Authorize(Roles = Access.Process)]
    public Task<IActionResult> CopyMaster(int id, [FromBody] CopyInput input) => CopyAsync(id, input, true);

    public class BulkCopyInput
    {
        public List<int> Ids { get; set; } = [];
        public int DestinationGroupId { get; set; }
    }

    [HttpPost("bulk-copy")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> BulkCopy([FromBody] BulkCopyInput input)
    {
        var ids = (input.Ids ?? []).Distinct().ToList();
        if (ids.Count == 0) throw ApiException.Bad("Select at least one process schedule to copy.");
        await me.EnsureGroupAsync(input.DestinationGroupId);
        var dest = await db.Groups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == input.DestinationGroupId && !g.IsDeleted)
                   ?? throw ApiException.NotFound("Destination group");
        var sources = await db.ProcessSchedules.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync();
        if (sources.Count != ids.Count) throw ApiException.NotFound("Process Schedule");
        foreach (var g in sources.Select(s => s.GroupId).Distinct()) await me.EnsureGroupAsync(g);

        await InTransactionAsync(async () =>
        {
            foreach (var s in sources.OrderBy(x => x.Id))
            {
                var number = s.Number;
                for (var n = 2; await db.ProcessSchedules.AnyAsync(x => x.GroupId == dest.Id && !x.IsArchived && x.Number == number); n++)
                    number = $"{s.Number} ({n})";
                var copy = (await copier.CopySchedulesCoreAsync([s.Id], dest.Id, true, s.Name, number))[0];
                audit.Log(Entity, copy.Id, "Created", $"Bulk copied from \"{s.Name}\" (#{s.Number})", dest.Id);
                audit.Log(Entity, s.Id, "Copied", $"Bulk copied to group \"{dest.Name}\" (#{number})", s.GroupId);
            }
            await db.SaveChangesAsync();
            return true;
        });
        var count = sources.Count;
        return Ok(new { message = $"Successfully copied {count} schedule{(count == 1 ? "" : "s")} to the {dest.Name} group.", count });
    }

    [HttpPost("{id:int}/archive")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Archive(int id)
    {
        me.EnsureAdmin();
        var s = await LoadAsync(id, includeArchived: true);
        if (!s.IsArchived)
        {
            s.IsArchived = true;
            s.UpdatedAt = DateTime.UtcNow;
            audit.Log(Entity, s.Id, "Archived", $"{s.Name} (#{s.Number})", s.GroupId);
            await db.SaveChangesAsync();
        }
        return Ok(new { message = "Process schedule archived." });
    }

    [HttpPost("{id:int}/restore")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Restore(int id)
    {
        me.EnsureAdmin();
        var s = await LoadAsync(id, includeArchived: true);
        if (s.IsArchived)
        {
            await EnsureUniqueNumberAsync(s.GroupId, s.Number, s.Id);
            var deletedSteps = await db.ProcessScheduleSteps.Where(x => x.ScheduleId == id && x.ProcessStep!.IsDeleted)
                .Select(x => x.ProcessStep!.Name).Distinct().ToListAsync();
            if (deletedSteps.Count > 0)
                throw ApiException.Bad("This schedule cannot be restored because it uses deleted process steps: " + string.Join(", ", deletedSteps) + ".");
            s.IsArchived = false;
            s.UpdatedAt = DateTime.UtcNow;
            audit.Log(Entity, s.Id, "Restored", $"{s.Name} (#{s.Number})", s.GroupId);
            await db.SaveChangesAsync();
        }
        return Ok(new { message = "Process schedule restored." });
    }

    // ---------- Print ----------

    /// <summary>GET /api/process-schedules/5/print — every step with its sub step passes and values (edits applied).</summary>
    [HttpGet("{id:int}/print")]
    [Authorize(Roles = Access.ProcessRead)]
    public async Task<IActionResult> Print(int id)
    {
        var s = await db.ProcessSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) ?? throw ApiException.NotFound("Process Schedule");
        await me.EnsureGroupAsync(s.GroupId);
        var groupName = await db.Groups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstAsync();
        var department = s.DepartmentId == null ? null : await db.Departments.Where(d => d.Id == s.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync();
        var rows = await db.ProcessScheduleSteps.AsNoTracking().Where(x => x.ScheduleId == id)
            .OrderBy(x => x.Ordering).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.ProcessStepId, x.NameOverride, StepName = x.ProcessStep!.Name, Sector = x.ProcessStep.IndustrySector!.Name })
            .ToListAsync();
        var values = await calc.LoadValuesAsync(id);

        var number = 0;
        var steps = rows.Select(r => new
        {
            Number = ++number, ScheduleStepId = r.Id, r.ProcessStepId, Name = r.NameOverride ?? r.StepName, OriginalName = r.StepName,
            IndustrySectorName = r.Sector,
            Passes = values.Where(v => v.ScheduleStepId == r.Id)
                .GroupBy(v => (v.SubStepSequence, v.Pass, v.SubStepLabel, v.SubStepName, v.SubStepInstruction))
                .OrderBy(g => g.Key.SubStepSequence).ThenBy(g => g.Key.Pass)
                .Select(g => new
                {
                    Label = g.Key.SubStepLabel, Name = g.Key.SubStepName, Instruction = g.Key.SubStepInstruction,
                    Values = g.Select(v => new
                    {
                        v.ProcessStepValueId, v.Characteristic, v.Unit, v.InputType, v.CalcVariable, v.Value, v.MaterialName,
                        Display = ProcessStepsController.Display(v.InputType, v.Value, v.Unit, v.MaterialName), v.MinValue, v.MaxValue,
                    }).ToList(),
                }).ToList(),
        }).ToList();

        return Ok(new
        {
            s.Id, s.Name, s.Number, s.CustomerName, s.GroupId, GroupName = groupName, DepartmentName = department, s.IsArchived,
            s.CreatedAt, s.UpdatedAt, PrintedAt = DateTime.UtcNow, Steps = steps,
        });
    }

    // ---------- Estimates: material quantities & pricing ----------

    /// <summary>GET /api/process-schedules/5/quantities?oneSided=20&amp;twoSided=20</summary>
    [HttpGet("{id:int}/quantities")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Quantities(int id, [FromQuery] decimal oneSided = 0, [FromQuery] decimal twoSided = 0)
    {
        await LoadAsync(id, includeArchived: true);
        var r = await calc.QuantitiesAsync(id, oneSided, twoSided);
        var values = await calc.LoadValuesAsync(id);
        return Ok(new
        {
            r.SquareFootage, r.ProductionHours, r.Materials, r.MaterialCostPerSqFt, r.HoursPerSqFt, r.OtherCostPerSqFt,
            TotalCost = r.Materials.Sum(m => m.Cost),
            HasCoverage = values.Any(v => v.CalcVariable == CalcVariables.Coverage && ScheduleCalculator.Num(v.Value) > 0),
            HasMaterials = values.Any(v => v.CalcVariable == CalcVariables.MaterialId && v.MaterialId != null),
            HasProductionRate = values.Any(v => v.CalcVariable == CalcVariables.ProductionRate && ScheduleCalculator.Num(v.Value) > 0),
        });
    }

    public class QuantitiesInput
    {
        public decimal OneSidedArea { get; set; }
        public decimal TwoSidedArea { get; set; }
    }

    [HttpPut("{id:int}/quantities")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> SaveQuantities(int id, [FromBody] QuantitiesInput input)
    {
        var s = await LoadAsync(id);
        if (input.OneSidedArea < 0 || input.TwoSidedArea < 0) throw ApiException.Bad("Surface areas cannot be negative.");
        audit.Log(Entity, s.Id, "Material quantities saved",
            $"One sided {Fmt(s.OneSidedArea)} → {Fmt(input.OneSidedArea)} sq ft, two sided {Fmt(s.TwoSidedArea)} → {Fmt(input.TwoSidedArea)} sq ft", s.GroupId);
        s.OneSidedArea = input.OneSidedArea;
        s.TwoSidedArea = input.TwoSidedArea;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Material quantities saved." });
    }

    private static void ValidatePricing(PricingInput p)
    {
        if (new[] { p.LaborRate, p.MarkUp, p.PremiumMarkUp, p.OneSidedComplexity, p.OneSidedArea, p.TwoSidedComplexity, p.TwoSidedArea, p.HighComplexity, p.HighComplexityArea }
            .Any(v => v < 0))
            throw ApiException.Bad("Pricing values cannot be negative.");
    }

    private async Task<object> PricingPayloadAsync(int id, PricingInput p)
    {
        var r = await calc.PriceAsync(id, p);
        var standard = r.Rows.Where(x => x.Label != "High Complexity").Sum(x => x.Cost);
        var high = r.Rows.Where(x => x.Label == "High Complexity").Sum(x => x.Cost);
        var markUpAmount = (standard + high) * p.MarkUp / 100m;
        var premiumAmount = high * (1 + p.MarkUp / 100m) * p.PremiumMarkUp / 100m;
        return new
        {
            r.TotalPrice, r.TotalSquareFootage, r.BaseCostPerSqFt, r.MaterialCostPerSqFt, r.LaborCostPerSqFt, r.OtherCostPerSqFt,
            r.Rows, r.Subtotal, MarkUpAmount = Math.Round(markUpAmount, 2), PremiumMarkUpAmount = Math.Round(premiumAmount, 2),
        };
    }

    /// <summary>POST /api/process-schedules/5/pricing — calculates a pricing scenario (nothing is saved).</summary>
    [HttpPost("{id:int}/pricing")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Pricing(int id, [FromBody] PricingInput input)
    {
        await LoadAsync(id, includeArchived: true);
        ValidatePricing(input);
        return Ok(await PricingPayloadAsync(id, input));
    }

    [HttpPut("{id:int}/pricing")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> SavePricing(int id, [FromBody] PricingInput input)
    {
        var s = await LoadAsync(id);
        ValidatePricing(input);
        var r = await calc.PriceAsync(id, input);
        audit.Log(Entity, s.Id, "Pricing saved",
            $"Labor ${Fmt(input.LaborRate)}/hr, mark-up {Fmt(input.MarkUp)}%, premium {Fmt(input.PremiumMarkUp)}%; " +
            $"areas {Fmt(input.OneSidedArea)}/{Fmt(input.TwoSidedArea)}/{Fmt(input.HighComplexityArea)} sq ft; total ${Fmt(s.TotalJobPrice)} → ${Fmt(r.TotalPrice)}", s.GroupId);
        s.LaborRate = input.LaborRate;
        s.MarkUp = input.MarkUp;
        s.PremiumMarkUp = input.PremiumMarkUp;
        s.OneSidedComplexity = input.OneSidedComplexity;
        s.OneSidedPriceArea = input.OneSidedArea;
        s.TwoSidedComplexity = input.TwoSidedComplexity;
        s.TwoSidedPriceArea = input.TwoSidedArea;
        s.HighComplexity = input.HighComplexity;
        s.HighComplexityArea = input.HighComplexityArea;
        s.TotalJobPrice = r.TotalPrice;
        s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Pricing saved.", totalPrice = r.TotalPrice });
    }

    /// <summary>GET /api/process-schedules/5/estimates — saved quantities + pricing inputs to prefill the pages.</summary>
    [HttpGet("{id:int}/estimates")]
    [Authorize(Roles = Access.Process)]
    public async Task<IActionResult> Estimates(int id)
    {
        var s = await LoadAsync(id, includeArchived: true);
        var groupName = await db.Groups.Where(g => g.Id == s.GroupId).Select(g => g.Name).FirstAsync();
        return Ok(new
        {
            s.Id, s.Name, s.Number, s.CustomerName, s.GroupId, GroupName = groupName, s.IsArchived,
            s.OneSidedArea, s.TwoSidedArea,
            s.LaborRate, s.MarkUp, s.PremiumMarkUp, s.OneSidedComplexity, OneSidedPriceArea = s.OneSidedPriceArea,
            s.TwoSidedComplexity, TwoSidedPriceArea = s.TwoSidedPriceArea, s.HighComplexity, s.HighComplexityArea, s.TotalJobPrice, s.UpdatedAt,
        });
    }
}
