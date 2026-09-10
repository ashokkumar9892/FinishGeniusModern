using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Central document library ("Docs" on Equipment &amp; Materials) and the per-object "Documents" action.
/// A document is uploaded once per group and linked to any number of objects of the same group
/// (materials, formulas, process steps, process schedules, and the group-level Pricing / Material Quantities pages).
/// </summary>
[ApiController]
[Authorize]
[Route("api/documents")]
public class DocumentsController(AppDbContext db, CurrentUser me, AuditService audit, FileStorage files) : ControllerBase
{
    public const long MaxFileSize = 100L * 1024 * 1024;
    private const string AuditType = "Document";

    public class CreateDocumentForm
    {
        public int GroupId { get; set; }
        public string? Name { get; set; }
        public IFormFile? File { get; set; }
        public string? EntityType { get; set; }
        public int? EntityId { get; set; }
    }

    public class UpdateDocumentForm
    {
        public string? Name { get; set; }
        public IFormFile? File { get; set; }
    }

    public record LinkRequest(string EntityType, int EntityId);

    public record LinkDto(string EntityType, int EntityId, string Label);

    public record DocumentDto(
        int Id, int GroupId, string GroupName, string Name, string? FileName, string? StoredFile, string? ContentType,
        long FileSize, DateTime CreatedAt, DateTime? UpdatedAt, string? CreatedByName, List<LinkDto> Links);

    // ------------------------------------------------------------------ queries

    /// <summary>
    /// GET /api/documents?groupId=2&amp;entityType=Material&amp;entityId=15&amp;search=sds —
    /// documents of the group; with entityType + entityId only those linked to that object
    /// (entityId 0 = group-level link, e.g. Pricing / Material Quantities).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? groupId, [FromQuery] string? entityType, [FromQuery] int? entityId, [FromQuery] string? search)
    {
        var scope = await me.ScopeAsync(groupId);
        var q = db.Documents.AsNoTracking().Where(d => scope.Contains(d.GroupId));
        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var type = NormalizeType(entityType);
            var id = entityId ?? 0;
            q = q.Where(d => d.Links.Any(l => l.EntityType == type && l.EntityId == id));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(d => d.Name.Contains(s) || (d.FileName != null && d.FileName.Contains(s)));
        }
        return Ok(await ProjectAsync(q.OrderByDescending(d => d.Id)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var doc = await LoadAsync(id);
        return Ok((await ProjectAsync(db.Documents.AsNoTracking().Where(d => d.Id == doc.Id))).Single());
    }

    /// <summary>
    /// GET /api/documents/link-targets?groupId=2 — everything a document can be linked to, in one round-trip
    /// (lean projections only; the legacy Link button loaded full objects and was slow).
    /// </summary>
    [HttpGet("link-targets")]
    public async Task<IActionResult> LinkTargets([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var materials = await db.Materials.AsNoTracking()
            .Where(m => m.GroupId == groupId && !m.IsDeleted)
            .OrderBy(m => m.ProductName)
            .Select(m => new { m.Id, Name = m.ProductName, m.ProductCode, MaterialType = (int)m.MaterialType })
            .ToListAsync();
        var formulas = await db.Formulas.AsNoTracking()
            .Where(f => f.GroupId == groupId && !f.IsDeleted)
            .OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, f.Number })
            .ToListAsync();
        var processSteps = await db.ProcessSteps.AsNoTracking()
            .Where(s => s.GroupId == groupId && !s.IsDeleted)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync();
        var processSchedules = await db.ProcessSchedules.AsNoTracking()
            .Where(s => s.GroupId == groupId && !s.IsArchived)
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Number })
            .ToListAsync();
        return Ok(new { materials, formulas, processSteps, processSchedules });
    }

    // ------------------------------------------------------------------ create / update / copy / delete

    /// <summary>POST /api/documents (multipart: groupId, name, file, entityType?, entityId?) — upload and optionally link.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxFileSize + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize + 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] CreateDocumentForm form)
    {
        await me.EnsureGroupAsync(form.GroupId);
        var file = ValidateFile(form.File, required: true)!;
        var name = CleanName(form.Name) ?? Path.GetFileName(file.FileName);

        string? linkType = null;
        var linkId = 0;
        if (!string.IsNullOrWhiteSpace(form.EntityType))
        {
            linkType = NormalizeType(form.EntityType);
            linkId = form.EntityId ?? 0;
            await EnsureTargetsAsync(form.GroupId, [(linkType, linkId)]);
        }

        var stored = await files.SaveAsync(file, $"documents/{form.GroupId}");
        var doc = new Document
        {
            GroupId = form.GroupId, Name = name, FileName = Path.GetFileName(file.FileName), StoredFile = stored,
            ContentType = ContentTypeOf(file), FileSize = file.Length, CreatedBy = me.Id == 0 ? null : me.Id,
        };
        if (linkType != null) doc.Links.Add(new DocumentLink { EntityType = linkType, EntityId = linkId });
        db.Documents.Add(doc);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            files.Delete(stored);
            throw;
        }

        var labels = linkType == null ? null : await LabelsAsync([(linkType, linkId)]);
        audit.Log(AuditType, doc.Id, "Created", $"Name: {doc.Name}\nFile: {doc.FileName} ({FormatSize(doc.FileSize)})" +
            (labels == null ? "" : $"\nLinked to {labels[(linkType!, linkId)]}"), doc.GroupId);
        await db.SaveChangesAsync();

        return Ok(new { message = linkType == null ? "Document created." : "Document uploaded and linked.", id = doc.Id, document = await DtoAsync(doc.Id) });
    }

    /// <summary>PUT /api/documents/{id} (multipart: name, file?) — rename and/or replace the file (the old file is deleted).</summary>
    [HttpPut("{id:int}")]
    [RequestSizeLimit(MaxFileSize + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxFileSize + 1024 * 1024)]
    public async Task<IActionResult> Update(int id, [FromForm] UpdateDocumentForm form)
    {
        var doc = await LoadAsync(id, tracked: true);
        var name = CleanName(form.Name) ?? throw ApiException.Bad("Name is required.");
        var file = ValidateFile(form.File, required: false);

        var changes = new List<string>();
        if (name != doc.Name) changes.Add($"Name: {doc.Name} → {name}");
        doc.Name = name;

        string? oldFile = null;
        if (file != null)
        {
            var stored = await files.SaveAsync(file, $"documents/{doc.GroupId}");
            changes.Add($"File replaced: {doc.FileName} → {Path.GetFileName(file.FileName)} ({FormatSize(file.Length)})");
            oldFile = doc.StoredFile;
            doc.StoredFile = stored;
            doc.FileName = Path.GetFileName(file.FileName);
            doc.ContentType = ContentTypeOf(file);
            doc.FileSize = file.Length;
        }
        doc.UpdatedAt = DateTime.UtcNow;
        audit.Log(AuditType, doc.Id, "Updated", changes.Count == 0 ? "No changes" : string.Join("\n", changes), doc.GroupId);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            if (file != null) files.Delete(doc.StoredFile);
            throw;
        }
        if (oldFile != null && oldFile != doc.StoredFile) files.Delete(oldFile);
        return Ok(new { message = "Document updated.", id = doc.Id, document = await DtoAsync(doc.Id) });
    }

    /// <summary>POST /api/documents/{id}/copy — duplicates the document and its file (links are not copied).</summary>
    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id)
    {
        var src = await LoadAsync(id);
        var copy = new Document
        {
            GroupId = src.GroupId, Name = Truncate($"{src.Name} (Copy)", 400), FileName = src.FileName, ContentType = src.ContentType,
            FileSize = src.FileSize, CreatedBy = me.Id == 0 ? null : me.Id,
            StoredFile = src.StoredFile == null ? null : await files.CopyAsync(src.StoredFile, $"documents/{src.GroupId}"),
        };
        db.Documents.Add(copy);
        await db.SaveChangesAsync();
        audit.Log(AuditType, copy.Id, "Copied", $"Copied from #{src.Id} {src.Name}", copy.GroupId);
        audit.Log(AuditType, src.Id, "Copied", $"Copied to #{copy.Id} {copy.Name}", src.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Document copied.", id = copy.Id, document = await DtoAsync(copy.Id) });
    }

    /// <summary>DELETE /api/documents/{id} — removes the document, all of its links and the stored file.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var doc = await LoadAsync(id, tracked: true);
        await db.Entry(doc).Collection(d => d.Links).LoadAsync();
        var linkCount = doc.Links.Count;
        db.DocumentLinks.RemoveRange(doc.Links);
        db.Documents.Remove(doc);
        audit.Log(AuditType, doc.Id, "Deleted", $"Name: {doc.Name}\nFile: {doc.FileName}\nLinks removed: {linkCount}", doc.GroupId);
        await db.SaveChangesAsync();
        files.Delete(doc.StoredFile);
        return Ok(new { message = "Document deleted." });
    }

    // ------------------------------------------------------------------ links

    [HttpGet("{id:int}/links")]
    public async Task<IActionResult> Links(int id)
    {
        var doc = await LoadAsync(id);
        var links = await db.DocumentLinks.AsNoTracking().Where(l => l.DocumentId == doc.Id)
            .Select(l => new { l.EntityType, l.EntityId }).ToListAsync();
        var labels = await LabelsAsync(links.Select(l => (l.EntityType, l.EntityId)));
        return Ok(links.Select(l => new LinkDto(l.EntityType, l.EntityId, labels[(l.EntityType, l.EntityId)]))
            .OrderBy(l => l.EntityType).ThenBy(l => l.Label));
    }

    /// <summary>PUT /api/documents/{id}/links — replaces every link of the document (the "Link" modal).</summary>
    [HttpPut("{id:int}/links")]
    public async Task<IActionResult> ReplaceLinks(int id, [FromBody] List<LinkRequest>? body)
    {
        var doc = await LoadAsync(id, tracked: true);
        var wanted = (body ?? []).Select(l => (Type: NormalizeType(l.EntityType), l.EntityId)).Distinct().ToList();
        if (wanted.Count > 5000) throw ApiException.Bad("Too many links in one request.");

        var existing = await db.DocumentLinks.Where(l => l.DocumentId == doc.Id).ToListAsync();
        var existingKeys = existing.Select(l => (l.EntityType, l.EntityId)).ToHashSet();
        var wantedKeys = wanted.ToHashSet();
        var removed = existing.Where(l => !wantedKeys.Contains((l.EntityType, l.EntityId))).ToList();
        var added = wanted.Where(w => !existingKeys.Contains(w)).ToList();
        // Only new links are validated, so existing links to objects deleted since (still shown as "(deleted)") can be kept.
        await EnsureTargetsAsync(doc.GroupId, added);

        db.DocumentLinks.RemoveRange(removed);
        db.DocumentLinks.AddRange(added.Select(a => new DocumentLink { DocumentId = doc.Id, EntityType = a.Type, EntityId = a.EntityId }));

        if (added.Count > 0 || removed.Count > 0)
        {
            var labels = await LabelsAsync(added.Concat(removed.Select(r => (r.EntityType, r.EntityId))));
            var details = new List<string>();
            if (added.Count > 0) details.Add("Linked: " + string.Join(", ", added.Select(a => labels[a])));
            if (removed.Count > 0) details.Add("Unlinked: " + string.Join(", ", removed.Select(r => labels[(r.EntityType, r.EntityId)])));
            audit.Log(AuditType, doc.Id, "Links updated", string.Join("\n", details), doc.GroupId);
            doc.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        return Ok(new { message = "Document links saved.", linkCount = wanted.Count, added = added.Count, removed = removed.Count });
    }

    /// <summary>POST /api/documents/{id}/link — links the document to one object (idempotent).</summary>
    [HttpPost("{id:int}/link")]
    public async Task<IActionResult> AddLink(int id, [FromBody] LinkRequest body)
    {
        var doc = await LoadAsync(id);
        var type = NormalizeType(body.EntityType);
        await EnsureTargetsAsync(doc.GroupId, [(type, body.EntityId)]);
        var exists = await db.DocumentLinks.AnyAsync(l => l.DocumentId == doc.Id && l.EntityType == type && l.EntityId == body.EntityId);
        if (exists) return Ok(new { message = "Document is already linked." });

        db.DocumentLinks.Add(new DocumentLink { DocumentId = doc.Id, EntityType = type, EntityId = body.EntityId });
        var labels = await LabelsAsync([(type, body.EntityId)]);
        audit.Log(AuditType, doc.Id, "Linked", $"Linked to {labels[(type, body.EntityId)]}", doc.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Document linked." });
    }

    /// <summary>DELETE /api/documents/{id}/link?entityType=Material&amp;entityId=15 — removes one link (the document stays in the library).</summary>
    [HttpDelete("{id:int}/link")]
    public async Task<IActionResult> RemoveLink(int id, [FromQuery] string entityType, [FromQuery] int entityId)
    {
        var doc = await LoadAsync(id);
        var type = NormalizeType(entityType);
        var links = await db.DocumentLinks.Where(l => l.DocumentId == doc.Id && l.EntityType == type && l.EntityId == entityId).ToListAsync();
        if (links.Count == 0) throw ApiException.NotFound("Document link");
        db.DocumentLinks.RemoveRange(links);
        var labels = await LabelsAsync([(type, entityId)]);
        audit.Log(AuditType, doc.Id, "Unlinked", $"Unlinked from {labels[(type, entityId)]}", doc.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Document unlinked." });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Document> LoadAsync(int id, bool tracked = false)
    {
        var q = tracked ? db.Documents : db.Documents.AsNoTracking();
        var doc = await q.FirstOrDefaultAsync(d => d.Id == id) ?? throw ApiException.NotFound("Document");
        await me.EnsureGroupAsync(doc.GroupId);
        return doc;
    }

    private async Task<DocumentDto> DtoAsync(int id) =>
        (await ProjectAsync(db.Documents.AsNoTracking().Where(d => d.Id == id))).Single();

    private async Task<List<DocumentDto>> ProjectAsync(IQueryable<Document> q)
    {
        var rows = await q.Select(d => new
        {
            d.Id, d.GroupId, GroupName = d.Group!.Name, d.Name, d.FileName, d.StoredFile, d.ContentType, d.FileSize,
            d.CreatedAt, d.UpdatedAt,
            Creator = db.Users.Where(u => u.Id == d.CreatedBy).Select(u => new { u.FirstName, u.LastName, u.Username }).FirstOrDefault(),
            Links = d.Links.Select(l => new { l.EntityType, l.EntityId }).ToList(),
        }).AsSplitQuery().ToListAsync();

        var labels = new Dictionary<(string, int), string>();
        foreach (var grp in rows.GroupBy(r => r.GroupId))
            foreach (var kv in await LabelsAsync(grp.SelectMany(r => r.Links.Select(l => (l.EntityType, l.EntityId)))))
                labels[kv.Key] = kv.Value;

        return rows.Select(r => new DocumentDto(
            r.Id, r.GroupId, r.GroupName, r.Name, r.FileName, r.StoredFile, r.ContentType, r.FileSize, r.CreatedAt, r.UpdatedAt,
            r.Creator == null ? null : (Text.Clean($"{r.Creator.FirstName} {r.Creator.LastName}") ?? r.Creator.Username),
            r.Links.Select(l => new LinkDto(l.EntityType, l.EntityId, labels.GetValueOrDefault((l.EntityType, l.EntityId), $"{l.EntityType} #{l.EntityId}")))
                .OrderBy(l => l.EntityType).ThenBy(l => l.Label).ToList())).ToList();
    }

    /// <summary>Human-readable names for link targets, fetched with one query per entity type.</summary>
    private async Task<Dictionary<(string Type, int Id), string>> LabelsAsync(IEnumerable<(string Type, int Id)> keys)
    {
        var list = keys.Distinct().ToList();
        var result = new Dictionary<(string, int), string>();
        if (list.Count == 0) return result;

        List<int> Ids(params string[] types) => list.Where(k => types.Contains(k.Type) && k.Id > 0).Select(k => k.Id).Distinct().ToList();

        var materialIds = Ids(LinkEntityTypes.Material);
        var materials = materialIds.Count == 0 ? [] : await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ProductName, m.MaterialType, m.IsDeleted }).ToListAsync();
        var formulaIds = Ids(LinkEntityTypes.Formula);
        var formulas = formulaIds.Count == 0 ? [] : await db.Formulas.AsNoTracking().Where(f => formulaIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Name, f.IsDeleted }).ToListAsync();
        var stepIds = Ids(LinkEntityTypes.ProcessStep);
        var steps = stepIds.Count == 0 ? [] : await db.ProcessSteps.AsNoTracking().Where(s => stepIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.IsDeleted }).ToListAsync();
        var scheduleIds = Ids(LinkEntityTypes.ProcessSchedule, LinkEntityTypes.Pricing, LinkEntityTypes.MaterialQuantity);
        var schedules = scheduleIds.Count == 0 ? [] : await db.ProcessSchedules.AsNoTracking().Where(s => scheduleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Name, s.Number, s.IsArchived }).ToListAsync();

        foreach (var (type, id) in list)
        {
            string label = type switch
            {
                LinkEntityTypes.Material => materials.FirstOrDefault(m => m.Id == id) is { } m
                    ? $"{MaterialTypes.Label(m.MaterialType)}: {m.ProductName}{(m.IsDeleted ? " (deleted)" : "")}" : $"Material #{id}",
                LinkEntityTypes.Formula => formulas.FirstOrDefault(f => f.Id == id) is { } f
                    ? $"Formula: {f.Name}{(f.IsDeleted ? " (deleted)" : "")}" : $"Formula #{id}",
                LinkEntityTypes.ProcessStep => steps.FirstOrDefault(s => s.Id == id) is { } s
                    ? $"Process Step: {s.Name}{(s.IsDeleted ? " (deleted)" : "")}" : $"Process Step #{id}",
                LinkEntityTypes.ProcessSchedule => schedules.FirstOrDefault(s => s.Id == id) is { } ps
                    ? $"Process Schedule: {ps.Name} (#{ps.Number}){(ps.IsArchived ? " (archived)" : "")}" : $"Process Schedule #{id}",
                LinkEntityTypes.Pricing => id == 0 ? "Pricing (group documents)"
                    : schedules.FirstOrDefault(s => s.Id == id) is { } pp ? $"Pricing: {pp.Name} (#{pp.Number})" : $"Pricing #{id}",
                LinkEntityTypes.MaterialQuantity => id == 0 ? "Material Quantities (group documents)"
                    : schedules.FirstOrDefault(s => s.Id == id) is { } mq ? $"Material Quantities: {mq.Name} (#{mq.Number})" : $"Material Quantities #{id}",
                _ => $"{type} #{id}",
            };
            result[(type, id)] = label;
        }
        return result;
    }

    /// <summary>Validates that every link target exists (not deleted) and belongs to <paramref name="groupId"/>.</summary>
    private async Task EnsureTargetsAsync(int groupId, IReadOnlyCollection<(string Type, int Id)> targets)
    {
        foreach (var (type, id) in targets)
        {
            if (id < 0) throw ApiException.Bad("Invalid link target.");
            if (id == 0 && type is not (LinkEntityTypes.Pricing or LinkEntityTypes.MaterialQuantity))
                throw ApiException.Bad($"Please choose the {Friendly(type)} to link this document to.");
        }

        async Task Check(string type, Func<List<int>, Task<int>> count)
        {
            var ids = targets.Where(t => t.Type == type && t.Id > 0).Select(t => t.Id).Distinct().ToList();
            if (ids.Count == 0) return;
            if (await count(ids) != ids.Count)
                throw ApiException.Bad($"One or more selected {Friendly(type)} items do not exist or do not belong to this document's group.");
        }

        await Check(LinkEntityTypes.Material, ids => db.Materials.CountAsync(m => ids.Contains(m.Id) && m.GroupId == groupId && !m.IsDeleted));
        await Check(LinkEntityTypes.Formula, ids => db.Formulas.CountAsync(f => ids.Contains(f.Id) && f.GroupId == groupId && !f.IsDeleted));
        await Check(LinkEntityTypes.ProcessStep, ids => db.ProcessSteps.CountAsync(s => ids.Contains(s.Id) && s.GroupId == groupId && !s.IsDeleted));
        foreach (var t in new[] { LinkEntityTypes.ProcessSchedule, LinkEntityTypes.Pricing, LinkEntityTypes.MaterialQuantity })
            await Check(t, ids => db.ProcessSchedules.CountAsync(s => ids.Contains(s.Id) && s.GroupId == groupId));
    }

    private static string NormalizeType(string? entityType)
    {
        var t = entityType?.Trim() ?? "";
        return LinkEntityTypes.All.FirstOrDefault(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))
            ?? throw ApiException.Bad($"Unknown link type \"{t}\".");
    }

    private static string Friendly(string type) => type switch
    {
        LinkEntityTypes.ProcessStep => "process step",
        LinkEntityTypes.ProcessSchedule => "process schedule",
        LinkEntityTypes.MaterialQuantity => "material quantities",
        _ => type.ToLowerInvariant(),
    };

    private static IFormFile? ValidateFile(IFormFile? file, bool required)
    {
        if (file == null || file.Length == 0)
            return required ? throw ApiException.Bad("Please choose a file to upload.") : null;
        if (file.Length > MaxFileSize)
            throw ApiException.Bad($"File \"{file.FileName}\" is {FormatSize(file.Length)}; the maximum size is 100 MB.");
        return file;
    }

    private static string? CleanName(string? name)
    {
        var n = Text.Clean(name);
        if (n is { Length: > 200 }) throw ApiException.Bad("Name must be 200 characters or fewer.");
        return n;
    }

    private static string ContentTypeOf(IFormFile file)
    {
        var byExt = FileStorage.ContentTypeFor(file.FileName);
        if (byExt != "application/octet-stream") return byExt;
        return string.IsNullOrWhiteSpace(file.ContentType) ? byExt : Truncate(file.ContentType, 200);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };
}
