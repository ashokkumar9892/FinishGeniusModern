using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using FinishGenius.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// Groups (tenants). Everyone can list the groups they belong to; Group Administrators edit their own groups;
/// System Administrators create, copy and delete groups.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Everyone)]
[Route("api/groups")]
public class GroupsController(AppDbContext db, CurrentUser me, AuditService audit, FileStorage files, GroupCopyService copier) : ControllerBase
{
    public const string DefaultTimeZone = "Eastern Standard Time";

    /// <summary>Multipart form used by create and update.</summary>
    public class GroupForm
    {
        public string? Name { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }
        public string? Country { get; set; }
        public string? TimeZone { get; set; }
        public bool ChecklistDeletionEnabled { get; set; }
        public bool RemoveLogo { get; set; }
        public IFormFile? Logo { get; set; }
    }

    public record CopyRequest(string? NewName, GroupCopyOptions? Options);

    /// <summary>Copy-preview items: key = <see cref="GroupCopyOptions"/> property, label = name used in the copy result.</summary>
    private static readonly (string key, string label)[] CopyItems =
    [
        ("materials", "Equipment & Materials"),
        ("formulas", "Formulas"),
        ("processSteps", "Process Steps"),
        ("processSchedules", "Process Schedules"),
        ("departments", "Departments"),
        ("myWorkSetup", "My Work Setup (Defects & Adders)"),
        ("workInstructions", "Work Instructions"),
        ("photoGallery", "Photo Gallery"),
        ("vendors", "Vendors"),
        ("documents", "Documents"),
    ];

    // ------------------------------------------------------------------ read

    /// <summary>GET /api/groups?search= — groups the caller can access.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search)
    {
        var ids = await me.GroupIdsAsync();
        var q = db.Groups.AsNoTracking().Where(g => ids.Contains(g.Id));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(g => g.Name.Contains(s) || (g.City != null && g.City.Contains(s)) || (g.State != null && g.State.Contains(s)) || (g.Country != null && g.Country.Contains(s)));
        }
        var rows = await q.OrderBy(g => g.Name).ToListAsync();
        var defaultGroupId = await DefaultGroupIdAsync(ids);
        return Ok(rows.Select(g => Dto(g, defaultGroupId)));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var g = await LoadAsync(id);
        return Ok(Dto(g, await DefaultGroupIdAsync(await me.GroupIdsAsync())));
    }

    // ------------------------------------------------------------------ create / update

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Create([FromForm] GroupForm form)
    {
        me.EnsureSystemAdmin();
        var name = Text.Req(form.Name, "Full Name");
        await EnsureUniqueNameAsync(name, null);

        var g = new Group
        {
            Name = name,
            Address1 = Text.Clean(form.Address1), Address2 = Text.Clean(form.Address2), City = Text.Clean(form.City),
            State = Text.Clean(form.State), Zip = Text.Clean(form.Zip), Country = Text.Clean(form.Country),
            TimeZone = ValidTimeZone(form.TimeZone),
            ChecklistDeletionEnabled = form.ChecklistDeletionEnabled,
        };
        if (form.Logo is { Length: > 0 })
            g.LogoFile = await files.SaveAsync(form.Logo, "logos", FileStorage.ImageExtensions);

        db.Groups.Add(g);
        await db.SaveChangesAsync();
        audit.Log("Group", g.Id, "Created", $"Name: {g.Name}" + (g.ChecklistDeletionEnabled ? "; Checklist deletion on submit enabled" : ""), g.Id);
        await db.SaveChangesAsync();
        return Ok(new { message = "Group created.", id = g.Id });
    }

    [HttpPut("{id:int}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Update(int id, [FromForm] GroupForm form)
    {
        var g = await LoadForEditAsync(id);
        var name = Text.Req(form.Name, "Full Name");
        await EnsureUniqueNameAsync(name, id);

        var changes = new List<string>();
        void Set(string label, string? oldValue, string? newValue, Action<string?> apply)
        {
            if ((oldValue ?? "") == (newValue ?? "")) return;
            changes.Add($"{label}: {(string.IsNullOrEmpty(oldValue) ? "(blank)" : oldValue)} → {(string.IsNullOrEmpty(newValue) ? "(blank)" : newValue)}");
            apply(newValue);
        }

        Set("Full Name", g.Name, name, v => g.Name = v!);
        Set("Address1", g.Address1, Text.Clean(form.Address1), v => g.Address1 = v);
        Set("Address2", g.Address2, Text.Clean(form.Address2), v => g.Address2 = v);
        Set("City", g.City, Text.Clean(form.City), v => g.City = v);
        Set("State", g.State, Text.Clean(form.State), v => g.State = v);
        Set("Zip", g.Zip, Text.Clean(form.Zip), v => g.Zip = v);
        Set("Country", g.Country, Text.Clean(form.Country), v => g.Country = v);
        Set("Time Zone", g.TimeZone, ValidTimeZone(form.TimeZone), v => g.TimeZone = v!);
        if (g.ChecklistDeletionEnabled != form.ChecklistDeletionEnabled)
        {
            changes.Add($"Enable Checklist Deletion on Submit: {(g.ChecklistDeletionEnabled ? "Yes" : "No")} → {(form.ChecklistDeletionEnabled ? "Yes" : "No")}");
            g.ChecklistDeletionEnabled = form.ChecklistDeletionEnabled;
        }

        string? oldLogo = null;
        if (form.Logo is { Length: > 0 })
        {
            oldLogo = g.LogoFile;
            g.LogoFile = await files.SaveAsync(form.Logo, "logos", FileStorage.ImageExtensions);
            changes.Add("Logo replaced");
        }
        else if (form.RemoveLogo && g.LogoFile != null)
        {
            oldLogo = g.LogoFile;
            g.LogoFile = null;
            changes.Add("Logo removed");
        }

        if (changes.Count > 0)
        {
            g.UpdatedAt = DateTime.UtcNow;
            audit.Log("Group", g.Id, "Updated", string.Join("\n", changes), g.Id);
            await db.SaveChangesAsync();
            files.Delete(oldLogo);
        }
        return Ok(new { message = "Group updated.", id = g.Id });
    }

    /// <summary>POST /api/groups/{id}/api-key — (re)generates the key devices and integrations use.</summary>
    [HttpPost("{id:int}/api-key")]
    public async Task<IActionResult> GenerateApiKey(int id)
    {
        var g = await LoadForEditAsync(id);
        var hadKey = g.ApiKey != null;
        g.ApiKey = Guid.NewGuid();
        g.UpdatedAt = DateTime.UtcNow;
        audit.Log("Group", g.Id, "API Key generated", hadKey ? "Previous key replaced" : null, g.Id);
        await db.SaveChangesAsync();
        return Ok(new { message = "API Key generated!", apiKey = g.ApiKey });
    }

    // ------------------------------------------------------------------ delete

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        me.EnsureSystemAdmin();
        var g = await LoadAsync(id, tracking: true);
        var myGroupId = await db.Users.Where(u => u.Id == me.Id).Select(u => u.GroupId).FirstAsync();
        if (g.Id == myGroupId)
            throw ApiException.Bad($"You cannot delete \"{g.Name}\" because it is your own primary group. Move your account to another group first.");
        if (!await db.Groups.AnyAsync(x => !x.IsDeleted && x.Id != g.Id))
            throw ApiException.Bad("You cannot delete the last remaining group.");

        g.IsDeleted = true;
        g.UpdatedAt = DateTime.UtcNow;
        audit.Log("Group", g.Id, "Deleted", $"Name: {g.Name}", g.Id);
        await db.SaveChangesAsync();
        return Ok(new { message = "Group deleted." });
    }

    // ------------------------------------------------------------------ copy

    [HttpGet("{id:int}/copy-preview")]
    public async Task<IActionResult> CopyPreview(int id)
    {
        me.EnsureSystemAdmin();
        var g = await LoadAsync(id);
        var counts = await copier.CountsAsync(g.Id);
        int Count(string key) => key switch
        {
            "materials" => counts.GetValueOrDefault("Materials") + counts.GetValueOrDefault("Equipment"),
            "formulas" => counts.GetValueOrDefault("Formulas"),
            "processSteps" => counts.GetValueOrDefault("Process Steps"),
            "processSchedules" => counts.GetValueOrDefault("Process Schedules"),
            "departments" => counts.GetValueOrDefault("Departments"),
            "myWorkSetup" => counts.GetValueOrDefault("My Work Setup (Defects & Adders)"),
            "workInstructions" => counts.GetValueOrDefault("Work Instructions"),
            "photoGallery" => counts.GetValueOrDefault("Photo Gallery"),
            "vendors" => counts.GetValueOrDefault("Vendors"),
            "documents" => counts.GetValueOrDefault("Documents"),
            _ => 0,
        };
        return Ok(new
        {
            g.Id, g.Name,
            SuggestedName = await SuggestCopyNameAsync(g.Name),
            Items = CopyItems.Select(i => new { Key = i.key, Label = i.label, Count = Count(i.key) }),
        });
    }

    [HttpPost("{id:int}/copy")]
    public async Task<IActionResult> Copy(int id, CopyRequest req)
    {
        me.EnsureSystemAdmin();
        var src = await LoadAsync(id);
        var name = Text.Req(req.NewName, "New Group Name");
        await EnsureUniqueNameAsync(name, null);
        var options = req.Options ?? new GroupCopyOptions();

        var (dest, copied) = await copier.CopyGroupAsync(src.Id, name, options);
        var summary = copied.Count == 0 ? "No items selected." : string.Join("\n", copied.Select(c => $"{c.Key}: {c.Value}"));
        audit.Log("Group", dest.Id, "Created (copy)", $"Copied from \"{src.Name}\"\n{summary}", dest.Id);
        audit.Log("Group", src.Id, "Copied", $"Copied to \"{dest.Name}\"\n{summary}", src.Id);
        await db.SaveChangesAsync();

        return Ok(new
        {
            message = $"Group \"{dest.Name}\" created." + (copied.Count > 0 ? "\n" + summary : ""),
            id = dest.Id,
            copied = copied.Select(c => new { item = c.Key, count = c.Value }),
        });
    }

    // ------------------------------------------------------------------ helpers

    private async Task<Group> LoadAsync(int id, bool tracking = false)
    {
        var q = tracking ? db.Groups : db.Groups.AsNoTracking();
        var g = await q.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted) ?? throw ApiException.NotFound("Group");
        await me.EnsureGroupAsync(g.Id);
        return g;
    }

    /// <summary>System Admins edit any group; Group Admins edit groups they belong to.</summary>
    private async Task<Group> LoadForEditAsync(int id)
    {
        if (!me.IsAdmin)
            throw new ApiException(StatusCodes.Status403Forbidden, "Only Group Administrators and System Administrators can edit groups.");
        return await LoadAsync(id, tracking: true);
    }

    private async Task EnsureUniqueNameAsync(string name, int? exceptId)
    {
        if (name.Length > 200) throw ApiException.Bad("Full Name must be 200 characters or fewer.");
        if (await db.Groups.AnyAsync(g => !g.IsDeleted && g.Name == name && g.Id != exceptId))
            throw ApiException.Bad("Duplicate Group Name");
    }

    private async Task<string> SuggestCopyNameAsync(string name)
    {
        var baseName = $"{name} - Copy";
        var candidate = baseName;
        for (var i = 2; await db.Groups.AnyAsync(g => !g.IsDeleted && g.Name == candidate); i++)
            candidate = $"{baseName} {i}";
        return candidate;
    }

    private static string ValidTimeZone(string? tz)
    {
        var id = Text.Clean(tz) ?? DefaultTimeZone;
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return id;
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw ApiException.Bad("Select a valid Time Zone.");
        }
    }

    private async Task<int> DefaultGroupIdAsync(List<int> accessible)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == me.Id).Select(x => new { x.GroupId, x.DefaultGroupId }).FirstAsync();
        return u.DefaultGroupId is int d && accessible.Contains(d) ? d : u.GroupId;
    }

    private static string TimeZoneLabel(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id).DisplayName; }
        catch (Exception) { return id; }
    }

    private object Dto(Group g, int defaultGroupId) => new
    {
        g.Id, g.Name, g.Address1, g.Address2, g.City, g.State, g.Zip, g.Country,
        g.TimeZone, TimeZoneLabel = TimeZoneLabel(g.TimeZone),
        // The API key is a credential: only administrators see it.
        ApiKey = me.IsAdmin ? g.ApiKey : null,
        g.LogoFile, g.ChecklistDeletionEnabled, g.CreatedAt, g.UpdatedAt,
        IsDefault = g.Id == defaultGroupId,
        CanEdit = me.IsAdmin,
        CanDelete = me.IsSystemAdmin,
        CanCopy = me.IsSystemAdmin,
    };
}
