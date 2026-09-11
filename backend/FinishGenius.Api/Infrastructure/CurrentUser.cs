using System.Security.Claims;
using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Infrastructure;

/// <summary>Who is calling, and which groups (tenants) they may touch.</summary>
public class CurrentUser(IHttpContextAccessor http, AppDbContext db)
{
    private List<int>? _groupIds;

    private ClaimsPrincipal Principal => http.HttpContext?.User ?? new ClaimsPrincipal();

    public int Id => int.TryParse(Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    public string UserName => Principal.FindFirstValue(ClaimTypes.Name) ?? "";
    public int GroupId => int.TryParse(Principal.FindFirstValue("groupId"), out var g) ? g : 0;
    public bool IsInRole(string role) => Principal.IsInRole(role);
    public bool IsSystemAdmin => IsInRole(Roles.SystemAdmin);
    public bool IsGroupAdmin => IsInRole(Roles.GroupAdmin);
    /// <summary>System admins and support agents work across all groups.</summary>
    public bool SeesAllGroups => IsSystemAdmin || IsInRole(Roles.SupportAgent);
    public bool IsAdmin => IsSystemAdmin || IsGroupAdmin;
    public IReadOnlyList<string> RoleNames => Principal.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();
    /// <summary>Legacy "isGroupAdmin" lock: users whose only role is FG Pro cannot change the ingredients of Complete formulas.</summary>
    public bool IsFgProOnly => RoleNames is { Count: > 0 } r && r.All(x => x == Roles.FGPro);

    public async Task<List<int>> GroupIdsAsync()
    {
        if (_groupIds != null) return _groupIds;
        if (SeesAllGroups)
            _groupIds = await db.Groups.Where(g => !g.IsDeleted).Select(g => g.Id).ToListAsync();
        else
        {
            var extra = await db.UserGroups.Where(x => x.UserId == Id).Select(x => x.GroupId).ToListAsync();
            _groupIds = await db.Groups.Where(g => !g.IsDeleted && (g.Id == GroupId || extra.Contains(g.Id)))
                .Select(g => g.Id).ToListAsync();
        }
        return _groupIds;
    }

    public async Task<bool> CanAccessGroupAsync(int groupId) => (await GroupIdsAsync()).Contains(groupId);

    /// <summary>Throws 403 unless the caller may access <paramref name="groupId"/>.</summary>
    public async Task EnsureGroupAsync(int groupId)
    {
        if (!await CanAccessGroupAsync(groupId))
            throw new ApiException(StatusCodes.Status403Forbidden, "You do not have access to this group.");
    }

    /// <summary>Resolves an optional group filter: the requested group if allowed, otherwise all accessible groups.</summary>
    public async Task<List<int>> ScopeAsync(int? groupId)
    {
        if (groupId is > 0)
        {
            await EnsureGroupAsync(groupId.Value);
            return [groupId.Value];
        }
        return await GroupIdsAsync();
    }

    public void EnsureAdmin()
    {
        if (!IsAdmin) throw new ApiException(StatusCodes.Status403Forbidden, "Administrator rights are required.");
    }

    public void EnsureSystemAdmin()
    {
        if (!IsSystemAdmin) throw new ApiException(StatusCodes.Status403Forbidden, "System Administrator rights are required.");
    }
}
