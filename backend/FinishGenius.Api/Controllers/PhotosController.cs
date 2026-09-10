using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>Photo Gallery: group-scoped images with free-form tags.</summary>
[ApiController]
[Authorize(Roles = Access.Everyone)]
[Route("api/photos")]
public class PhotosController(AppDbContext db, CurrentUser me, AuditService audit, FileStorage files) : ControllerBase
{
    private const string Entity = "Photo";
    private const int MaxTagLength = 100;

    public record UpdateRequest(string? Name, List<string>? Tags);

    /// <summary>GET /api/photos?groupId=1&amp;search=door&amp;tags=a,b — tag filter keeps photos having ALL the tags.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int groupId, [FromQuery] string? search, [FromQuery] string? tags)
    {
        await me.EnsureGroupAsync(groupId);
        var q = db.Photos.AsNoTracking().Where(p => p.GroupId == groupId);
        foreach (var tag in NormalizeTags(tags?.Split(',')))
            q = q.Where(p => p.Tags.Any(t => t.Tag == tag));
        var s = Text.Clean(search);
        if (s != null) q = q.Where(p => p.Name.Contains(s) || p.Tags.Any(t => t.Tag.Contains(s)));

        var rows = await q.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Select(p => new
        {
            p.Id, p.GroupId, p.Name, p.StoredFile, p.ContentType, p.CreatedAt, p.CreatedBy,
            Tags = p.Tags.OrderBy(t => t.Tag).Select(t => t.Tag).ToList(),
        }).ToListAsync();

        var userIds = rows.Where(r => r.CreatedBy != null).Select(r => r.CreatedBy!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync();
        var names = users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } full ? full : u.Username);

        return Ok(rows.Select(r => new
        {
            r.Id, r.GroupId, r.Name, r.StoredFile, r.ContentType, r.Tags, r.CreatedAt,
            CreatedByName = r.CreatedBy != null && names.TryGetValue(r.CreatedBy.Value, out var n) ? n : null,
        }));
    }

    /// <summary>Distinct tags used in the group with the number of photos carrying each.</summary>
    [HttpGet("tags")]
    public async Task<IActionResult> Tags([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var rows = await db.PhotoTags.AsNoTracking()
            .Where(t => db.Photos.Any(p => p.Id == t.PhotoId && p.GroupId == groupId))
            .GroupBy(t => t.Tag).Select(g => new { Tag = g.Key, Count = g.Count() })
            .OrderBy(x => x.Tag).ToListAsync();
        return Ok(rows);
    }

    /// <summary>Multipart: groupId, files (one or more images), name (used when a single file is uploaded), tags (comma separated).</summary>
    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] int groupId, [FromForm(Name = "files")] List<IFormFile>? uploads,
        [FromForm] string? name, [FromForm] string? tags)
    {
        await me.EnsureGroupAsync(groupId);
        if (uploads == null || uploads.Count == 0) throw ApiException.Bad("Please select at least one photo to upload.");

        var folder = $"photos/{groupId}";
        // Validate every file before writing any: SaveAsync throws the standard "Invalid extension…" message without saving.
        var invalid = uploads.FirstOrDefault(f => !FileStorage.ImageExtensions.Contains(Path.GetExtension(f.FileName).ToLowerInvariant()));
        if (invalid != null) await files.SaveAsync(invalid, folder, FileStorage.ImageExtensions);
        var empty = uploads.FirstOrDefault(f => f.Length == 0);
        if (empty != null) throw ApiException.Bad($"File \"{empty.FileName}\" is empty.");

        var tagList = NormalizeTags(tags?.Split(','));
        var customName = uploads.Count == 1 ? Text.Clean(name) : null;
        if (customName is { Length: > 400 }) throw ApiException.Bad("Name must be 400 characters or fewer.");

        var photos = new List<Photo>();
        try
        {
            foreach (var f in uploads)
            {
                var path = await files.SaveAsync(f, folder, FileStorage.ImageExtensions);
                var fileName = Path.GetFileNameWithoutExtension(f.FileName);
                photos.Add(new Photo
                {
                    GroupId = groupId,
                    Name = customName ?? (fileName.Length > 400 ? fileName[..400] : fileName),
                    StoredFile = path,
                    ContentType = FileStorage.ContentTypeFor(path),
                    CreatedBy = me.Id == 0 ? null : me.Id,
                    Tags = tagList.Select(t => new PhotoTag { Tag = t }).ToList(),
                });
            }
            db.Photos.AddRange(photos);
            await db.SaveChangesAsync();
        }
        catch
        {
            foreach (var p in photos) files.Delete(p.StoredFile);
            throw;
        }

        foreach (var p in photos)
            audit.Log(Entity, p.Id, "Created", $"{p.Name}{(tagList.Count > 0 ? " — tags: " + string.Join(", ", tagList) : "")}", groupId);
        await db.SaveChangesAsync();
        return Ok(new
        {
            message = photos.Count == 1 ? "Photo uploaded." : $"{photos.Count} photos uploaded.",
            ids = photos.Select(p => p.Id),
        });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateRequest r)
    {
        var photo = await LoadAsync(id);
        var name = Text.Req(r.Name, "Name");
        if (name.Length > 400) throw ApiException.Bad("Name must be 400 characters or fewer.");
        var tags = NormalizeTags(r.Tags);

        var changes = new List<string>();
        if (name != photo.Name)
        {
            changes.Add($"Name \"{photo.Name}\" → \"{name}\"");
            photo.Name = name;
        }
        var removed = photo.Tags.Where(t => !tags.Contains(t.Tag, StringComparer.OrdinalIgnoreCase)).ToList();
        var added = tags.Where(t => !photo.Tags.Any(x => string.Equals(x.Tag, t, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var t in removed) photo.Tags.Remove(t);
        foreach (var t in added) photo.Tags.Add(new PhotoTag { Tag = t });
        if (added.Count > 0) changes.Add("Tags added: " + string.Join(", ", added));
        if (removed.Count > 0) changes.Add("Tags removed: " + string.Join(", ", removed.Select(t => t.Tag)));

        if (changes.Count == 0) return Ok(new { message = "No changes to save." });
        audit.Log(Entity, photo.Id, "Updated", string.Join("; ", changes), photo.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Photo saved." });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var photo = await LoadAsync(id);
        db.Photos.Remove(photo);
        audit.Log(Entity, photo.Id, "Deleted", photo.Name, photo.GroupId);
        await db.SaveChangesAsync();
        files.Delete(photo.StoredFile);
        return Ok(new { message = "Photo deleted." });
    }

    private async Task<Photo> LoadAsync(int id)
    {
        var photo = await db.Photos.Include(p => p.Tags).FirstOrDefaultAsync(p => p.Id == id) ?? throw ApiException.NotFound("Photo");
        await me.EnsureGroupAsync(photo.GroupId);
        return photo;
    }

    /// <summary>Trims, collapses inner whitespace, drops blanks and case-insensitive duplicates.</summary>
    private static List<string> NormalizeTags(IEnumerable<string?>? raw)
    {
        var result = new List<string>();
        foreach (var t in raw ?? [])
        {
            var tag = string.Join(' ', (t ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            if (tag.Length == 0) continue;
            if (tag.Length > MaxTagLength) throw ApiException.Bad($"Tag \"{tag[..20]}…\" must be {MaxTagLength} characters or fewer.");
            if (!result.Contains(tag, StringComparer.OrdinalIgnoreCase)) result.Add(tag);
        }
        return result;
    }
}
