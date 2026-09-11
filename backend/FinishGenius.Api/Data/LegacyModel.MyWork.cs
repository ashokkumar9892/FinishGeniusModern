using FinishGenius.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinishGenius.Api.Data;

/// <summary>Dashboard data on the old tables: My Work runs, their defects and adders, and device telemetry.</summary>
public static partial class LegacyModel
{
    private static void MyWorkAndTelemetry(ModelBuilder b)
    {
        b.Entity<WorkExecution>(e =>
        {
            // MyWorkExecution. The group comes from the schedule; a completed run ends at its last checklist entry
            // (same rule as the legacy import).
            e.ToTable("MyWorkExecution", "dbo");
            e.ToSqlQuery("""
                SELECT x.ID, ISNULL(sc.GroupID, 0) AS GroupID, x.FinishingScheduleID, x.UserID, CAST(ISNULL(x.Complete, 0) AS bit) AS Complete,
                       ISNULL(x.[Order], 0) AS [Order], x.Notes, x.EntryDate,
                       CASE WHEN x.Complete = 1 THEN ISNULL(lc.LastDate, x.EntryDate) END AS CompletedAt
                FROM dbo.MyWorkExecution x
                LEFT JOIN dbo.FinishingSchedules sc ON sc.ID = x.FinishingScheduleID
                LEFT JOIN (SELECT p.MyWorkExecutionID, MAX(l.EntryDate) AS LastDate
                           FROM dbo.MyWorkExecutedProcess p JOIN dbo.MyWorkExecutedProcessLine l ON l.MyWorkExecutedProcessID = p.ID
                           WHERE p.MyWorkExecutionID IS NOT NULL GROUP BY p.MyWorkExecutionID) lc ON lc.MyWorkExecutionID = x.ID
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.ScheduleId).HasColumnName("FinishingScheduleID");
            e.Property(x => x.UserId).HasColumnName("UserID");
            // The old table only knows complete / not complete.
            e.Property(x => x.Status).HasColumnName("Complete").HasConversion(new ValueConverter<ExecutionStatus, bool>(
                v => v == ExecutionStatus.Completed, v => v ? ExecutionStatus.Completed : ExecutionStatus.InProgress));
            e.Property(x => x.Ordering).HasColumnName("Order");
            e.Property(x => x.StartedAt).HasColumnName("EntryDate");
            ReadOnly(e.Property(x => x.GroupId));     // the schedule's group
            ReadOnly(e.Property(x => x.CompletedAt)); // the last checklist entry
        });

        b.Entity<DefectType>(e =>
        {
            e.ToTable("MyWorkDefect", "dbo");
            e.ToSqlQuery("SELECT d.ID, d.GroupID, d.Name, d.ChartColor, CAST(0 AS bit) AS IsArchived FROM dbo.MyWorkDefect d");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            ReadOnly(e.Property(x => x.IsArchived)); // the old site has no archive flag
        });

        b.Entity<AdderType>(e =>
        {
            e.ToTable("MyWorkAdder", "dbo");
            e.ToSqlQuery("SELECT a.ID, a.GroupID, a.Name, CAST(0 AS bit) AS IsArchived FROM dbo.MyWorkAdder a");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            ReadOnly(e.Property(x => x.IsArchived));
        });

        // Defects / adders are recorded per executed process in the old site; the run is that process's MyWorkExecution.
        // Recording them from this app comes with the My Work screens.
        b.Entity<WorkExecutionDefect>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, "Recording defects from this app");
            e.ToSqlQuery("""
                SELECT d.ID AS Id, p.MyWorkExecutionID AS ExecutionId, pd.MyWorkDefectID AS DefectTypeId, d.Quantity,
                       CAST(NULL AS nvarchar(400)) AS Notes, d.UserID AS UserId, d.EntryDate AS CreatedAt
                FROM dbo.MyWorkExecutedProcessDefect d
                JOIN dbo.MyWorkExecutedProcess p ON p.ID = d.MyWorkExecutedProcessID
                JOIN dbo.MyWorkProcessDefect pd ON pd.ID = d.MyWorkProcessDefectID
                WHERE p.MyWorkExecutionID IS NOT NULL
                """);
        });

        b.Entity<WorkExecutionAdder>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, "Recording adders from this app");
            e.ToSqlQuery("""
                SELECT a.ID AS Id, p.MyWorkExecutionID AS ExecutionId, pa.MyWorkAdderID AS AdderTypeId, CAST(ISNULL(a.Value, '') AS nvarchar(400)) AS Value,
                       a.UserID AS UserId, a.EntryDate AS CreatedAt
                FROM dbo.MyWorkExecutedProcessAdder a
                JOIN dbo.MyWorkExecutedProcess p ON p.ID = a.MyWorkExecutedProcessID
                JOIN dbo.MyWorkProcessAdder pa ON pa.ID = a.MyWorkProcessAdderID
                WHERE p.MyWorkExecutionID IS NOT NULL
                """);
        });

        b.Entity<DeviceMetric>(e =>
        {
            // ~12M rows, indexed by DateTime but not by device. Reads MUST filter on Timestamp: the IDX_DateTime hint keeps
            // SQL Server from scanning ~127k pages when the day and device are parameters (2 s → 0.05 s). Units come from DataType.
            e.ToTable("DeviceMetrics", "dbo");
            e.ToSqlQuery("""
                SELECT m.ID, m.DeviceID, m.Name, CAST(dt.Name AS nvarchar(400)) AS Unit, m.[DateTime],
                       COALESCE(m.FloatValue, CAST(m.IntValue AS float), CASE WHEN m.BoolValue IS NULL THEN NULL WHEN m.BoolValue = 1 THEN 1.0 ELSE 0.0 END) AS FloatValue,
                       CAST(m.StringValue AS nvarchar(400)) AS StringValue, m.DataTypeID
                FROM dbo.DeviceMetrics m WITH (INDEX(IDX_DateTime)) LEFT JOIN dbo.DataType dt ON dt.ID = m.DataTypeID
                """);
            e.Property(x => x.Id).HasColumnName("ID").HasConversion<int>();
            e.Property(x => x.DeviceId).HasColumnName("DeviceID");
            // datetime (not datetime2) parameters, or SQL Server converts the column and cannot seek the DateTime index.
            e.Property(x => x.Timestamp).HasColumnName("DateTime").HasColumnType("datetime");
            e.Property(x => x.Value).HasColumnName("FloatValue");
            e.Property(x => x.TextValue).HasColumnName("StringValue");
            ReadOnly(e.Property(x => x.Unit)); // DataType name
            e.Property<int>("DataTypeID").HasAnnotation(InsertValue, 25); // DataType 25 = "Number"
        });
    }
}
