using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Services;

public class GroupCopyOptions
{
    public bool Materials { get; set; } = true;
    public bool Formulas { get; set; } = true;
    public bool ProcessSteps { get; set; } = true;
    public bool ProcessSchedules { get; set; } = true;
    public bool Departments { get; set; } = true;
    public bool MyWorkSetup { get; set; } = true;
    public bool WorkInstructions { get; set; } = true;
    public bool PhotoGallery { get; set; } = true;
    public bool Vendors { get; set; } = true;
    public bool Documents { get; set; } = true;
}

/// <summary>
/// Copies data between groups. Used by "Copy Group" and every "Bulk Copy" action.
/// Categories, characteristics and materials are matched by name in the destination group
/// (and created when missing) so copied process steps keep working.
/// </summary>
public class GroupCopyService(AppDbContext db, FileStorage files, CurrentUser me)
{
    private readonly Dictionary<(int dest, int srcCategory), int> _categoryMap = new();
    private readonly Dictionary<(int dest, int srcCharacteristic), int> _characteristicMap = new();
    private readonly Dictionary<(int dest, int srcMaterial), int> _materialMap = new();
    private readonly Dictionary<(int dest, int srcStep), int> _stepMap = new();
    private readonly Dictionary<(int dest, int srcDepartment), int> _departmentMap = new();
    private readonly Dictionary<(int dest, int srcDocument), int> _documentMap = new();

    public async Task<Dictionary<string, int>> CountsAsync(int groupId) => new()
    {
        ["Materials"] = await db.Materials.CountAsync(m => m.GroupId == groupId && !m.IsDeleted && m.MaterialType != MaterialType.Equipment),
        ["Equipment"] = await db.Materials.CountAsync(m => m.GroupId == groupId && !m.IsDeleted && m.MaterialType == MaterialType.Equipment),
        ["Formulas"] = await db.Formulas.CountAsync(f => f.GroupId == groupId && !f.IsDeleted),
        ["Process Steps"] = await db.ProcessSteps.CountAsync(s => s.GroupId == groupId && !s.IsDeleted),
        ["Process Schedules"] = await db.ProcessSchedules.CountAsync(s => s.GroupId == groupId && !s.IsArchived),
        ["Departments"] = await db.Departments.CountAsync(d => d.GroupId == groupId),
        ["My Work Setup (Defects & Adders)"] = await db.DefectTypes.CountAsync(d => d.GroupId == groupId) + await db.AdderTypes.CountAsync(a => a.GroupId == groupId),
        ["Work Instructions"] = await db.WorkInstructions.CountAsync(w => w.GroupId == groupId && !w.IsDeleted),
        ["Photo Gallery"] = await db.Photos.CountAsync(p => p.GroupId == groupId),
        ["Vendors"] = await db.Vendors.CountAsync(v => v.GroupId == groupId && !v.IsDeleted),
        ["Documents"] = await db.Documents.CountAsync(d => d.GroupId == groupId),
    };

    public async Task<(Group group, Dictionary<string, int> copied)> CopyGroupAsync(int sourceId, string newName, GroupCopyOptions o)
    {
        var src = await db.Groups.FirstOrDefaultAsync(g => g.Id == sourceId && !g.IsDeleted) ?? throw ApiException.NotFound("Group");
        var dest = new Group
        {
            Name = newName, Address1 = src.Address1, Address2 = src.Address2, City = src.City, State = src.State,
            Zip = src.Zip, Country = src.Country, TimeZone = src.TimeZone, ChecklistDeletionEnabled = src.ChecklistDeletionEnabled,
            LogoFile = src.LogoFile == null ? null : await files.CopyAsync(src.LogoFile, "logos"),
        };
        var copied = new Dictionary<string, int>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // The new group is created inside the transaction so a failed copy leaves nothing behind.
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Groups.Add(dest);
            await db.SaveChangesAsync();
            if (o.Vendors)
                copied["Vendors"] = await CopyVendorsAsync(await db.Vendors.Where(v => v.GroupId == sourceId && !v.IsDeleted).Select(v => v.Id).ToListAsync(), dest.Id);
            if (o.Materials)
                copied["Equipment & Materials"] = await CopyMaterialsAsync(await db.Materials.Where(m => m.GroupId == sourceId && !m.IsDeleted).Select(m => m.Id).ToListAsync(), dest.Id);
            if (o.Formulas)
                copied["Formulas"] = await CopyFormulasAsync(await db.Formulas.Where(f => f.GroupId == sourceId && !f.IsDeleted).Select(f => f.Id).ToListAsync(), dest.Id);
            if (o.Departments)
                copied["Departments"] = await CopyDepartmentsAsync(sourceId, dest.Id);
            if (o.ProcessSteps || o.ProcessSchedules)
                copied["Process Steps"] = await CopyStepsAsync(await db.ProcessSteps.Where(s => s.GroupId == sourceId && !s.IsDeleted).Select(s => s.Id).ToListAsync(), dest.Id);
            if (o.ProcessSchedules)
                copied["Process Schedules"] = await CopySchedulesAsync(await db.ProcessSchedules.Where(s => s.GroupId == sourceId && !s.IsArchived).Select(s => s.Id).ToListAsync(), dest.Id, true);
            if (o.MyWorkSetup)
                copied["My Work Setup (Defects & Adders)"] = await CopyMyWorkSetupAsync(sourceId, dest.Id);
            if (o.WorkInstructions)
                copied["Work Instructions"] = await CopyWorkInstructionsAsync(await db.WorkInstructions.Where(w => w.GroupId == sourceId && !w.IsDeleted).Select(w => w.Id).ToListAsync(), dest.Id);
            if (o.PhotoGallery)
                copied["Photo Gallery"] = await CopyPhotosAsync(sourceId, dest.Id);
            if (o.Documents)
                copied["Documents"] = await CopyDocumentsAsync(sourceId, dest.Id);
            await tx.CommitAsync();
        });
        return (dest, copied);
    }

    // ---------- Categories / characteristics ----------

    public async Task<int> MapCategoryAsync(int srcCategoryId, int destGroup)
    {
        if (_categoryMap.TryGetValue((destGroup, srcCategoryId), out var id)) return id;
        var src = await db.MaterialCategories.Include(c => c.Characteristics).FirstAsync(c => c.Id == srcCategoryId);
        // Shared library categories (GroupId null) are used by every group as-is.
        if (src.GroupId == null || src.GroupId == destGroup) return _categoryMap[(destGroup, srcCategoryId)] = src.Id;

        var dest = await db.MaterialCategories.Include(c => c.Characteristics)
            .FirstOrDefaultAsync(c => c.GroupId == destGroup && c.Name == src.Name && c.MaterialType == src.MaterialType);
        if (dest == null)
        {
            dest = new MaterialCategory { GroupId = destGroup, Name = src.Name, MaterialType = src.MaterialType, Filter1 = src.Filter1, Filter2 = src.Filter2 };
            db.MaterialCategories.Add(dest);
        }
        foreach (var ch in src.Characteristics)
        {
            var match = dest.Characteristics.FirstOrDefault(c => c.Name == ch.Name);
            if (match == null)
            {
                match = new Characteristic { Name = ch.Name, Unit = ch.Unit, InputType = ch.InputType, CalcVariable = ch.CalcVariable, DefaultValue = ch.DefaultValue, Sequence = ch.Sequence };
                dest.Characteristics.Add(match);
            }
        }
        await db.SaveChangesAsync();
        foreach (var ch in src.Characteristics)
            _characteristicMap[(destGroup, ch.Id)] = dest.Characteristics.First(c => c.Name == ch.Name).Id;
        return _categoryMap[(destGroup, srcCategoryId)] = dest.Id;
    }

    private async Task<int> MapCharacteristicAsync(int srcCharacteristicId, int srcCategoryId, int destGroup)
    {
        if (_characteristicMap.TryGetValue((destGroup, srcCharacteristicId), out var id)) return id;
        await MapCategoryAsync(srcCategoryId, destGroup);
        return _characteristicMap.TryGetValue((destGroup, srcCharacteristicId), out id) ? id : srcCharacteristicId;
    }

    // ---------- Materials / vendors ----------

    public async Task<int> CopyMaterialsAsync(List<int> ids, int destGroup)
    {
        // Formula mirrors travel with their formula (CopyFormulasAsync / MapMaterialAsync), never as loose materials.
        var mirrors = (await db.Formulas.AsNoTracking().Where(f => f.MaterialId != null && ids.Contains(f.MaterialId.Value))
            .Select(f => f.MaterialId!.Value).ToListAsync()).ToHashSet();
        var count = 0;
        foreach (var id in ids.Where(i => !mirrors.Contains(i)))
        {
            await MapMaterialAsync(id, destGroup, forceNew: true);
            count++;
        }
        return count;
    }

    /// <summary>The material of <paramref name="destGroup"/> that corresponds to a material of another group (matched by name, created when missing).</summary>
    public Task<int> MapMaterialToGroupAsync(int srcMaterialId, int destGroup) => MapMaterialAsync(srcMaterialId, destGroup);

    private async Task<int> MapMaterialAsync(int srcMaterialId, int destGroup, bool forceNew = false)
    {
        if (_materialMap.TryGetValue((destGroup, srcMaterialId), out var id)) return id;
        var src = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == srcMaterialId);
        if (src.GroupId == destGroup && !forceNew) return src.Id;

        // A formula's mirror maps to the same formula in the destination group (matched by number, copied when missing).
        if (src.MaterialType == MaterialType.Formula)
        {
            var formula = await db.Formulas.AsNoTracking().FirstOrDefaultAsync(f => f.MaterialId == src.Id);
            if (formula != null) return _materialMap[(destGroup, srcMaterialId)] = await MapFormulaMirrorAsync(formula, destGroup);
        }

        if (!forceNew)
        {
            var existing = await db.Materials.FirstOrDefaultAsync(m => m.GroupId == destGroup && !m.IsDeleted &&
                m.MaterialType == src.MaterialType && m.ProductName == src.ProductName && m.ProductCode == src.ProductCode);
            if (existing != null) return _materialMap[(destGroup, srcMaterialId)] = existing.Id;
        }
        var copy = new Material
        {
            GroupId = destGroup, MaterialType = src.MaterialType,
            CategoryId = src.CategoryId == null ? null : await MapCategoryAsync(src.CategoryId.Value, destGroup),
            ProductCode = src.ProductCode, ProductName = src.ProductName, Density = src.Density, Price = src.Price,
            Voc = src.Voc, Hap = src.Hap, Tap = src.Tap, MinQuantity = src.MinQuantity, Notes = src.Notes,
        };
        db.Materials.Add(copy);
        await db.SaveChangesAsync();
        return _materialMap[(destGroup, srcMaterialId)] = copy.Id;
    }

    public async Task<int> CopyVendorsAsync(List<int> ids, int destGroup)
    {
        var src = await db.Vendors.AsNoTracking().Where(v => ids.Contains(v.Id)).ToListAsync();
        foreach (var v in src)
        {
            v.Id = 0;
            v.GroupId = destGroup;
            db.Vendors.Add(v);
        }
        await db.SaveChangesAsync();
        return src.Count;
    }

    // ---------- Formulas ----------

    /// <summary>
    /// Copies formulas (with their mirror material and linked documents — documents of another group are copied into
    /// <paramref name="destGroup"/>, documents of the same group are simply linked).
    /// </summary>
    public async Task<int> CopyFormulasAsync(List<int> ids, int destGroup)
    {
        var src = await db.Formulas.AsNoTracking().Include(f => f.Ingredients).Where(f => ids.Contains(f.Id)).ToListAsync();
        foreach (var f in src)
            await CopyFormulaAsync(f, destGroup);
        return src.Count;
    }

    private async Task<Formula> CopyFormulaAsync(Formula f, int destGroup)
    {
        var copy = new Formula
        {
            GroupId = destGroup, CategoryId = f.CategoryId == null ? null : await MapCategoryAsync(f.CategoryId.Value, destGroup),
            Name = f.Name, Number = f.Number, CustomerName = f.CustomerName, IsComplete = f.IsComplete, BatchSize = f.BatchSize,
            BatchType = f.BatchType, BatchValue = f.BatchValue, EmployeeName = f.EmployeeName, PurchaseOrderNumber = f.PurchaseOrderNumber,
            MixedOn = DateTime.UtcNow,
            ContainerType = f.ContainerType, ContainerPrice = f.ContainerPrice, MarkUp = f.MarkUp, Substrate = f.Substrate, Notes = f.Notes,
            SpinDeltaL = f.SpinDeltaL, SpinDeltaA = f.SpinDeltaA, SpinDeltaB = f.SpinDeltaB, SpinDeltaE = f.SpinDeltaE,
            SpexDeltaL = f.SpexDeltaL, SpexDeltaA = f.SpexDeltaA, SpexDeltaB = f.SpexDeltaB, SpexDeltaE = f.SpexDeltaE,
            CreatedBy = me.Id == 0 ? null : me.Id,
        };
        foreach (var i in f.Ingredients.OrderBy(i => i.Sequence))
            copy.Ingredients.Add(new FormulaIngredient { MaterialId = await MapMaterialAsync(i.MaterialId, destGroup), Grams = i.Grams, DispenseAmount = i.Grams, Sequence = i.Sequence });
        await FormulaMirror.SyncAsync(db, copy);
        db.Formulas.Add(copy);
        await db.SaveChangesAsync();
        if (f.MaterialId is { } srcMirror && copy.MaterialId is { } destMirror) _materialMap[(destGroup, srcMirror)] = destMirror;

        var docIds = await db.DocumentLinks.AsNoTracking().Where(l => l.EntityType == LinkEntityTypes.Formula && l.EntityId == f.Id)
            .Select(l => l.DocumentId).Distinct().ToListAsync();
        foreach (var docId in docIds)
            db.DocumentLinks.Add(new DocumentLink { DocumentId = await MapDocumentAsync(docId, destGroup), EntityType = LinkEntityTypes.Formula, EntityId = copy.Id });
        if (docIds.Count > 0) await db.SaveChangesAsync();
        return copy;
    }

    /// <summary>Mirror material of the formula matching <paramref name="src"/> in <paramref name="destGroup"/> (same number), copying the formula when missing.</summary>
    private async Task<int> MapFormulaMirrorAsync(Formula src, int destGroup)
    {
        var number = src.Number?.Trim();
        var match = number == null ? null : await db.Formulas.Include(f => f.Ingredients)
            .FirstOrDefaultAsync(f => f.GroupId == destGroup && !f.IsDeleted && f.Number != null && f.Number.Trim() == number);
        if (match == null)
        {
            var full = await db.Formulas.AsNoTracking().Include(f => f.Ingredients).FirstAsync(f => f.Id == src.Id);
            match = await CopyFormulaAsync(full, destGroup);
        }
        else if (match.MaterialId == null)
        {
            await FormulaMirror.SyncAsync(db, match);
            await db.SaveChangesAsync();
        }
        return match.MaterialId!.Value;
    }

    /// <summary>The document of <paramref name="destGroup"/> that corresponds to <paramref name="srcDocumentId"/> (copied with its file once per run).</summary>
    public async Task<int> MapDocumentAsync(int srcDocumentId, int destGroup)
    {
        if (_documentMap.TryGetValue((destGroup, srcDocumentId), out var id)) return id;
        var d = await db.Documents.AsNoTracking().FirstAsync(x => x.Id == srcDocumentId);
        if (d.GroupId == destGroup) return _documentMap[(destGroup, srcDocumentId)] = d.Id;
        var copy = new Document
        {
            GroupId = destGroup, Name = d.Name, FileName = d.FileName, ContentType = d.ContentType, FileSize = d.FileSize, CreatedBy = me.Id == 0 ? null : me.Id,
            StoredFile = d.StoredFile == null ? null : await files.CopyAsync(d.StoredFile, $"documents/{destGroup}"),
        };
        db.Documents.Add(copy);
        await db.SaveChangesAsync();
        return _documentMap[(destGroup, srcDocumentId)] = copy.Id;
    }

    // ---------- Process steps / schedules ----------

    public async Task<int> CopyStepsAsync(List<int> ids, int destGroup, string? newName = null)
    {
        var src = await db.ProcessSteps.AsNoTracking()
            .Include(s => s.Entries).ThenInclude(e => e.Values).ThenInclude(v => v.Characteristic)
            .Where(s => ids.Contains(s.Id)).ToListAsync();
        foreach (var s in src)
            await CopyStepAsync(s, destGroup, newName ?? s.Name);
        return src.Count;
    }

    private async Task<int> CopyStepAsync(ProcessStep s, int destGroup, string name)
    {
        var copy = new ProcessStep { GroupId = destGroup, Name = name, IndustrySectorId = s.IndustrySectorId };
        // Same ordering as ValueIdMapAsync, which maps schedule overrides onto the copied values by position.
        foreach (var e in s.Entries.OrderBy(e => e.SubStepId).ThenBy(e => e.Pass).ThenBy(e => e.Id))
        {
            var entry = new ProcessStepEntry
            {
                SubStepId = e.SubStepId, Pass = e.Pass, PullDownId = e.PullDownId,
                CategoryId = e.CategoryId == null ? null : await MapCategoryAsync(e.CategoryId.Value, destGroup),
            };
            foreach (var v in e.Values.OrderBy(v => v.Id))
            {
                entry.Values.Add(new ProcessStepValue
                {
                    CharacteristicId = v.Characteristic == null ? v.CharacteristicId : await MapCharacteristicAsync(v.CharacteristicId, v.Characteristic.CategoryId, destGroup),
                    Value = v.Value,
                    MaterialId = v.MaterialId == null ? null : await MapMaterialAsync(v.MaterialId.Value, destGroup),
                });
            }
            copy.Entries.Add(entry);
        }
        db.ProcessSteps.Add(copy);
        await db.SaveChangesAsync();
        _stepMap[(destGroup, s.Id)] = copy.Id;
        return copy.Id;
    }

    private async Task<int> MapStepAsync(int srcStepId, int destGroup)
    {
        if (_stepMap.TryGetValue((destGroup, srcStepId), out var id)) return id;
        var s = await db.ProcessSteps.AsNoTracking()
            .Include(x => x.Entries).ThenInclude(e => e.Values).ThenInclude(v => v.Characteristic)
            .FirstAsync(x => x.Id == srcStepId);
        if (s.GroupId == destGroup) return s.Id;
        return await CopyStepAsync(s, destGroup, s.Name);
    }

    /// <summary>Copies schedules. <paramref name="withEdits"/> = "Copy Master" (keeps schedule-level step edits); false = "Clone".</summary>
    public async Task<List<ProcessSchedule>> CopySchedulesCoreAsync(List<int> ids, int destGroup, bool withEdits, string? newName = null, string? newNumber = null)
    {
        var src = await db.ProcessSchedules.AsNoTracking().Include(s => s.Steps).ThenInclude(st => st.Overrides)
            .Where(s => ids.Contains(s.Id)).ToListAsync();
        var created = new List<ProcessSchedule>();
        foreach (var s in src)
        {
            var copy = new ProcessSchedule
            {
                GroupId = destGroup, Name = newName ?? s.Name, Number = newNumber ?? s.Number, CustomerName = s.CustomerName,
                DepartmentId = s.GroupId == destGroup ? s.DepartmentId : (s.DepartmentId == null ? null : _departmentMap.GetValueOrDefault((destGroup, s.DepartmentId.Value)) is var d and > 0 ? d : null),
                OneSidedArea = s.OneSidedArea, TwoSidedArea = s.TwoSidedArea, LaborRate = s.LaborRate, MarkUp = s.MarkUp,
                PremiumMarkUp = s.PremiumMarkUp, OneSidedComplexity = s.OneSidedComplexity, OneSidedPriceArea = s.OneSidedPriceArea,
                TwoSidedComplexity = s.TwoSidedComplexity, TwoSidedPriceArea = s.TwoSidedPriceArea, HighComplexity = s.HighComplexity,
                HighComplexityArea = s.HighComplexityArea, TotalJobPrice = s.TotalJobPrice,
            };
            foreach (var st in s.Steps.OrderBy(x => x.Ordering))
            {
                var stepId = await MapStepAsync(st.ProcessStepId, destGroup);
                var newStep = new ProcessScheduleStep { ProcessStepId = stepId, Ordering = st.Ordering, NameOverride = withEdits ? st.NameOverride : null };
                if (withEdits && st.Overrides.Count > 0)
                {
                    // Override rows point at value ids of the step; when the step was copied, remap by position.
                    var valueMap = await ValueIdMapAsync(st.ProcessStepId, stepId);
                    foreach (var o in st.Overrides)
                        if (valueMap.TryGetValue(o.ProcessStepValueId, out var newValueId))
                            newStep.Overrides.Add(new ScheduleStepOverride { ProcessStepValueId = newValueId, Value = o.Value, MinValue = o.MinValue, MaxValue = o.MaxValue });
                }
                copy.Steps.Add(newStep);
            }
            db.ProcessSchedules.Add(copy);
            created.Add(copy);
        }
        await db.SaveChangesAsync();
        return created;
    }

    public async Task<int> CopySchedulesAsync(List<int> ids, int destGroup, bool withEdits) =>
        (await CopySchedulesCoreAsync(ids, destGroup, withEdits)).Count;

    private async Task<Dictionary<int, int>> ValueIdMapAsync(int srcStepId, int destStepId)
    {
        if (srcStepId == destStepId)
            return await db.ProcessStepValues.Where(v => db.ProcessStepEntries.Any(e => e.Id == v.EntryId && e.ProcessStepId == srcStepId))
                .ToDictionaryAsync(v => v.Id, v => v.Id);
        async Task<List<int>> Ordered(int stepId) => await db.ProcessStepEntries.Where(e => e.ProcessStepId == stepId)
            .OrderBy(e => e.SubStepId).ThenBy(e => e.Pass).ThenBy(e => e.Id)
            .SelectMany(e => e.Values.OrderBy(v => v.Id).Select(v => v.Id)).ToListAsync();
        var a = await Ordered(srcStepId);
        var b = await Ordered(destStepId);
        return a.Zip(b).ToDictionary(p => p.First, p => p.Second);
    }

    // ---------- Other group data ----------

    private async Task<int> CopyDepartmentsAsync(int sourceGroup, int destGroup)
    {
        var src = await db.Departments.AsNoTracking().Where(d => d.GroupId == sourceGroup).ToListAsync();
        var pairs = src.Select(d => (d.Id, copy: new Department { GroupId = destGroup, Name = d.Name })).ToList();
        db.Departments.AddRange(pairs.Select(p => p.copy));
        await db.SaveChangesAsync();
        foreach (var (srcId, copy) in pairs) _departmentMap[(destGroup, srcId)] = copy.Id;
        return src.Count;
    }

    private async Task<int> CopyMyWorkSetupAsync(int sourceGroup, int destGroup)
    {
        var defects = await db.DefectTypes.AsNoTracking().Where(d => d.GroupId == sourceGroup).ToListAsync();
        var adders = await db.AdderTypes.AsNoTracking().Where(d => d.GroupId == sourceGroup).ToListAsync();
        db.DefectTypes.AddRange(defects.Select(d => new DefectType { GroupId = destGroup, Name = d.Name, ChartColor = d.ChartColor, IsArchived = d.IsArchived }));
        db.AdderTypes.AddRange(adders.Select(a => new AdderType { GroupId = destGroup, Name = a.Name, IsArchived = a.IsArchived }));
        await db.SaveChangesAsync();
        return defects.Count + adders.Count;
    }

    public async Task<int> CopyWorkInstructionsAsync(List<int> ids, int destGroup, string? newName = null, string? newNumber = null)
    {
        var src = await db.WorkInstructions.AsNoTracking()
            .Include(w => w.Steps).ThenInclude(s => s.Media).Include(w => w.RelatedDocuments).Include(w => w.Items)
            .Where(w => ids.Contains(w.Id)).ToListAsync();
        foreach (var w in src)
        {
            var copy = new WorkInstruction
            {
                GroupId = destGroup, DocumentNumber = newNumber ?? w.DocumentNumber, Name = newName ?? w.Name, IssueDate = DateTime.UtcNow,
                Version = 1, Controlled = w.Controlled, Location = w.Location, Purpose = w.Purpose, Scope = w.Scope, Terminology = w.Terminology,
            };
            copy.Trail.Add(new WorkInstructionTrail { Version = copy.StatusText, Author = me.UserName, Log = $"Copied from #{w.DocumentNumber} {w.Name}" });
            foreach (var s in w.Steps.OrderBy(s => s.Level))
            {
                var step = new WorkInstructionStep { Level = s.Level, Title = s.Title, Body = s.Body };
                foreach (var m in s.Media)
                    step.Media.Add(new WorkInstructionMedia { FileName = m.FileName, ContentType = m.ContentType, StoredFile = await files.CopyAsync(m.StoredFile, $"work-instructions/{destGroup}") });
                copy.Steps.Add(step);
            }
            foreach (var r in w.RelatedDocuments)
                copy.RelatedDocuments.Add(new WorkInstructionRelatedDoc { DocumentNumber = r.DocumentNumber, DocumentName = r.DocumentName, Author = r.Author });
            foreach (var i in w.Items)
                copy.Items.Add(new WorkInstructionItem
                {
                    Kind = i.Kind, Description = i.Description,
                    MaterialId = i.MaterialId == null ? null : await MapMaterialAsync(i.MaterialId.Value, destGroup),
                });
            db.WorkInstructions.Add(copy);
        }
        await db.SaveChangesAsync();
        return src.Count;
    }

    private async Task<int> CopyPhotosAsync(int sourceGroup, int destGroup)
    {
        var src = await db.Photos.AsNoTracking().Include(p => p.Tags).Where(p => p.GroupId == sourceGroup).ToListAsync();
        foreach (var p in src)
        {
            db.Photos.Add(new Photo
            {
                GroupId = destGroup, Name = p.Name, ContentType = p.ContentType, CreatedBy = me.Id,
                StoredFile = await files.CopyAsync(p.StoredFile, $"photos/{destGroup}"),
                Tags = p.Tags.Select(t => new PhotoTag { Tag = t.Tag }).ToList(),
            });
        }
        await db.SaveChangesAsync();
        return src.Count;
    }

    private async Task<int> CopyDocumentsAsync(int sourceGroup, int destGroup)
    {
        // Documents already copied with their formulas are reused, so nothing is duplicated.
        var src = await db.Documents.AsNoTracking().Where(d => d.GroupId == sourceGroup).Select(d => d.Id).ToListAsync();
        foreach (var id in src)
            await MapDocumentAsync(id, destGroup);
        return src.Count;
    }
}
