using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Standard Work Instructions: header (page 1), tools/materials (page 2) and illustrated steps (page 3).
/// Every change appends an Edit Trail row; changing a RELEASED document starts DRAFT v(N+1).
/// </summary>
[ApiController]
[Authorize(Roles = Access.WorkInstructions)]
[Route("api/work-instructions")]
public class WorkInstructionsController(AppDbContext db, CurrentUser me, AuditService audit, FileStorage files, GroupCopyService copier) : ControllerBase
{
    private const string Entity = "WorkInstruction";
    /// <summary>Legacy "Delete Instruction" was broken; admins and FG Pro users may delete.</summary>
    private const string DeleteRoles = $"{Roles.SystemAdmin},{Roles.GroupAdmin},{Roles.FGPro}";
    private static readonly string[] ItemKinds = ["Equipment", "Material"];

    public record CreateRequest(int GroupId, string? DocumentNumber, string? Name);
    public record HeaderRequest(string? DocumentNumber, string? Name, DateTime? IssueDate, string? Location, bool Controlled,
        string? Purpose, string? Scope, string? Terminology);
    public record StepRequest(int? Level, string? Title, string? Body);
    public record RelatedDocRequest(string? DocumentNumber, string? DocumentName, string? Author);
    public record SignatureRequest(string? Name, string? Position, DateTime? Date);
    public record ItemRequest(string? Kind, string? Description, int? MaterialId);
    public record CopyRequest(string? NewName, string? NewNumber, int? DestinationGroupId);

    // ------------------------------------------------------------------ list / create

    /// <summary>GET /api/work-instructions?groupId=1&amp;search=paint</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] string? search)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.WorkInstructions.AsNoTracking().Where(w => w.GroupId == groupId && !w.IsDeleted);
        var s = Text.Clean(search);
        if (s != null) q = q.Where(w => w.Name.Contains(s) || w.DocumentNumber.Contains(s));
        var rows = await q.Select(w => new
        {
            w.Id, w.GroupId, GroupName = w.Group!.Name, w.DocumentNumber, w.Name, w.IssueDate, w.CreatedAt, w.UpdatedAt,
            w.Version, w.IsReleased, StepCount = w.Steps.Count,
        }).ToListAsync();
        return Ok(rows.OrderBy(r => r.DocumentNumber).Select(r => new
        {
            r.Id, r.GroupId, r.GroupName, r.DocumentNumber, r.Name, r.IssueDate, r.CreatedAt, r.UpdatedAt,
            Status = StatusText(r.IsReleased, r.Version), r.IsReleased, r.Version, r.StepCount,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateRequest r)
    {
        await me.EnsureGroupAsync(r.GroupId);
        var w = new WorkInstruction
        {
            GroupId = r.GroupId,
            DocumentNumber = Max(Text.Req(r.DocumentNumber, "Document #"), 400, "Document #"),
            Name = Max(Text.Req(r.Name, "Document Name"), 400, "Document Name"),
            IssueDate = DateTime.UtcNow,
            Version = 1,
        };
        w.Trail.Add(new WorkInstructionTrail { Version = w.StatusText, Author = Author, Log = "Created" });
        db.WorkInstructions.Add(w);
        await db.SaveChangesAsync();
        audit.Log(Entity, w.Id, "Created", $"#{w.DocumentNumber} {w.Name}", w.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Work instruction created.", id = w.Id });
    }

    // ------------------------------------------------------------------ document

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var w = await db.WorkInstructions.AsNoTracking().AsSplitQuery()
            .Include(x => x.Group)
            .Include(x => x.Steps).ThenInclude(s => s.Media)
            .Include(x => x.Trail).Include(x => x.RelatedDocuments).Include(x => x.Signatures).Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Work instruction");
        await me.EnsureGroupAsync(w.GroupId);

        var materialIds = w.Items.Where(i => i.MaterialId != null).Select(i => i.MaterialId!.Value).Distinct().ToList();
        Dictionary<int, string> materialNames = materialIds.Count == 0
            ? []
            : await db.Materials.AsNoTracking().Where(m => materialIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.ProductName);

        return Ok(new
        {
            w.Id, w.GroupId, GroupName = w.Group?.Name, w.DocumentNumber, w.Name, w.IssueDate, w.Version, w.IsReleased,
            Status = w.StatusText, w.Controlled, w.Location, w.Purpose, w.Scope, w.Terminology, w.CreatedAt, w.UpdatedAt,
            Trail = w.Trail.OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
                .Select(t => new { t.Id, t.Version, t.Author, t.Log, t.Date }),
            RelatedDocuments = w.RelatedDocuments.OrderBy(d => d.Id)
                .Select(d => new { d.Id, d.DocumentNumber, d.DocumentName, d.Author }),
            Signatures = w.Signatures.OrderBy(s => s.Id).Select(s => new { s.Id, s.Name, s.Position, s.Date }),
            Items = w.Items.OrderBy(i => i.Id).Select(i => new
            {
                i.Id, i.Kind, i.Description, i.MaterialId,
                MaterialName = i.MaterialId != null && materialNames.TryGetValue(i.MaterialId.Value, out var n) ? n : null,
            }),
            Steps = w.Steps.OrderBy(s => s.Level).ThenBy(s => s.Id).Select(s => new
            {
                s.Id, s.Level, s.Title, s.Body,
                Media = s.Media.OrderBy(m => m.Id).Select(MediaDto),
            }),
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateHeader(int id, HeaderRequest r)
    {
        var w = await LoadAsync(id);
        var number = Max(Text.Req(r.DocumentNumber, "Document #"), 400, "Document #");
        var name = Max(Text.Req(r.Name, "Document Name"), 400, "Document Name");
        var location = Max(Text.Clean(r.Location), 4000, "Location");
        var changes = new List<string>();

        if (number != w.DocumentNumber) { changes.Add($"Document # \"{w.DocumentNumber}\" → \"{number}\""); w.DocumentNumber = number; }
        if (name != w.Name) { changes.Add($"Name \"{w.Name}\" → \"{name}\""); w.Name = name; }
        if (r.IssueDate != null && r.IssueDate.Value.Date != w.IssueDate.Date)
        {
            changes.Add($"Issue Date {w.IssueDate:MM/dd/yyyy} → {r.IssueDate.Value:MM/dd/yyyy}");
            w.IssueDate = NoonUtc(r.IssueDate.Value);
        }
        if (location != w.Location) { changes.Add($"Location \"{w.Location}\" → \"{location}\""); w.Location = location; }
        if (r.Controlled != w.Controlled)
        {
            changes.Add(r.Controlled ? "Uncontrolled Copy → Controlled Copy" : "Controlled Copy → Uncontrolled Copy");
            w.Controlled = r.Controlled;
        }
        var purpose = Max(Text.Clean(r.Purpose), 4000, "Purpose");
        if (purpose != w.Purpose) { changes.Add("Purpose updated"); w.Purpose = purpose; }
        var scope = Max(Text.Clean(r.Scope), 4000, "Scope");
        if (scope != w.Scope) { changes.Add("Scope updated"); w.Scope = scope; }
        var terminology = Max(Text.Clean(r.Terminology), 4000, "Terminology");
        if (terminology != w.Terminology) { changes.Add("Terminology updated"); w.Terminology = terminology; }

        if (changes.Count == 0) return Ok(new { message = "No changes to save." });
        Change(w, string.Join("; ", changes));
        await db.SaveChangesAsync();
        return Ok(new { message = "Work instruction saved.", status = w.StatusText });
    }

    [HttpPost("{id:int}/release")]
    public async Task<IActionResult> Release(int id)
    {
        var w = await LoadAsync(id);
        if (w.IsReleased) throw ApiException.Bad("This work instruction is already released.");
        w.IsReleased = true;
        w.UpdatedAt = DateTime.UtcNow;
        AddTrail(w, "Released");
        audit.Log(Entity, w.Id, "Released", w.StatusText, w.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Work instruction released.", status = w.StatusText });
    }

    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id, CopyRequest r)
    {
        var w = await LoadAsync(id);
        var dest = r.DestinationGroupId is > 0 ? r.DestinationGroupId.Value : w.GroupId;
        await me.EnsureGroupAsync(dest);
        var name = Max(Text.Req(r.NewName, "New Name"), 400, "New Name");
        var number = Max(Text.Clean(r.NewNumber) ?? w.DocumentNumber, 400, "New Number");

        await copier.CopyWorkInstructionsAsync([id], dest, name, number);
        var copy = db.ChangeTracker.Entries<WorkInstruction>().Select(e => e.Entity)
            .Where(x => x.GroupId == dest && x.Id != id && x.Name == name).OrderByDescending(x => x.Id).First();

        // The copy service drops material links; inside the same group they are still valid, so restore them.
        if (dest == w.GroupId)
        {
            var linked = await db.WorkInstructionItems.AsNoTracking().Where(i => i.WorkInstructionId == id && i.MaterialId != null).ToListAsync();
            foreach (var item in copy.Items.Where(i => i.MaterialId == null))
                item.MaterialId = linked.FirstOrDefault(l => l.Kind == item.Kind && l.Description == item.Description)?.MaterialId;
        }
        audit.Log(Entity, copy.Id, "Created", $"Copied from #{w.DocumentNumber} {w.Name}", dest);
        audit.Log(Entity, w.Id, "Copied", $"Copied to #{number} {name}" + (dest != w.GroupId ? $" (group {dest})" : ""), w.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Work instruction copied.", id = copy.Id });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = DeleteRoles)]
    public async Task<IActionResult> Delete(int id)
    {
        var w = await LoadAsync(id);
        if (db is LegacyAppDbContext)
            db.WorkInstructions.Remove(w); // no deleted flag on the old table: removed with its contents, like the old site's delete
        else
        {
            w.IsDeleted = true;
            w.UpdatedAt = DateTime.UtcNow;
        }
        audit.Log(Entity, w.Id, "Deleted", $"#{w.DocumentNumber} {w.Name}", w.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Work instruction deleted." });
    }

    // ------------------------------------------------------------------ steps

    [HttpPost("{id:int}/steps")]
    public async Task<IActionResult> AddStep(int id, StepRequest r)
    {
        var w = await LoadAsync(id);
        var title = Max(Text.Req(r.Title, "Description / Title"), 4000, "Description / Title");
        var level = r.Level is > 0 ? r.Level.Value : await NextLevelAsync(id);
        var step = new WorkInstructionStep { WorkInstructionId = id, Level = level, Title = title, Body = Text.Clean(r.Body) };
        db.WorkInstructionSteps.Add(step);
        Change(w, $"Added step {level}: {Short(title)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Step added.", id = step.Id, level });
    }

    [HttpPut("steps/{stepId:int}")]
    public async Task<IActionResult> UpdateStep(int stepId, StepRequest r)
    {
        var (step, w) = await LoadStepAsync(stepId);
        var title = Max(Text.Req(r.Title, "Description / Title"), 4000, "Description / Title");
        var level = r.Level is > 0 ? r.Level.Value : step.Level;
        var body = Text.Clean(r.Body);
        var changes = new List<string>();
        if (level != step.Level) changes.Add($"level {step.Level} → {level}");
        if (title != step.Title) changes.Add("title");
        if (body != step.Body) changes.Add("details");
        if (changes.Count == 0) return Ok(new { message = "No changes to save.", id = step.Id });

        step.Level = level;
        step.Title = title;
        step.Body = body;
        Change(w, $"Updated step {level} ({string.Join(", ", changes)}): {Short(title)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Step saved.", id = step.Id });
    }

    [HttpDelete("steps/{stepId:int}")]
    public async Task<IActionResult> DeleteStep(int stepId)
    {
        var (step, w) = await LoadStepAsync(stepId);
        var stored = step.Media.Select(m => m.StoredFile).ToList();
        db.WorkInstructionSteps.Remove(step);
        Change(w, $"Deleted step {step.Level}: {Short(step.Title)}");
        await db.SaveChangesAsync();
        foreach (var path in stored) files.Delete(path);
        return Ok(new { message = "Step deleted." });
    }

    [HttpPost("steps/{stepId:int}/copy")]
    public async Task<IActionResult> CopyStep(int stepId)
    {
        var (step, w) = await LoadStepAsync(stepId);
        var level = await NextLevelAsync(w.Id);
        var copy = new WorkInstructionStep { WorkInstructionId = w.Id, Level = level, Title = step.Title, Body = step.Body };
        foreach (var m in step.Media.OrderBy(m => m.Id))
            copy.Media.Add(new WorkInstructionMedia
            {
                FileName = m.FileName, ContentType = m.ContentType,
                StoredFile = await files.CopyAsync(m.StoredFile, MediaFolder(w.GroupId)),
            });
        db.WorkInstructionSteps.Add(copy);
        Change(w, $"Copied step {step.Level} as step {level}: {Short(step.Title)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Step copied.", id = copy.Id, level });
    }

    /// <summary>Multipart upload (field "files", one or more): jpg, jpeg, png, gif, mp4, mov — extensions are case-insensitive.</summary>
    [HttpPost("steps/{stepId:int}/media")]
    public async Task<IActionResult> UploadMedia(int stepId, [FromForm(Name = "files")] List<IFormFile>? uploads)
    {
        var (step, w) = await LoadStepAsync(stepId);
        if (uploads == null || uploads.Count == 0) throw ApiException.Bad("Please select a file to upload.");
        if (db is LegacyAppDbContext && step.Media.Count + uploads.Count > 3)
            throw ApiException.Bad($"A step can hold 3 files on this database ({step.Media.Count} already attached).");

        var folder = MediaFolder(w.GroupId);
        // Reject the whole batch before anything is written. SaveAsync throws the exact legacy message for a bad extension
        // ("Invalid extension for file "x.docx". Only "jpg, png, gif, mp4, mov" files are supported.") without saving.
        var invalid = uploads.FirstOrDefault(f => !FileStorage.MediaExtensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant()));
        if (invalid != null) await files.SaveAsync(invalid, folder, FileStorage.MediaExtensions);
        var empty = uploads.FirstOrDefault(f => f.Length == 0);
        if (empty != null) throw ApiException.Bad($"File \"{empty.FileName}\" is empty.");

        var added = new List<WorkInstructionMedia>();
        try
        {
            foreach (var f in uploads)
            {
                var path = await files.SaveAsync(f, folder, FileStorage.MediaExtensions);
                added.Add(new WorkInstructionMedia
                {
                    StepId = step.Id, StoredFile = path, ContentType = FileStorage.ContentTypeFor(path),
                    FileName = Trunc(Path.GetFileName(f.FileName), 400),
                });
            }
            db.WorkInstructionMedia.AddRange(added);
            Change(w, $"Added {Plural(added.Count, "media file")} to step {step.Level}: {string.Join(", ", added.Select(a => a.FileName))}");
            await db.SaveChangesAsync();
        }
        catch
        {
            foreach (var m in added) files.Delete(m.StoredFile);
            throw;
        }
        return Ok(new
        {
            message = added.Count == 1 ? "File uploaded." : $"{added.Count} files uploaded.",
            media = added.Select(MediaDto),
        });
    }

    [HttpDelete("media/{mediaId:int}")]
    public async Task<IActionResult> DeleteMedia(int mediaId)
    {
        var media = await db.WorkInstructionMedia.FirstOrDefaultAsync(m => m.Id == mediaId) ?? throw ApiException.NotFound("Media file");
        var (step, w) = await LoadStepAsync(media.StepId);
        db.WorkInstructionMedia.Remove(media);
        Change(w, $"Removed media \"{media.FileName}\" from step {step.Level}");
        await db.SaveChangesAsync();
        files.Delete(media.StoredFile);
        return Ok(new { message = "File deleted." });
    }

    /// <summary>Renumbers step levels 1..N in their current order.</summary>
    [HttpPost("{id:int}/steps/renumber")]
    public async Task<IActionResult> Renumber(int id)
    {
        var w = await LoadAsync(id);
        var steps = await db.WorkInstructionSteps.Where(s => s.WorkInstructionId == id).OrderBy(s => s.Level).ThenBy(s => s.Id).ToListAsync();
        // Old-database sub steps (1.1, 1.2 …) share their parent's level there: number the levels, not the rows.
        var levels = steps.Select(s => s.Level).Distinct().ToList();
        var changed = false;
        for (var i = 0; i < steps.Count; i++)
        {
            var level = db is LegacyAppDbContext ? levels.IndexOf(steps[i].Level) + 1 : i + 1;
            if (steps[i].Level == level) continue;
            steps[i].Level = level;
            changed = true;
        }
        if (!changed) return Ok(new { message = "Steps are already numbered in order." });
        Change(w, "Renumbered steps");
        await db.SaveChangesAsync();
        return Ok(new { message = "Steps renumbered." });
    }

    // ------------------------------------------------------------------ related documents

    [HttpPost("{id:int}/related-documents")]
    public async Task<IActionResult> AddRelatedDoc(int id, RelatedDocRequest r)
    {
        var w = await LoadAsync(id);
        var doc = new WorkInstructionRelatedDoc { WorkInstructionId = id };
        ApplyRelatedDoc(doc, r);
        db.WorkInstructionRelatedDocs.Add(doc);
        Change(w, $"Added related document {RelatedLabel(doc)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Related document added.", id = doc.Id });
    }

    [HttpPut("{id:int}/related-documents/{docId:int}")]
    public async Task<IActionResult> UpdateRelatedDoc(int id, int docId, RelatedDocRequest r)
    {
        var w = await LoadAsync(id);
        var doc = await db.WorkInstructionRelatedDocs.FirstOrDefaultAsync(d => d.Id == docId && d.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Related document");
        ApplyRelatedDoc(doc, r);
        Change(w, $"Updated related document {RelatedLabel(doc)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Related document saved." });
    }

    [HttpDelete("{id:int}/related-documents/{docId:int}")]
    public async Task<IActionResult> DeleteRelatedDoc(int id, int docId)
    {
        var w = await LoadAsync(id);
        var doc = await db.WorkInstructionRelatedDocs.FirstOrDefaultAsync(d => d.Id == docId && d.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Related document");
        db.WorkInstructionRelatedDocs.Remove(doc);
        Change(w, $"Removed related document {RelatedLabel(doc)}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Related document deleted." });
    }

    // ------------------------------------------------------------------ approval signatures

    [HttpPost("{id:int}/signatures")]
    public async Task<IActionResult> AddSignature(int id, SignatureRequest r)
    {
        var w = await LoadAsync(id);
        var sig = new WorkInstructionSignature { WorkInstructionId = id };
        ApplySignature(sig, r);
        db.WorkInstructionSignatures.Add(sig);
        Change(w, $"Added approval signature {sig.Name}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Signature added.", id = sig.Id });
    }

    [HttpPut("{id:int}/signatures/{sigId:int}")]
    public async Task<IActionResult> UpdateSignature(int id, int sigId, SignatureRequest r)
    {
        var w = await LoadAsync(id);
        var sig = await db.WorkInstructionSignatures.FirstOrDefaultAsync(s => s.Id == sigId && s.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Signature");
        ApplySignature(sig, r);
        Change(w, $"Updated approval signature {sig.Name}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Signature saved." });
    }

    [HttpDelete("{id:int}/signatures/{sigId:int}")]
    public async Task<IActionResult> DeleteSignature(int id, int sigId)
    {
        var w = await LoadAsync(id);
        var sig = await db.WorkInstructionSignatures.FirstOrDefaultAsync(s => s.Id == sigId && s.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Signature");
        db.WorkInstructionSignatures.Remove(sig);
        Change(w, $"Removed approval signature {sig.Name}");
        await db.SaveChangesAsync();
        return Ok(new { message = "Signature deleted." });
    }

    // ------------------------------------------------------------------ tools & equipment / materials

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, ItemRequest r)
    {
        var w = await LoadAsync(id);
        var item = new WorkInstructionItem { WorkInstructionId = id };
        await ApplyItemAsync(item, r, w.GroupId);
        db.WorkInstructionItems.Add(item);
        Change(w, $"Added {ItemLabel(item.Kind)} \"{item.Description}\"");
        await db.SaveChangesAsync();
        return Ok(new { message = item.Kind == "Equipment" ? "Equipment added." : "Material added.", id = item.Id });
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int id, int itemId, ItemRequest r)
    {
        var w = await LoadAsync(id);
        var item = await db.WorkInstructionItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Item");
        await ApplyItemAsync(item, r, w.GroupId);
        Change(w, $"Updated {ItemLabel(item.Kind)} \"{item.Description}\"");
        await db.SaveChangesAsync();
        return Ok(new { message = item.Kind == "Equipment" ? "Equipment saved." : "Material saved." });
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var w = await LoadAsync(id);
        var item = await db.WorkInstructionItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WorkInstructionId == id)
            ?? throw ApiException.NotFound("Item");
        db.WorkInstructionItems.Remove(item);
        Change(w, $"Removed {ItemLabel(item.Kind)} \"{item.Description}\"");
        await db.SaveChangesAsync();
        return Ok(new { message = item.Kind == "Equipment" ? "Equipment removed." : "Material removed." });
    }

    // ------------------------------------------------------------------ helpers

    private string Author => Text.Clean(User.FindFirstValue(ClaimTypes.Email)) ?? me.UserName;

    private async Task<WorkInstruction> LoadAsync(int id)
    {
        var w = await db.WorkInstructions.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Work instruction");
        await me.EnsureGroupAsync(w.GroupId);
        return w;
    }

    private async Task<(WorkInstructionStep step, WorkInstruction w)> LoadStepAsync(int stepId)
    {
        var step = await db.WorkInstructionSteps.Include(s => s.Media).FirstOrDefaultAsync(s => s.Id == stepId) ?? throw ApiException.NotFound("Step");
        return (step, await LoadAsync(step.WorkInstructionId));
    }

    private async Task<int> NextLevelAsync(int workInstructionId) =>
        (await db.WorkInstructionSteps.Where(s => s.WorkInstructionId == workInstructionId).MaxAsync(s => (int?)s.Level) ?? 0) + 1;

    /// <summary>Records a change: a RELEASED document first becomes DRAFT v(N+1); then an Edit Trail row and an audit row are added.</summary>
    private void Change(WorkInstruction w, string log)
    {
        if (w.IsReleased)
        {
            w.IsReleased = false;
            w.Version++;
            log = $"New draft from RELEASED v{w.Version - 1}. {log}";
        }
        w.UpdatedAt = DateTime.UtcNow;
        AddTrail(w, log);
        audit.Log(Entity, w.Id, "Updated", log, w.GroupId);
    }

    private void AddTrail(WorkInstruction w, string log) => db.WorkInstructionTrails.Add(new WorkInstructionTrail
    {
        WorkInstructionId = w.Id, Version = w.StatusText, Author = Trunc(Author, 400), Log = Trunc(log, 4000),
    });

    private static void ApplyRelatedDoc(WorkInstructionRelatedDoc doc, RelatedDocRequest r)
    {
        doc.DocumentName = Max(Text.Req(r.DocumentName, "Document Name"), 400, "Document Name");
        doc.DocumentNumber = Max(Text.Clean(r.DocumentNumber) ?? "", 400, "Document Number");
        doc.Author = Max(Text.Clean(r.Author), 400, "Author");
    }

    private static void ApplySignature(WorkInstructionSignature sig, SignatureRequest r)
    {
        sig.Name = Max(Text.Req(r.Name, "Name"), 400, "Name");
        sig.Position = Max(Text.Clean(r.Position), 400, "Position");
        sig.Date = r.Date == null ? DateTime.UtcNow : NoonUtc(r.Date.Value);
    }

    private async Task ApplyItemAsync(WorkInstructionItem item, ItemRequest r, int groupId)
    {
        var kind = ItemKinds.FirstOrDefault(k => string.Equals(k, Text.Clean(r.Kind), StringComparison.OrdinalIgnoreCase))
            ?? throw ApiException.Bad("Kind must be Equipment or Material.");
        Material? material = null;
        if (r.MaterialId is > 0)
            material = await db.Materials.AsNoTracking().FirstOrDefaultAsync(m => m.Id == r.MaterialId && m.GroupId == groupId && !m.IsDeleted)
                ?? throw ApiException.Bad("The selected item was not found in this group.");
        item.Kind = kind;
        item.MaterialId = material?.Id;
        item.Description = Max(Text.Clean(r.Description) ?? material?.ProductName ?? Text.Req(null, "Description"), 400, "Description");
    }

    private static object MediaDto(WorkInstructionMedia m) => new
    {
        m.Id, m.FileName, m.StoredFile, Url = m.StoredFile, m.ContentType,
        IsVideo = m.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                  || Path.GetExtension(m.StoredFile).ToLowerInvariant() is ".mp4" or ".mov",
    };

    private static string StatusText(bool released, int version) => $"{(released ? "RELEASED" : "DRAFT")} v{version}";
    private static string MediaFolder(int groupId) => $"work-instructions/{groupId}";
    private static string ItemLabel(string kind) => kind == "Equipment" ? "tool/equipment" : "material";
    private static string RelatedLabel(WorkInstructionRelatedDoc d) => $"{(d.DocumentNumber.Length > 0 ? "#" + d.DocumentNumber + " " : "")}{d.DocumentName}";
    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
    private static string Short(string s) => s.Length > 80 ? s[..80] + "…" : s;
    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
    /// <summary>Date-only values are stored at 12:00 UTC so they display as the same calendar day in every US time zone.</summary>
    private static DateTime NoonUtc(DateTime d) => DateTime.SpecifyKind(d.Date.AddHours(12), DateTimeKind.Utc);

    [return: NotNullIfNotNull(nameof(s))]
    private static string? Max(string? s, int max, string field) =>
        s != null && s.Length > max ? throw ApiException.Bad($"{field} must be {max} characters or fewer.") : s;
}
