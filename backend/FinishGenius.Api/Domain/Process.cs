namespace FinishGenius.Api.Domain;

/// <summary>Aerospace, Automotive, Construction, Food, Heavy Manufacturing, Marine, Wood.</summary>
public class IndustrySector
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class SubStep
{
    public int Id { get; set; }
    public int IndustrySectorId { get; set; }
    public IndustrySector? IndustrySector { get; set; }
    /// <summary>"User" sub steps are edited in the step builder; "Admin" ones are part of every step but not edited there.</summary>
    public string UserRole { get; set; } = "User";
    public string Name { get; set; } = "";
    public string ShortName { get; set; } = "";
    public int Sequence { get; set; }
    public int PassThroughs { get; set; } = 1;
    public string? WebLink { get; set; }
    public string? Instruction { get; set; }
    public List<SubStepPullDown> PullDowns { get; set; } = [];
}

/// <summary>
/// A dropdown question inside a sub step. Replaces the legacy free-SQL "Query" with a safe structured
/// source: categories of <see cref="MaterialType"/> whose Filter1/Filter2 match (empty = any).
/// </summary>
public class SubStepPullDown
{
    public int Id { get; set; }
    public int SubStepId { get; set; }
    public int Sequence { get; set; }
    public string Header { get; set; } = "";
    public string? ChoiceName { get; set; }
    public MaterialType? MaterialType { get; set; }
    public string? CategoryFilter1 { get; set; }
    public string? CategoryFilter2 { get; set; }
}

public class ProcessStep : GroupOwned
{
    public string Name { get; set; } = "";
    public int IndustrySectorId { get; set; }
    public IndustrySector? IndustrySector { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public List<ProcessStepEntry> Entries { get; set; } = [];
}

/// <summary>The category chosen for one pull down in one pass of a sub step of a process step.</summary>
public class ProcessStepEntry
{
    public int Id { get; set; }
    public int ProcessStepId { get; set; }
    public int SubStepId { get; set; }
    public SubStep? SubStep { get; set; }
    public int Pass { get; set; } = 1;
    public int? PullDownId { get; set; }
    public int? CategoryId { get; set; }
    public MaterialCategory? Category { get; set; }
    public List<ProcessStepValue> Values { get; set; } = [];
}

public class ProcessStepValue
{
    public int Id { get; set; }
    public int EntryId { get; set; }
    public int CharacteristicId { get; set; }
    public Characteristic? Characteristic { get; set; }
    public string? Value { get; set; }
    public int? MaterialId { get; set; }
    public Material? Material { get; set; }
}

public class ProcessSchedule : GroupOwned
{
    public string Name { get; set; } = "";
    public string Number { get; set; } = "";
    public string? CustomerName { get; set; }
    public int? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Saved Material Quantities estimate
    public decimal OneSidedArea { get; set; }
    public decimal TwoSidedArea { get; set; }

    // Saved Pricing scenario
    public decimal LaborRate { get; set; }
    public decimal MarkUp { get; set; }
    public decimal PremiumMarkUp { get; set; }
    public decimal OneSidedComplexity { get; set; }
    public decimal OneSidedPriceArea { get; set; }
    public decimal TwoSidedComplexity { get; set; }
    public decimal TwoSidedPriceArea { get; set; }
    public decimal HighComplexity { get; set; }
    public decimal HighComplexityArea { get; set; }
    public decimal TotalJobPrice { get; set; }

    public List<ProcessScheduleStep> Steps { get; set; } = [];
}

public class ProcessScheduleStep
{
    public int Id { get; set; }
    public int ScheduleId { get; set; }
    public int ProcessStepId { get; set; }
    public ProcessStep? ProcessStep { get; set; }
    public int Ordering { get; set; }
    /// <summary>Schedule-level rename of the associated step ("Edit Step").</summary>
    public string? NameOverride { get; set; }
    public List<ScheduleStepOverride> Overrides { get; set; } = [];
}

/// <summary>Schedule-level edits to an associated step's values and allowed input ranges.
/// "Copy Master" keeps these; "Clone" drops them.</summary>
public class ScheduleStepOverride
{
    public int Id { get; set; }
    public int ScheduleStepId { get; set; }
    public int ProcessStepValueId { get; set; }
    public string? Value { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
}
