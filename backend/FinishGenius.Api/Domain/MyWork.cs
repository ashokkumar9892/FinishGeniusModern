namespace FinishGenius.Api.Domain;

public enum ExecutionStatus
{
    InProgress = 0,
    Completed = 1,
    Cancelled = 2,
}

/// <summary>One run of a process schedule started from My Work ("Start Process").</summary>
public class WorkExecution : GroupOwned
{
    public int ScheduleId { get; set; }
    public ProcessSchedule? Schedule { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public ExecutionStatus Status { get; set; }
    public int Ordering { get; set; }
    public string? Notes { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<WorkExecutionLine> Lines { get; set; } = [];
}

/// <summary>Snapshot of a checklist line taken when the execution starts, so later schedule edits don't rewrite history.</summary>
public class WorkExecutionLine
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public int StepNumber { get; set; }
    public string StepName { get; set; } = "";
    public int Sequence { get; set; }
    public string Description { get; set; } = "";
    public string? Value { get; set; }
    public string? Unit { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public List<WorkLineCheck> Checks { get; set; } = [];
}

public class WorkLineCheck
{
    public int Id { get; set; }
    public int LineId { get; set; }
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string? RecordedValue { get; set; }
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

public class DefectType : GroupOwned
{
    public string Name { get; set; } = "";
    public string? ChartColor { get; set; }
    public bool IsArchived { get; set; }
}

public class AdderType : GroupOwned
{
    public string Name { get; set; } = "";
    public bool IsArchived { get; set; }
}

public class WorkExecutionDefect
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public int DefectTypeId { get; set; }
    public DefectType? DefectType { get; set; }
    public int Quantity { get; set; }
    public string? Notes { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class WorkExecutionAdder
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public int AdderTypeId { get; set; }
    public AdderType? AdderType { get; set; }
    public string Value { get; set; } = "";
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
