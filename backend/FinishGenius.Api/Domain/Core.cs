namespace FinishGenius.Api.Domain;

/// <summary>Role names stored in fg.UserRoles. Labels shown in the UI live in <see cref="Roles.Labels"/>.</summary>
public static class Roles
{
    public const string SystemAdmin = "SystemAdmin";
    public const string SupportAgent = "SupportAgent";
    public const string GroupAdmin = "GroupAdmin";
    public const string FGPro = "FGPro";
    public const string FGProPlus = "FGProPlus";

    public static readonly string[] All = [FGPro, FGProPlus, GroupAdmin, SupportAgent, SystemAdmin];

    public static readonly Dictionary<string, string> Labels = new()
    {
        [FGPro] = "Finish Genius Pro",
        [FGProPlus] = "Finish Genius Pro+",
        [GroupAdmin] = "Group Administrator",
        [SupportAgent] = "FG Support Agent",
        [SystemAdmin] = "System Administrator",
    };
}

public abstract class GroupOwned
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
}

public class Group
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string TimeZone { get; set; } = "Eastern Standard Time";
    public Guid? ApiKey { get; set; }
    public string? LogoFile { get; set; }
    public bool ChecklistDeletionEnabled { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class User
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public Group? Group { get; set; }
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool Disabled { get; set; }
    public bool AgreementAccepted { get; set; }
    public DateTime? AgreementAcceptedAt { get; set; }
    /// <summary>The group the user lands in ("Is Default" on the Groups page).</summary>
    public int? DefaultGroupId { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public List<UserRole> Roles { get; set; } = [];
    public List<UserGroup> ExtraGroups { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class UserRole
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Role { get; set; } = "";
}

/// <summary>Additional groups a user may access besides <see cref="User.GroupId"/>.</summary>
public class UserGroup
{
    public int UserId { get; set; }
    public int GroupId { get; set; }
}

public class Department : GroupOwned
{
    public string Name { get; set; } = "";
}

public class AuditLog
{
    public long Id { get; set; }
    public int? GroupId { get; set; }
    public int? UserId { get; set; }
    public string? UserName { get; set; }
    public string EntityType { get; set; } = "";
    public int EntityId { get; set; }
    public string Action { get; set; } = "";
    public string? Details { get; set; }
    /// <summary>Optional before/after values (formula "View History" columns Old Value / New Value).</summary>
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Message
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int FromUserId { get; set; }
    public User? FromUser { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Internal = "Internal DPM" (staff), Support = FG APP Support, Question = Finishing Questions, General = DPM Center.</summary>
    public string MessageType { get; set; } = "General";
    public int? ReplyToId { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public List<MessageRecipient> Recipients { get; set; } = [];
}

public class MessageRecipient
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public bool Viewed { get; set; }
}

public class AppSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}
