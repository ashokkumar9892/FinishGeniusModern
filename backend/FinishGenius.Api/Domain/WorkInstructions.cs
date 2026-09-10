namespace FinishGenius.Api.Domain;

public class WorkInstruction : GroupOwned
{
    public string DocumentNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime IssueDate { get; set; } = DateTime.UtcNow;
    /// <summary>Revision number; status is shown as "DRAFT v{Version}" or "RELEASED v{Version}".</summary>
    public int Version { get; set; } = 1;
    public bool IsReleased { get; set; }
    public bool Controlled { get; set; }
    public string? Location { get; set; }
    public string? Purpose { get; set; }
    public string? Scope { get; set; }
    public string? Terminology { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<WorkInstructionStep> Steps { get; set; } = [];
    public List<WorkInstructionTrail> Trail { get; set; } = [];
    public List<WorkInstructionRelatedDoc> RelatedDocuments { get; set; } = [];
    public List<WorkInstructionSignature> Signatures { get; set; } = [];
    public List<WorkInstructionItem> Items { get; set; } = [];

    public string StatusText => $"{(IsReleased ? "RELEASED" : "DRAFT")} v{Version}";
}

public class WorkInstructionStep
{
    public int Id { get; set; }
    public int WorkInstructionId { get; set; }
    public int Level { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public List<WorkInstructionMedia> Media { get; set; } = [];
}

public class WorkInstructionMedia
{
    public int Id { get; set; }
    public int StepId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredFile { get; set; } = "";
    public string ContentType { get; set; } = "";
}

public class WorkInstructionTrail
{
    public int Id { get; set; }
    public int WorkInstructionId { get; set; }
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string? Log { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
}

public class WorkInstructionRelatedDoc
{
    public int Id { get; set; }
    public int WorkInstructionId { get; set; }
    public string DocumentNumber { get; set; } = "";
    public string DocumentName { get; set; } = "";
    public string? Author { get; set; }
    public int? DocumentId { get; set; }
}

public class WorkInstructionSignature
{
    public int Id { get; set; }
    public int WorkInstructionId { get; set; }
    public string Name { get; set; } = "";
    public string? Position { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
}

/// <summary>"Approved Tools &amp; Equipment" (Kind = Equipment) and "Materials" (Kind = Material) on page 2.</summary>
public class WorkInstructionItem
{
    public int Id { get; set; }
    public int WorkInstructionId { get; set; }
    public string Kind { get; set; } = "Equipment";
    public string Description { get; set; } = "";
    public int? MaterialId { get; set; }
}
