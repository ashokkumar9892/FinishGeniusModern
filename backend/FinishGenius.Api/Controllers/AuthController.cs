using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokens, CurrentUser me) : ControllerBase
{
    public record LoginRequest(string Username, string Password, bool RememberMe);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var name = (req.Username ?? "").Trim();
        // Imported legacy data can contain the same username/email on an enabled and a disabled account.
        var candidates = await db.Users.Include(u => u.Roles)
            .Where(u => !u.IsDeleted && (u.Username == name || u.Email == name))
            .OrderBy(u => u.Disabled).ThenByDescending(u => u.Id).ToListAsync();
        var user = candidates.FirstOrDefault(u => Passwords.Verify(u.PasswordHash, req.Password ?? ""));
        if (user == null)
            return Unauthorized(new { message = "Invalid username or password." });
        if (user.Disabled)
            return Unauthorized(new { message = "This account is disabled. Contact your administrator." });

        if (Passwords.IsLegacyBcrypt(user.PasswordHash))
            user.PasswordHash = Passwords.Hash(req.Password!); // upgrade legacy hash
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var (token, expires) = tokens.Create(user, req.RememberMe);
        return Ok(new { token, expires });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == me.Id && !u.IsDeleted);
        if (user == null || user.Disabled) return Unauthorized(new { message = "Session expired." });
        var groupIds = await me.GroupIdsAsync();
        var groups = await db.Groups.AsNoTracking().Where(g => groupIds.Contains(g.Id))
            .OrderBy(g => g.Name).Select(g => new { g.Id, g.Name, g.LogoFile }).ToListAsync();
        var unread = await db.MessageRecipients.CountAsync(r => r.UserId == me.Id && !r.Viewed);
        var defaultGroup = user.DefaultGroupId is int d && groupIds.Contains(d) ? d : user.GroupId;
        return Ok(new
        {
            user.Id, user.Username, user.Email, user.FirstName, user.LastName, user.PhoneNumber, user.GroupId,
            DefaultGroupId = defaultGroup, user.AgreementAccepted,
            Roles = user.Roles.Select(r => r.Role).ToList(),
            Groups = groups,
            UnreadMessages = unread,
        });
    }

    [HttpPost("accept-agreement")]
    [Authorize]
    public async Task<IActionResult> AcceptAgreement()
    {
        var user = await db.Users.FirstAsync(u => u.Id == me.Id);
        user.AgreementAccepted = true;
        user.AgreementAcceptedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return Ok(new { message = "Agreement accepted." });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req)
    {
        var user = await db.Users.FirstAsync(u => u.Id == me.Id);
        if (!Passwords.Verify(user.PasswordHash, req.CurrentPassword ?? ""))
            throw ApiException.Bad("Current password is incorrect.");
        Passwords.Validate(req.NewPassword);
        user.PasswordHash = Passwords.Hash(req.NewPassword);
        await db.SaveChangesAsync();
        return Ok(new { message = "Password changed." });
    }

    /// <summary>"Choose" on the Groups page: sets the group the user lands in.</summary>
    [HttpPost("default-group/{groupId:int}")]
    [Authorize]
    public async Task<IActionResult> SetDefaultGroup(int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var user = await db.Users.FirstAsync(u => u.Id == me.Id);
        user.DefaultGroupId = groupId;
        await db.SaveChangesAsync();
        return Ok(new { message = "Default group updated." });
    }

    /// <summary>"Unselect" on the Groups page: clears the chosen default group.</summary>
    [HttpDelete("default-group")]
    [Authorize]
    public async Task<IActionResult> ClearDefaultGroup()
    {
        var user = await db.Users.FirstAsync(u => u.Id == me.Id);
        user.DefaultGroupId = null;
        await db.SaveChangesAsync();
        return Ok(new { message = "Default group cleared." });
    }
}
