using FinishGenius.Api.Data;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Controllers;

/// <summary>
/// DPM Center: internal messaging between users, FG APP Support / Finishing Questions to AWFI staff,
/// and Internal DPM between System Administrators. Inbox/sent are personal (not group scoped) so they
/// match the unread badge from <c>/api/auth/me</c>; sending is scoped to a group the caller can access.
/// </summary>
[ApiController]
[Authorize(Roles = Access.Everyone)]
[Route("api/messages")]
public class MessagesController(AppDbContext db, CurrentUser me) : ControllerBase
{
    public static class Types
    {
        public const string General = "General", Support = "Support", Question = "Question", Internal = "Internal";
        public static readonly string[] All = [General, Support, Question, Internal];

        public static string Label(string t) => t switch
        {
            Support => "FG APP Support",
            Question => "Finishing Questions",
            Internal => "Internal DPM",
            _ => "General message",
        };
    }

    public record SendRequest(int GroupId, string? Type, string? Subject, string? Body, List<int>? RecipientUserIds, int? ReplyToId);

    private const int ListLimit = 500;
    private const int SnippetLength = 160;

    // ------------------------------------------------------------------ lists

    [HttpGet("inbox")]
    public async Task<IActionResult> Inbox()
    {
        var uid = me.Id;
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.Recipients.Any(r => r.UserId == uid))
            .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id)
            .Take(ListLimit)
            .Select(m => new
            {
                m.Id, m.GroupId,
                GroupName = db.Groups.Where(g => g.Id == m.GroupId).Select(g => g.Name).FirstOrDefault(),
                m.FromUserId, FromFirst = m.FromUser!.FirstName, FromLast = m.FromUser.LastName, FromUsername = m.FromUser.Username,
                m.Subject, Snippet = m.Body.Length > SnippetLength ? m.Body.Substring(0, SnippetLength) : m.Body,
                m.MessageType, m.ReplyToId, m.SentAt,
                Viewed = !m.Recipients.Any(r => r.UserId == uid && !r.Viewed),
            })
            .ToListAsync();
        return Ok(rows.Select(m => new
        {
            m.Id, m.GroupId, m.GroupName, m.FromUserId, FromName = Name(m.FromFirst, m.FromLast, m.FromUsername),
            m.Subject, m.Snippet, Type = m.MessageType, TypeLabel = Types.Label(m.MessageType), m.ReplyToId, m.SentAt, m.Viewed,
        }));
    }

    [HttpGet("sent")]
    public async Task<IActionResult> Sent()
    {
        var uid = me.Id;
        var rows = await db.Messages.AsNoTracking()
            .Where(m => m.FromUserId == uid)
            .OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id)
            .Take(ListLimit)
            .Select(m => new
            {
                m.Id, m.GroupId,
                GroupName = db.Groups.Where(g => g.Id == m.GroupId).Select(g => g.Name).FirstOrDefault(),
                m.Subject, Snippet = m.Body.Length > SnippetLength ? m.Body.Substring(0, SnippetLength) : m.Body,
                m.MessageType, m.ReplyToId, m.SentAt,
                Recipients = m.Recipients.Select(r => new { r.User!.FirstName, r.User.LastName, r.User.Username }).Take(4).ToList(),
                RecipientCount = m.Recipients.Count,
            })
            .ToListAsync();
        return Ok(rows.Select(m => new
        {
            m.Id, m.GroupId, m.GroupName, m.Subject, m.Snippet, Type = m.MessageType, TypeLabel = Types.Label(m.MessageType),
            m.ReplyToId, m.SentAt, Viewed = true,
            ToNames = m.Recipients.Take(3).Select(r => Name(r.FirstName, r.LastName, r.Username)).ToList(),
            m.RecipientCount,
        }));
    }

    // ------------------------------------------------------------------ read

    /// <summary>GET /api/messages/{id} — the whole conversation (original + replies) visible to the caller; marks it read.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var uid = me.Id;
        var msg = await db.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id) ?? throw ApiException.NotFound("Message");
        if (!await IsParticipantAsync(msg.Id, msg.FromUserId)) throw ApiException.NotFound("Message");

        var threadIds = await ThreadIdsAsync(msg);
        var messages = await db.Messages.AsNoTracking()
            .Where(m => threadIds.Contains(m.Id) && (m.FromUserId == uid || m.Recipients.Any(r => r.UserId == uid)))
            .OrderBy(m => m.SentAt).ThenBy(m => m.Id)
            .Select(m => new
            {
                m.Id, m.FromUserId, FromFirst = m.FromUser!.FirstName, FromLast = m.FromUser.LastName,
                FromUsername = m.FromUser.Username, FromEmail = m.FromUser.Email,
                m.Subject, m.Body, m.MessageType, m.ReplyToId, m.SentAt, m.GroupId,
                Recipients = m.Recipients.Select(r => new { r.UserId, r.User!.FirstName, r.User.LastName, r.User.Username, r.Viewed }).ToList(),
            })
            .ToListAsync();

        // Mark every message of the conversation read for the caller.
        var unread = await db.MessageRecipients.Where(r => r.UserId == uid && !r.Viewed && threadIds.Contains(r.MessageId)).ToListAsync();
        unread.ForEach(r => r.Viewed = true);
        if (unread.Count > 0) await db.SaveChangesAsync();

        var root = messages.FirstOrDefault() ?? throw ApiException.NotFound("Message");
        var groupName = await db.Groups.Where(g => g.Id == msg.GroupId).Select(g => g.Name).FirstOrDefaultAsync();
        return Ok(new
        {
            msg.Id, Subject = root.Subject, Type = msg.MessageType, TypeLabel = Types.Label(msg.MessageType),
            msg.GroupId, GroupName = groupName,
            MarkedRead = unread.Count,
            Messages = messages.Select(m => new
            {
                m.Id, m.FromUserId, FromName = Name(m.FromFirst, m.FromLast, m.FromUsername), m.FromEmail,
                m.Subject, m.Body, m.ReplyToId, m.SentAt, IsMine = m.FromUserId == uid,
                Recipients = m.Recipients.Select(r => new { Id = r.UserId, Name = Name(r.FirstName, r.LastName, r.Username), r.Viewed }),
            }),
        });
    }

    // ------------------------------------------------------------------ send

    [HttpPost]
    public async Task<IActionResult> Send(SendRequest req)
    {
        var uid = me.Id;
        var body = (req.Body ?? "").Trim();
        if (body.Length == 0) throw ApiException.Bad("Message is required.");
        if (body.Length > 20000) throw ApiException.Bad("Message must be 20,000 characters or fewer.");

        int groupId;
        string type;
        string subject;
        var participants = new HashSet<int>();
        if (req.ReplyToId is int parentId)
        {
            var parent = await db.Messages.AsNoTracking().Include(m => m.Recipients).FirstOrDefaultAsync(m => m.Id == parentId)
                         ?? throw ApiException.NotFound("Message");
            if (parent.FromUserId != uid && parent.Recipients.All(r => r.UserId != uid)) throw ApiException.NotFound("Message");
            groupId = parent.GroupId;
            type = parent.MessageType;
            participants.Add(parent.FromUserId);
            foreach (var r in parent.Recipients) participants.Add(r.UserId);
            subject = Text.Clean(req.Subject) ?? (parent.Subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase) ? parent.Subject : $"RE: {parent.Subject}");
        }
        else
        {
            if (req.GroupId <= 0) throw ApiException.Bad("Group is required.");
            await me.EnsureGroupAsync(req.GroupId);
            groupId = req.GroupId;
            type = Types.All.FirstOrDefault(t => string.Equals(t, req.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
                   ?? throw ApiException.Bad("Select a valid message type.");
            if (type == Types.Internal && !me.IsSystemAdmin)
                throw new ApiException(StatusCodes.Status403Forbidden, "Only System Administrators can send Internal DPM messages.");
            subject = Text.Req(req.Subject, "Subject");
        }
        if (subject.Length > 400) throw ApiException.Bad("Subject must be 400 characters or fewer.");

        var requested = (req.RecipientUserIds ?? []).Where(x => x > 0).Distinct().ToList();
        List<int> recipients;
        if (requested.Count > 0)
        {
            var allowed = await AllowedRecipientsQuery(groupId).Select(u => u.Id).Where(id => requested.Contains(id)).ToListAsync();
            // Conversation participants may always be answered, even across groups.
            if (participants.Count > 0)
                allowed.AddRange(await ActiveUsers().Where(u => participants.Contains(u.Id) && requested.Contains(u.Id)).Select(u => u.Id).ToListAsync());
            if (type == Types.Internal)
                allowed = await ActiveUsers().Where(u => allowed.Contains(u.Id) && u.Roles.Any(r => r.Role == Roles.SystemAdmin)).Select(u => u.Id).ToListAsync();
            if (requested.Any(x => !allowed.Contains(x)))
                throw ApiException.Bad("One or more recipients are not valid for this message.");
            recipients = requested;
        }
        else if (participants.Count > 0)
        {
            recipients = await ActiveUsers().Where(u => participants.Contains(u.Id)).Select(u => u.Id).ToListAsync();
        }
        else
        {
            recipients = type switch
            {
                Types.Support or Types.Question => await ActiveUsers()
                    .Where(u => u.Roles.Any(r => r.Role == Roles.SupportAgent || r.Role == Roles.SystemAdmin)).Select(u => u.Id).ToListAsync(),
                Types.Internal => await ActiveUsers().Where(u => u.Roles.Any(r => r.Role == Roles.SystemAdmin)).Select(u => u.Id).ToListAsync(),
                _ => throw ApiException.Bad("Select at least one recipient."),
            };
        }
        recipients.Remove(uid);
        if (recipients.Count == 0)
            throw ApiException.Bad(type is Types.Support or Types.Question or Types.Internal && requested.Count == 0 && participants.Count == 0
                ? "There is no support staff available to receive this message."
                : "Select at least one recipient other than yourself.");

        var message = new Message
        {
            GroupId = groupId, FromUserId = uid, Subject = subject, Body = body, MessageType = type, ReplyToId = req.ReplyToId,
            Recipients = recipients.Distinct().Select(r => new MessageRecipient { UserId = r }).ToList(),
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        return Ok(new { message = "Message sent.", id = message.Id, recipientCount = message.Recipients.Count });
    }

    // ------------------------------------------------------------------ housekeeping

    [HttpPost("{id:int}/unread")]
    public async Task<IActionResult> MarkUnread(int id)
    {
        var row = await db.MessageRecipients.FirstOrDefaultAsync(r => r.MessageId == id && r.UserId == me.Id) ?? throw ApiException.NotFound("Message");
        row.Viewed = false;
        await db.SaveChangesAsync();
        return Ok(new { message = "Marked as unread." });
    }

    [HttpPost("mark-all-read")]
    public async Task<IActionResult> MarkAllRead()
    {
        var uid = me.Id;
        var count = await db.MessageRecipients.Where(r => r.UserId == uid && !r.Viewed)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Viewed, true));
        return Ok(new { message = count == 0 ? "No unread messages." : $"{count} message{(count == 1 ? "" : "s")} marked as read.", count });
    }

    /// <summary>DELETE /api/messages/{id} — removes the message from the caller's inbox (other recipients keep it).</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await db.MessageRecipients.FirstOrDefaultAsync(r => r.MessageId == id && r.UserId == me.Id) ?? throw ApiException.NotFound("Message");
        db.MessageRecipients.Remove(row);
        await db.SaveChangesAsync();
        return Ok(new { message = "Message deleted." });
    }

    /// <summary>GET /api/messages/recipients?groupId= — members of the group plus AWFI support staff.</summary>
    [HttpGet("recipients")]
    public async Task<IActionResult> Recipients([FromQuery] int groupId)
    {
        await me.EnsureGroupAsync(groupId);
        var uid = me.Id;
        var rows = await AllowedRecipientsQuery(groupId).Where(u => u.Id != uid)
            .Select(u => new
            {
                u.Id, u.FirstName, u.LastName, u.Username, u.Email, u.GroupId, GroupName = u.Group!.Name,
                IsSystemAdmin = u.Roles.Any(r => r.Role == Roles.SystemAdmin),
                IsSupport = u.Roles.Any(r => r.Role == Roles.SupportAgent),
            })
            .ToListAsync();
        return Ok(rows
            .Select(u => new
            {
                u.Id, Name = Name(u.FirstName, u.LastName, u.Username), u.Email, u.GroupName,
                Tag = u.IsSupport ? "FG Support" : u.IsSystemAdmin ? "System Admin" : null,
                InGroup = u.GroupId == groupId,
            })
            .OrderBy(u => u.Tag != null).ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase));
    }

    // ------------------------------------------------------------------ helpers

    private IQueryable<User> ActiveUsers() => db.Users.AsNoTracking().Where(u => !u.IsDeleted && !u.Disabled);

    /// <summary>Users of the group (primary or additional) and AWFI support staff.</summary>
    private IQueryable<User> AllowedRecipientsQuery(int groupId) => ActiveUsers().Where(u =>
        u.GroupId == groupId || u.ExtraGroups.Any(x => x.GroupId == groupId) ||
        u.Roles.Any(r => r.Role == Roles.SupportAgent || r.Role == Roles.SystemAdmin));

    private async Task<bool> IsParticipantAsync(int messageId, int fromUserId) =>
        fromUserId == me.Id || await db.MessageRecipients.AnyAsync(r => r.MessageId == messageId && r.UserId == me.Id);

    /// <summary>Ids of the conversation containing <paramref name="msg"/>: walk up to the root, then collect all replies.</summary>
    private async Task<List<int>> ThreadIdsAsync(Message msg)
    {
        var rootId = msg.Id;
        var parent = msg.ReplyToId;
        for (var guard = 0; parent != null && guard < 100; guard++)
        {
            var p = parent.Value;
            var next = await db.Messages.AsNoTracking().Where(m => m.Id == p).Select(m => new { m.Id, m.ReplyToId }).FirstOrDefaultAsync();
            if (next == null) break;
            rootId = next.Id;
            parent = next.ReplyToId;
        }
        var ids = new List<int> { rootId };
        var frontier = new List<int> { rootId };
        for (var guard = 0; frontier.Count > 0 && guard < 100 && ids.Count < 1000; guard++)
        {
            var current = frontier;
            frontier = await db.Messages.AsNoTracking().Where(m => m.ReplyToId != null && current.Contains(m.ReplyToId.Value))
                .Select(m => m.Id).ToListAsync();
            frontier = frontier.Except(ids).ToList();
            ids.AddRange(frontier);
        }
        return ids;
    }

    private static string Name(string? first, string? last, string username)
    {
        var n = $"{first} {last}".Trim();
        return n.Length > 0 ? n : username;
    }
}
