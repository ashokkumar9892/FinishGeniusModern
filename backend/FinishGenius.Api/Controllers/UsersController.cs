using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// User administration (Group Administrators: users of their own groups; System Administrators: everyone)
/// plus the signed-in user's own profile (<c>/api/users/me</c>, any role).
/// </summary>
[ApiController]
[Authorize]
[Route("api/users")]
public partial class UsersController(AppDbContext db, CurrentUser me, AuditService audit) : ControllerBase
{
    public class UserRequest
    {
        public int GroupId { get; set; }
        public string? Email { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? Roles { get; set; }
        public List<int>? ExtraGroupIds { get; set; }
    }

    public record StatusRequest(bool Disabled);
    public record ResetPasswordRequest(string? Password);
    public record ProfileRequest(string? FirstName, string? LastName, string? PhoneNumber);

    private static readonly string[] PrivilegedRoles = [Roles.SystemAdmin, Roles.SupportAgent];
    private static readonly string[] AdminRoles = [Roles.SystemAdmin, Roles.GroupAdmin];

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    // ------------------------------------------------------------------ list / get

    /// <summary>GET /api/users?groupId= — users whose primary or additional group is the given group (all accessible groups when omitted).</summary>
    [HttpGet]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> List([FromQuery] int? groupId, [FromQuery] string? search)
    {
        var q = db.Users.AsNoTracking().Where(u => !u.IsDeleted);
        if (groupId is > 0 || !me.IsSystemAdmin)
        {
            var scope = await me.ScopeAsync(groupId);
            q = q.Where(u => scope.Contains(u.GroupId) || u.ExtraGroups.Any(x => scope.Contains(x.GroupId)));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => u.Email.Contains(s) || u.Username.Contains(s) || (u.FirstName != null && u.FirstName.Contains(s)) || (u.LastName != null && u.LastName.Contains(s)));
        }
        var rows = await Project(q).OrderBy(u => u.GroupName).ThenBy(u => u.Username).ToListAsync();
        var accessible = await me.GroupIdsAsync();
        return Ok(rows.Select(r => Dto(r, accessible)));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> Get(int id)
    {
        var row = await Project(db.Users.AsNoTracking().Where(u => u.Id == id && !u.IsDeleted)).FirstOrDefaultAsync()
                  ?? throw ApiException.NotFound("User");
        var accessible = await me.GroupIdsAsync();
        if (!accessible.Contains(row.GroupId) && !row.ExtraGroupIds.Any(accessible.Contains))
            throw ApiException.NotFound("User");
        return Ok(Dto(row, accessible));
    }

    // ------------------------------------------------------------------ create / update

    [HttpPost]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> Create(UserRequest req)
    {
        var user = new User();
        var (roles, extra) = await ValidateAsync(req, null);
        Passwords.Validate(req.Password);
        Apply(user, req);
        user.PasswordHash = Passwords.Hash(req.Password!);
        user.Roles = roles.Select(r => new UserRole { Role = r }).ToList();
        user.ExtraGroups = extra.Select(g => new UserGroup { GroupId = g }).ToList();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        audit.Log("User", user.Id, "Created", $"Username: {user.Username}; Email: {user.Email}; Roles: {RoleLabels(roles)}", user.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "User created.", id = user.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> Update(int id, UserRequest req)
    {
        var user = await LoadManageableAsync(id);
        var accessible = await me.GroupIdsAsync();
        var (roles, extra) = await ValidateAsync(req, user);

        var isSelf = user.Id == me.Id;
        var oldRoles = user.Roles.Select(r => r.Role).ToList();
        if (isSelf)
        {
            var lost = oldRoles.Intersect(AdminRoles).Except(roles).ToList();
            if (lost.Count > 0)
                throw ApiException.Bad($"You cannot remove your own {Roles.Labels[lost[0]]} role.");
        }
        if (!me.IsSystemAdmin)
        {
            // Group Admins keep privileged roles assigned by a System Admin (they just cannot add or remove them).
            roles = roles.Where(r => !PrivilegedRoles.Contains(r)).Concat(oldRoles.Where(PrivilegedRoles.Contains)).Distinct().ToList();
            // …and additional groups they cannot see stay untouched.
            extra = extra.Concat(user.ExtraGroups.Select(x => x.GroupId).Where(g => !accessible.Contains(g))).Distinct().Where(g => g != req.GroupId).ToList();
        }

        var changes = new List<string>();
        void Note(string label, string? a, string? b)
        {
            if ((a ?? "") != (b ?? "")) changes.Add($"{label}: {(string.IsNullOrEmpty(a) ? "(blank)" : a)} → {(string.IsNullOrEmpty(b) ? "(blank)" : b)}");
        }
        var groupNames = await db.Groups.AsNoTracking().Where(g => g.Id == user.GroupId || g.Id == req.GroupId).ToDictionaryAsync(g => g.Id, g => g.Name);
        Note("Group", groupNames.GetValueOrDefault(user.GroupId), groupNames.GetValueOrDefault(req.GroupId));
        Note("Email", user.Email, Text.Clean(req.Email));
        Note("Username", user.Username, Text.Clean(req.Username));
        Note("First Name", user.FirstName, Text.Clean(req.FirstName));
        Note("Last Name", user.LastName, Text.Clean(req.LastName));
        Note("Phone Number", user.PhoneNumber, NormalizePhone(req.PhoneNumber));
        Note("Roles", RoleLabels(oldRoles), RoleLabels(roles));
        var oldExtra = user.ExtraGroups.Select(x => x.GroupId).OrderBy(x => x).ToList();
        if (!oldExtra.SequenceEqual(extra.OrderBy(x => x))) changes.Add("Additional groups changed");

        Apply(user, req);
        if (!string.IsNullOrEmpty(req.Password))
        {
            Passwords.Validate(req.Password);
            user.PasswordHash = Passwords.Hash(req.Password);
            changes.Add("Password changed");
        }

        user.Roles.RemoveAll(r => !roles.Contains(r.Role));
        foreach (var r in roles.Where(r => user.Roles.All(x => x.Role != r)))
            user.Roles.Add(new UserRole { Role = r });
        user.ExtraGroups.RemoveAll(x => !extra.Contains(x.GroupId));
        foreach (var g in extra.Where(g => user.ExtraGroups.All(x => x.GroupId != g)))
            user.ExtraGroups.Add(new UserGroup { UserId = user.Id, GroupId = g });

        if (changes.Count > 0) audit.Log("User", user.Id, "Updated", string.Join("\n", changes), user.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "User updated.", id = user.Id });
    }

    [HttpPost("{id:int}/status")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> SetStatus(int id, StatusRequest req)
    {
        var user = await LoadManageableAsync(id);
        if (user.Id == me.Id && req.Disabled) throw ApiException.Bad("You cannot disable your own account.");
        if (user.Disabled != req.Disabled)
        {
            user.Disabled = req.Disabled;
            audit.Log("User", user.Id, req.Disabled ? "Disabled" : "Enabled", null, user.GroupId);
            await db.SaveChangesAsync();
        }
        return Ok(new { message = req.Disabled ? "User disabled." : "User enabled." });
    }

    [HttpPost("{id:int}/reset-password")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest req)
    {
        var user = await LoadManageableAsync(id);
        Passwords.Validate(req.Password);
        user.PasswordHash = Passwords.Hash(req.Password!);
        audit.Log("User", user.Id, "Password reset", null, user.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = $"Password reset for {user.Username}." });
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await LoadManageableAsync(id);
        if (user.Id == me.Id) throw ApiException.Bad("You cannot delete your own account.");
        user.IsDeleted = true;
        audit.Log("User", user.Id, "Deleted", $"Username: {user.Username}; Email: {user.Email}", user.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "User deleted." });
    }

    /// <summary>GET /api/users/{id}/agreement-certificate — printable proof of the user-agreement acceptance.</summary>
    [HttpGet("{id:int}/agreement-certificate")]
    [Authorize(Roles = Access.Users)]
    public async Task<IActionResult> AgreementCertificate(int id)
    {
        var row = await Project(db.Users.AsNoTracking().Where(u => u.Id == id && !u.IsDeleted)).FirstOrDefaultAsync()
                  ?? throw ApiException.NotFound("User");
        var accessible = await me.GroupIdsAsync();
        if (!accessible.Contains(row.GroupId) && !row.ExtraGroupIds.Any(accessible.Contains))
            throw ApiException.NotFound("User");
        if (!row.AgreementAccepted)
            throw ApiException.Bad("This user has not accepted the user agreement yet.");

        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        var accepted = row.AgreementAcceptedAt is DateTime at ? DateTime.SpecifyKind(at, DateTimeKind.Utc).ToString("MMMM d, yyyy 'at' h:mm:ss tt 'UTC'") : "Date not recorded";
        var name = $"{row.FirstName} {row.LastName}".Trim();
        var html = $$"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><title>User Agreement Acceptance - {{H(row.Username)}}</title>
            <style>
              body { font-family: Segoe UI, Arial, sans-serif; color: #1e293b; margin: 40px; }
              .box { max-width: 720px; margin: 0 auto; border: 2px solid #ea580c; border-radius: 10px; padding: 32px 40px; }
              h1 { font-size: 22px; margin: 0 0 4px; } .sub { color: #64748b; margin: 0 0 24px; }
              table { border-collapse: collapse; width: 100%; } td { padding: 8px 4px; border-bottom: 1px solid #e2e8f0; }
              td:first-child { color: #64748b; width: 38%; } .foot { margin-top: 24px; font-size: 12px; color: #64748b; }
            </style></head>
            <body><div class="box">
              <h1>Certificate of User Agreement Acceptance</h1>
              <p class="sub">AWFI Agent Finishing &amp; Finish Genius Software Support User Agreement</p>
              <table>
                <tr><td>User</td><td>{{H(string.IsNullOrEmpty(name) ? row.Username : name)}}</td></tr>
                <tr><td>Username</td><td>{{H(row.Username)}}</td></tr>
                <tr><td>Email</td><td>{{H(row.Email)}}</td></tr>
                <tr><td>Group</td><td>{{H(row.GroupName)}}</td></tr>
                <tr><td>Accepted</td><td>{{H(accepted)}}</td></tr>
              </table>
              <p class="foot">The user confirmed that they have read, understood, and agree to the terms and conditions outlined in the
              document provided. Generated by Finish Genius on {{DateTime.UtcNow:MMMM d, yyyy h:mm tt}} UTC by {{H(me.UserName)}}.</p>
            </div></body></html>
            """;
        return File(Encoding.UTF8.GetBytes(html), "text/html", $"agreement-acceptance-{row.Username}.html");
    }

    // ------------------------------------------------------------------ own profile (any signed-in user)

    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        var row = await Project(db.Users.AsNoTracking().Where(u => u.Id == me.Id && !u.IsDeleted)).FirstOrDefaultAsync()
                  ?? throw ApiException.NotFound("User");
        var extraNames = await db.Groups.AsNoTracking().Where(g => row.ExtraGroupIds.Contains(g.Id) && !g.IsDeleted)
            .OrderBy(g => g.Name).Select(g => g.Name).ToListAsync();
        return Ok(new
        {
            row.Id, row.Username, row.Email, row.FirstName, row.LastName, row.PhoneNumber, row.GroupId, row.GroupName,
            ExtraGroups = extraNames,
            row.Roles, RoleLabels = row.Roles.Select(r => Roles.Labels.GetValueOrDefault(r, r)).ToList(),
            row.AgreementAccepted, row.AgreementAcceptedAt, row.CreatedAt, row.LastLoginAt,
        });
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile(ProfileRequest req)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == me.Id && !u.IsDeleted) ?? throw ApiException.NotFound("User");
        var phone = ValidPhone(req.PhoneNumber);
        var first = Text.Clean(req.FirstName);
        var last = Text.Clean(req.LastName);
        if (first?.Length > 100 || last?.Length > 100) throw ApiException.Bad("Names must be 100 characters or fewer.");
        var changes = new List<string>();
        if (user.FirstName != first) changes.Add($"First Name: {user.FirstName ?? "(blank)"} → {first ?? "(blank)"}");
        if (user.LastName != last) changes.Add($"Last Name: {user.LastName ?? "(blank)"} → {last ?? "(blank)"}");
        if (user.PhoneNumber != phone) changes.Add($"Phone Number: {user.PhoneNumber ?? "(blank)"} → {phone ?? "(blank)"}");
        user.FirstName = first;
        user.LastName = last;
        user.PhoneNumber = phone;
        if (changes.Count > 0) audit.Log("User", user.Id, "Profile updated", string.Join("\n", changes), user.GroupId);
        await db.SaveChangesAsync();
        return Ok(new { message = "Profile updated." });
    }

    // ------------------------------------------------------------------ helpers

    private sealed class UserRow
    {
        public int Id { get; init; }
        public int GroupId { get; init; }
        public string? GroupName { get; init; }
        public string Email { get; init; } = "";
        public string Username { get; init; } = "";
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? PhoneNumber { get; init; }
        public bool Disabled { get; init; }
        public bool AgreementAccepted { get; init; }
        public DateTime? AgreementAcceptedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? LastLoginAt { get; init; }
        public List<string> Roles { get; init; } = [];
        public List<int> ExtraGroupIds { get; init; } = [];
    }

    private static IQueryable<UserRow> Project(IQueryable<User> q) => q.Select(u => new UserRow
    {
        Id = u.Id, GroupId = u.GroupId, GroupName = u.Group!.Name, Email = u.Email, Username = u.Username,
        FirstName = u.FirstName, LastName = u.LastName, PhoneNumber = u.PhoneNumber, Disabled = u.Disabled,
        AgreementAccepted = u.AgreementAccepted, AgreementAcceptedAt = u.AgreementAcceptedAt,
        CreatedAt = u.CreatedAt, LastLoginAt = u.LastLoginAt,
        Roles = u.Roles.Select(r => r.Role).ToList(),
        ExtraGroupIds = u.ExtraGroups.Select(x => x.GroupId).ToList(),
    });

    private object Dto(UserRow r, List<int> accessible) => new
    {
        r.Id, r.GroupId, r.GroupName, r.Email, r.Username, r.FirstName, r.LastName, r.PhoneNumber, r.Disabled,
        r.AgreementAccepted, r.AgreementAcceptedAt, r.CreatedAt, r.LastLoginAt,
        Roles = Domain.Roles.All.Where(r.Roles.Contains).ToList(),
        r.ExtraGroupIds,
        IsSelf = r.Id == me.Id,
        CanManage = CanManage(r.GroupId, r.Roles, accessible),
    };

    private bool CanManage(int groupId, IEnumerable<string> roles, List<int> accessible) =>
        me.IsSystemAdmin || (me.IsGroupAdmin && accessible.Contains(groupId) && !roles.Contains(Roles.SystemAdmin));

    /// <summary>Loads a user for modification, enforcing group scope and the "Group Admins cannot touch System Admins" rule.</summary>
    private async Task<User> LoadManageableAsync(int id)
    {
        var user = await db.Users.Include(u => u.Roles).Include(u => u.ExtraGroups).FirstOrDefaultAsync(u => u.Id == id && !u.IsDeleted)
                   ?? throw ApiException.NotFound("User");
        var accessible = await me.GroupIdsAsync();
        if (!me.IsSystemAdmin && !accessible.Contains(user.GroupId))
        {
            if (user.ExtraGroups.Any(x => accessible.Contains(x.GroupId)))
                throw new ApiException(StatusCodes.Status403Forbidden, "This user belongs to another group. Only an administrator of that group can modify the account.");
            throw ApiException.NotFound("User");
        }
        if (!CanManage(user.GroupId, user.Roles.Select(r => r.Role), accessible))
            throw new ApiException(StatusCodes.Status403Forbidden, "Only a System Administrator can modify a System Administrator account.");
        return user;
    }

    /// <summary>Validates the create/edit form; returns the clean role list and additional group ids.</summary>
    private async Task<(List<string> roles, List<int> extraGroups)> ValidateAsync(UserRequest req, User? existing)
    {
        if (req.GroupId <= 0) throw ApiException.Bad("Group is required.");
        await me.EnsureGroupAsync(req.GroupId);

        var email = Text.Req(req.Email, "Email Address");
        if (email.Length > 256 || !EmailRegex().IsMatch(email)) throw ApiException.Bad("Not a valid email address.");
        var username = Text.Req(req.Username, "Username");
        if (username.Length > 100) throw ApiException.Bad("Username must be 100 characters or fewer.");
        if (username.Any(char.IsWhiteSpace)) throw ApiException.Bad("Username cannot contain spaces.");
        if (Text.Clean(req.FirstName)?.Length > 100 || Text.Clean(req.LastName)?.Length > 100)
            throw ApiException.Bad("Names must be 100 characters or fewer.");
        ValidPhone(req.PhoneNumber);

        var existingId = existing?.Id ?? 0;
        if (await db.Users.AnyAsync(u => !u.IsDeleted && u.Id != existingId && u.Email == email))
            throw ApiException.Bad("A user with this email address already exists.");
        if (await db.Users.AnyAsync(u => !u.IsDeleted && u.Id != existingId && u.Username == username))
            throw ApiException.Bad("A user with this username already exists.");

        var roles = (req.Roles ?? []).Select(r => r?.Trim() ?? "").Where(r => r != "").Distinct().ToList();
        if (roles.Count == 0) throw ApiException.Bad("Select at least one user role.");
        var invalid = roles.FirstOrDefault(r => !Roles.All.Contains(r));
        if (invalid != null) throw ApiException.Bad($"Unknown role \"{invalid}\".");
        if (!me.IsSystemAdmin)
        {
            var existingRoles = existing?.Roles.Select(r => r.Role).ToList() ?? [];
            if (roles.Any(r => PrivilegedRoles.Contains(r) && !existingRoles.Contains(r)))
                throw new ApiException(StatusCodes.Status403Forbidden, "Group Administrators cannot assign the System Administrator or FG Support Agent role.");
        }

        var extra = (req.ExtraGroupIds ?? []).Where(g => g > 0 && g != req.GroupId).Distinct().ToList();
        var accessible = await me.GroupIdsAsync();
        var existingExtra = existing?.ExtraGroups.Select(x => x.GroupId).ToList() ?? [];
        foreach (var g in extra.Where(g => !existingExtra.Contains(g)))
            if (!accessible.Contains(g))
                throw new ApiException(StatusCodes.Status403Forbidden, "You do not have access to one of the selected additional groups.");
        return (roles, extra.Where(g => accessible.Contains(g) || existingExtra.Contains(g)).ToList());
    }

    private static void Apply(User user, UserRequest req)
    {
        user.GroupId = req.GroupId;
        user.Email = req.Email!.Trim();
        user.Username = req.Username!.Trim();
        user.FirstName = Text.Clean(req.FirstName);
        user.LastName = Text.Clean(req.LastName);
        user.PhoneNumber = NormalizePhone(req.PhoneNumber);
    }

    /// <summary>Formats 10 digits as the legacy mask "(718) 697 - 9892"; blank stays blank.</summary>
    private static string? NormalizePhone(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        return digits.Length == 10 ? $"({digits[..3]}) {digits[3..6]} - {digits[6..]}" : phone?.Trim();
    }

    private static string? ValidPhone(string? phone)
    {
        var digits = (phone ?? "").Count(char.IsDigit);
        if (digits != 0 && digits != 10) throw ApiException.Bad("Not a valid phone number");
        return NormalizePhone(phone);
    }

    private static string RoleLabels(IEnumerable<string> roles) =>
        string.Join(", ", Roles.All.Where(roles.Contains).Select(r => Roles.Labels[r]));
}
