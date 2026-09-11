namespace FinishGenius.Api.Domain;

/// <summary>Unit of a formula's batch size. Values match the legacy BatchTypeEnum.</summary>
public enum FormulaBatchType
{
    Gallons = 1,
    Grams = 2,
    Litres = 3,
    Kilograms = 4,
    Quarts = 5,
}

/// <summary>A user's last used scale / label printer for one formula (legacy FormulationScales + FormulationPrinters).</summary>
public class FormulaDevicePreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int FormulaId { get; set; }
    public int? ScaleDeviceId { get; set; }
    public int? PrinterDeviceId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Ingredient state before the last successful machine dispense, for "Undo Dispense" (legacy DispenseFormulationMaterials).</summary>
public class FormulaDispenseSnapshot
{
    public int Id { get; set; }
    public int FormulaId { get; set; }
    public int IngredientId { get; set; }
    public decimal Grams { get; set; }
    public decimal DispenseAmount { get; set; }
    public decimal DispensedGrams { get; set; }
    public bool IsDispensed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Per-group dispense settings: "Clean Nozzle" interval (legacy GroupDispenseTimeSpanMapping).</summary>
public class DispenseSetting
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    /// <summary>Hours after the last dispense before the nozzle must be cleaned; null/0 = never.</summary>
    public int? CleanNozzleHours { get; set; }
    /// <summary>Set by "Confirm" in the nozzle-cleaning popup; cleared by the next successful dispense.</summary>
    public bool IsNozzleCleaned { get; set; }
    public DateTime? LastDispensedAt { get; set; }
}

/// <summary>Automatic purge window of a Network Bridge (legacy PurgeSettings, keyed by the bridge's API key).</summary>
public class PurgeSetting
{
    public int Id { get; set; }
    public int BridgeDeviceId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public TimeOnly Time { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A canister purge that failed (legacy PurgeFailed). Active failures block dispensing until cleared.</summary>
public class PurgeFailure
{
    public int Id { get; set; }
    public int BridgeDeviceId { get; set; }
    public int CanisterNumber { get; set; }
    public string? Message { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A canister purge that succeeded (legacy PurgeSucess). PurgeType is "Auto" (scheduled) or "Manual".</summary>
public class PurgeSuccess
{
    public int Id { get; set; }
    public int BridgeDeviceId { get; set; }
    public int CanisterNumber { get; set; }
    public string? Message { get; set; }
    public string? PurgeType { get; set; }
    public DateTime ExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class DeviceCommandTypes
{
    public const string Dispense = "Dispense";
    public const string Purge = "Purge";
    public const string GetWeight = "GetWeight";
    public const string Tare = "Tare";
    public const string PrintLabel = "PrintLabel";
}

public static class DeviceCommandStatuses
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public const string Expired = "Expired";
    public static bool IsOpen(string s) => s is Pending or Sent;
}

/// <summary>
/// Hardware integration point: a job for a shop-floor device (dispense, purge, read/tare scale, print label) that the
/// device's Network Bridge picks up with <c>GET /api/bridge/commands</c> (X-Api-Key = bridge key) and answers with
/// <c>POST /api/bridge/commands/{id}/result</c>. Replaces the legacy SignalR/UDCP calls.
/// </summary>
public class DeviceCommand : GroupOwned
{
    /// <summary>The device the command is for (dispense machine, scale, label printer or the bridge itself for purges).</summary>
    public int DeviceId { get; set; }
    /// <summary>The Network Bridge that relays the command.</summary>
    public int BridgeDeviceId { get; set; }
    public string CommandType { get; set; } = "";
    /// <summary>JSON payload for the bridge.</summary>
    public string? Payload { get; set; }
    public string Status { get; set; } = DeviceCommandStatuses.Pending;
    public string? ResultMessage { get; set; }
    /// <summary>JSON/text returned by the bridge (e.g. the weight read from a scale).</summary>
    public string? ResultData { get; set; }
    public int? FormulaId { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>Open commands older than this are reported as expired (no bridge picked them up / answered).</summary>
    public DateTime ExpiresAt { get; set; }
}
