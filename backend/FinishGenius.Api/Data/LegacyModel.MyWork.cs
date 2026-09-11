using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinishGenius.Api.Data;

/// <summary>
/// My Work on the old tables. A run (MyWorkExecution) holds one executed process per My Work process of its schedule's
/// steps (MyWorkExecutedProcess); the checklist lines are the lines of those processes (MyWorkProcessLine) and a check is
/// a MyWorkExecutedProcessLine row. Defects and adders are recorded per executed process. Plus device telemetry.
/// </summary>
public static partial class LegacyModel
{
    /// <summary>
    /// A checklist line has no row of its own: its id is executedProcessId * LineSlots + the line's position in its My Work
    /// process (processes have at most 94 lines on Prod).
    /// </summary>
    private const int LineSlots = 1000;

    /// <summary>My Work lines with their position in the process (the second half of a checklist line id).</summary>
    private const string RankedLines =
        "(SELECT l.ID, l.MyWorkProcessID, l.Sequence, l.Description, l.isArchived, ROW_NUMBER() OVER (PARTITION BY l.MyWorkProcessID ORDER BY l.ID) AS rn FROM dbo.MyWorkProcessLine l)";

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
            // The old table only knows complete / not complete; a cancelled run is deleted (MyWorkController).
            e.Property(x => x.Status).HasColumnName("Complete").HasConversion(new ValueConverter<ExecutionStatus, bool>(
                v => v == ExecutionStatus.Completed, v => v ? ExecutionStatus.Completed : ExecutionStatus.InProgress));
            e.Property(x => x.Ordering).HasColumnName("Order");
            e.Property(x => x.StartedAt).HasColumnName("EntryDate");
            ReadOnly(e.Property(x => x.GroupId));     // the schedule's group
            ReadOnly(e.Property(x => x.CompletedAt)); // the last checklist entry
        });

        b.Entity<WorkExecutionLine>(e =>
        {
            // Every line of every executed process (like the old site: by process, not by process version), archived lines
            // only where they were checked. Unit and allowed range come from the line's first input (My Work metric); the
            // old site ignores a range unless max > 0 and min >= 0.
            e.ToTable((string?)null);
            e.ToSqlQuery($"""
                SELECT CAST(p.ID * {LineSlots} + pl.rn AS int) AS Id, p.MyWorkExecutionID AS ExecutionId,
                       CAST(DENSE_RANK() OVER (PARTITION BY p.MyWorkExecutionID ORDER BY p.ID) AS int) AS StepNumber,
                       CAST(ISNULL(mp.Name, '') AS nvarchar(4000)) AS StepName, pl.Sequence, CAST(ISNULL(pl.Description, '') AS nvarchar(4000)) AS Description,
                       CAST(NULL AS nvarchar(4000)) AS Value, CAST(mt.Unit AS nvarchar(400)) AS Unit, mt.MinValue, mt.MaxValue,
                       p.ID AS ExecutedProcessID, pl.ID AS ProcessLineID
                FROM dbo.MyWorkExecutedProcess p
                JOIN dbo.MyWorkProcess mp ON mp.ID = p.MyWorkProcessID
                JOIN {RankedLines} pl ON pl.MyWorkProcessID = p.MyWorkProcessID
                OUTER APPLY (SELECT TOP 1 dt.Name AS Unit,
                                    CASE WHEN m.maxVal > 0 AND m.minVal >= 0 THEN CAST(m.minVal AS decimal(18, 4)) END AS MinValue,
                                    CASE WHEN m.maxVal > 0 AND m.minVal >= 0 THEN CAST(m.maxVal AS decimal(18, 4)) END AS MaxValue
                             FROM dbo.MyWorkProcessLineMetric lm JOIN dbo.MyWorkMetric m ON m.ID = lm.MyWorkMetricID LEFT JOIN dbo.DataType dt ON dt.ID = m.DataTypeID
                             WHERE lm.MyWorkProcessLineID = pl.ID ORDER BY lm.ID) mt
                WHERE p.MyWorkExecutionID IS NOT NULL
                  AND (ISNULL(pl.isArchived, 0) = 0
                       OR EXISTS (SELECT 1 FROM dbo.MyWorkExecutedProcessLine el WHERE el.MyWorkExecutedProcessID = p.ID AND el.MyWorkProcessLineID = pl.ID))
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property<int>("ExecutedProcessID");
            e.Property<int>("ProcessLineID");
        });

        b.Entity<WorkLineCheck>(e =>
        {
            // MyWorkExecutedProcessLine; the recorded value is the entry's input (MyWorkExecutedProcessLineMetric).
            e.ToTable("MyWorkExecutedProcessLine", "dbo");
            e.ToSqlQuery($"""
                SELECT el.ID, CAST(el.MyWorkExecutedProcessID * {LineSlots} + pl.rn AS int) AS LineId, el.UserID, CAST(u.Username AS nvarchar(400)) AS UserName,
                       CAST(mv.Recorded AS nvarchar(400)) AS RecordedValue, el.EntryDate, el.MyWorkExecutedProcessID, el.MyWorkProcessLineID
                FROM dbo.MyWorkExecutedProcessLine el
                JOIN {RankedLines} pl ON pl.ID = el.MyWorkProcessLineID
                LEFT JOIN dbo.[User] u ON u.ID = el.UserID
                OUTER APPLY (SELECT {RecordedValueSql} AS Recorded
                             FROM dbo.MyWorkExecutedProcessLineMetric x WHERE x.MyWorkExecutedProcessLineID = el.ID) mv
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.UserId).HasColumnName("UserID");
            e.Property(x => x.CheckedAt).HasColumnName("EntryDate");
            ReadOnly(e.Property(x => x.LineId));        // encoded; saved as the two columns below
            ReadOnly(e.Property(x => x.UserName));
            ReadOnly(e.Property(x => x.RecordedValue)); // saved as an input row after the insert
            e.Property<int>("MyWorkExecutedProcessID");
            e.Property<int>("MyWorkProcessLineID");
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
        // New ones go to the run's first process that has the type set up (MyWorkProcessDefect / MyWorkProcessAdder).
        b.Entity<WorkExecutionDefect>(e =>
        {
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT d.ID AS Id, p.MyWorkExecutionID AS ExecutionId, pd.MyWorkDefectID AS DefectTypeId, d.Quantity,
                       CAST(NULL AS nvarchar(400)) AS Notes, d.UserID AS UserId, d.EntryDate AS CreatedAt
                FROM dbo.MyWorkExecutedProcessDefect d
                JOIN dbo.MyWorkExecutedProcess p ON p.ID = d.MyWorkExecutedProcessID
                JOIN dbo.MyWorkProcessDefect pd ON pd.ID = d.MyWorkProcessDefectID
                WHERE p.MyWorkExecutionID IS NOT NULL
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        b.Entity<WorkExecutionAdder>(e =>
        {
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT a.ID AS Id, p.MyWorkExecutionID AS ExecutionId, pa.MyWorkAdderID AS AdderTypeId, CAST(ISNULL(a.Value, '') AS nvarchar(400)) AS Value,
                       a.UserID AS UserId, a.EntryDate AS CreatedAt
                FROM dbo.MyWorkExecutedProcessAdder a
                JOIN dbo.MyWorkExecutedProcess p ON p.ID = a.MyWorkExecutedProcessID
                JOIN dbo.MyWorkProcessAdder pa ON pa.ID = a.MyWorkProcessAdderID
                WHERE p.MyWorkExecutionID IS NOT NULL
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
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

    // ------------------------------------------------------------------ My Work helpers for controllers

    private const string RunProcessesSql = """
        FROM dbo.FinishingSchedulesSteps st
        JOIN dbo.MyWorkProcess p ON p.FinishingStepID = st.FinishingStepID AND p.Status = 1
        CROSS APPLY (SELECT MAX(v.ID) AS VersionID FROM dbo.MyWorkProcessVersion v WHERE v.MyWorkProcessID = p.ID) v
        WHERE st.FinishingScheduleID = @s AND v.VersionID IS NOT NULL
        """;

    /// <summary>Active My Work processes a run of the schedule would get (the old site's "Start Process").</summary>
    public static async Task<int> MyWorkProcessCountAsync(DbContext db, int scheduleId) =>
        (await db.Database.SqlQueryRaw<int>($"SELECT COUNT(*) AS Value {RunProcessesSql}", new SqlParameter("@s", scheduleId)).ToListAsync()).First();

    /// <summary>The schedule's "delete on submit" flag (FinishingSchedules.DeletionEnabled).</summary>
    public static async Task<bool> ScheduleDeletionEnabledAsync(DbContext db, int scheduleId) =>
        (await db.Database.SqlQueryRaw<bool>("SELECT CAST(ISNULL(DeletionEnabled, 0) AS bit) AS Value FROM dbo.FinishingSchedules WHERE ID = @s",
            new SqlParameter("@s", scheduleId)).ToListAsync()).FirstOrDefault();

    // ------------------------------------------------------------------ run screens
    // The mapped line / check queries join on the encoded line id, which SQL Server can only compute for every row of the
    // unindexed tables: seconds per screen on Prod, and a join of the two can exhaust Prod's small tempdb. The run screens
    // (MyWorkController) use these queries instead: they filter by the run before ranking or joining anything.

    // Each is a small batch: the run's executed processes, their lines, checks and inputs go into table variables first
    // (tens to hundreds of rows), so the final join never touches the big tables (one combined query took 1.6 s on Dev).
    // OPTION (RECOMPILE): SQL Server 2017 assumes a table variable holds one row and would scan the big table once per row.

    private const string RecordedValueSql = """
        MAX(COALESCE(x.StringValue, CAST(CAST(x.DecimalValue AS float) AS nvarchar(40)), CAST(x.IntValue AS nvarchar(20)),
                     CASE WHEN x.BoolValue = 1 THEN N'Yes' WHEN x.BoolValue = 0 THEN N'No' END))
        """;

    private static readonly string RunLinesSql = $"""
        SET NOCOUNT ON;
        DECLARE @p TABLE (ID int PRIMARY KEY, ProcessID int, StepNumber int);
        INSERT @p SELECT e.ID, e.MyWorkProcessID, DENSE_RANK() OVER (ORDER BY e.ID) FROM dbo.MyWorkExecutedProcess e WHERE e.MyWorkExecutionID = @x;
        DECLARE @l TABLE (ID int PRIMARY KEY, ProcessID int, Sequence int, Description nvarchar(max), Archived bit, rn int);
        INSERT @l SELECT l.ID, l.MyWorkProcessID, l.Sequence, l.Description, ISNULL(l.isArchived, 0), ROW_NUMBER() OVER (PARTITION BY l.MyWorkProcessID ORDER BY l.ID)
            FROM dbo.MyWorkProcessLine l WHERE l.MyWorkProcessID IN (SELECT ProcessID FROM @p) OPTION (RECOMPILE);
        DECLARE @c TABLE (P int, L int, PRIMARY KEY (P, L));
        INSERT @c SELECT DISTINCT c.MyWorkExecutedProcessID, c.MyWorkProcessLineID FROM dbo.MyWorkExecutedProcessLine c WHERE c.MyWorkExecutedProcessID IN (SELECT ID FROM @p) OPTION (RECOMPILE);
        DECLARE @m TABLE (L int PRIMARY KEY, Unit nvarchar(400), MinV float, MaxV float);
        INSERT @m SELECT x.MyWorkProcessLineID, CAST(x.Unit AS nvarchar(400)), x.minVal, x.maxVal
            FROM (SELECT lm.MyWorkProcessLineID, dt.Name AS Unit, m.minVal, m.maxVal, ROW_NUMBER() OVER (PARTITION BY lm.MyWorkProcessLineID ORDER BY lm.ID) AS r
                  FROM dbo.MyWorkProcessLineMetric lm JOIN dbo.MyWorkMetric m ON m.ID = lm.MyWorkMetricID LEFT JOIN dbo.DataType dt ON dt.ID = m.DataTypeID
                  WHERE lm.MyWorkProcessLineID IN (SELECT ID FROM @l)) x
            WHERE x.r = 1 OPTION (RECOMPILE);
        SELECT CAST(p.ID * {LineSlots} + l.rn AS int) AS Id, @x AS ExecutionId, p.StepNumber,
               CAST(ISNULL(mp.Name, '') AS nvarchar(4000)) AS StepName, l.Sequence, CAST(ISNULL(l.Description, '') AS nvarchar(4000)) AS Description,
               CAST(NULL AS nvarchar(4000)) AS Value, mt.Unit,
               CASE WHEN mt.MaxV > 0 AND mt.MinV >= 0 THEN CAST(mt.MinV AS decimal(18, 4)) END AS MinValue,
               CASE WHEN mt.MaxV > 0 AND mt.MinV >= 0 THEN CAST(mt.MaxV AS decimal(18, 4)) END AS MaxValue,
               p.ID AS ExecutedProcessID, l.ID AS ProcessLineID
        FROM @p p
        JOIN dbo.MyWorkProcess mp ON mp.ID = p.ProcessID
        JOIN @l l ON l.ProcessID = p.ProcessID
        LEFT JOIN @c c ON c.P = p.ID AND c.L = l.ID
        LEFT JOIN @m mt ON mt.L = l.ID
        WHERE l.Archived = 0 OR c.L IS NOT NULL OPTION (RECOMPILE);
        """;

    private static readonly string RunChecksSql = $"""
        SET NOCOUNT ON;
        DECLARE @p TABLE (ID int PRIMARY KEY, ProcessID int);
        INSERT @p SELECT e.ID, e.MyWorkProcessID FROM dbo.MyWorkExecutedProcess e WHERE e.MyWorkExecutionID = @x;
        DECLARE @k TABLE (ID int PRIMARY KEY, P int, L int, UserID int, EntryDate datetime);
        INSERT @k SELECT c.ID, c.MyWorkExecutedProcessID, c.MyWorkProcessLineID, c.UserID, c.EntryDate
            FROM dbo.MyWorkExecutedProcessLine c WHERE c.MyWorkExecutedProcessID IN (SELECT ID FROM @p) OPTION (RECOMPILE);
        DECLARE @l TABLE (ID int PRIMARY KEY, rn int);
        INSERT @l SELECT l.ID, ROW_NUMBER() OVER (PARTITION BY l.MyWorkProcessID ORDER BY l.ID)
            FROM dbo.MyWorkProcessLine l WHERE l.MyWorkProcessID IN (SELECT ProcessID FROM @p) OPTION (RECOMPILE);
        DECLARE @m TABLE (K int PRIMARY KEY, Recorded nvarchar(400));
        INSERT @m SELECT x.MyWorkExecutedProcessLineID, LEFT({RecordedValueSql}, 400)
            FROM dbo.MyWorkExecutedProcessLineMetric x WHERE x.MyWorkExecutedProcessLineID IN (SELECT ID FROM @k) GROUP BY x.MyWorkExecutedProcessLineID OPTION (RECOMPILE);
        SELECT k.ID, CAST(k.P * {LineSlots} + l.rn AS int) AS LineId, k.UserID, CAST(u.Username AS nvarchar(400)) AS UserName, m.Recorded AS RecordedValue,
               k.EntryDate, k.P AS MyWorkExecutedProcessID, k.L AS MyWorkProcessLineID
        FROM @k k JOIN @l l ON l.ID = k.L LEFT JOIN dbo.[User] u ON u.ID = k.UserID LEFT JOIN @m m ON m.K = k.ID OPTION (RECOMPILE);
        """;

    /// <summary>
    /// Per run: checklist lines, checked lines, last checklist entry, defects and adders. <paramref name="runs"/> is a
    /// SELECT of run ids. The mapped WorkExecution.CompletedAt and the defect / adder counts cost ~45 s and ~5 s for a
    /// 600-run list on Prod (SQL Server re-evaluates them per run); this batch does it once for all listed runs.
    /// </summary>
    private static string RunSummarySql(string runs) => $"""
        SET NOCOUNT ON;
        DECLARE @r TABLE (ID int PRIMARY KEY);
        INSERT @r {runs};
        DECLARE @p TABLE (ID int PRIMARY KEY, X int, ProcessID int);
        INSERT @p SELECT e.ID, e.MyWorkExecutionID, e.MyWorkProcessID FROM dbo.MyWorkExecutedProcess e WHERE e.MyWorkExecutionID IN (SELECT ID FROM @r) OPTION (RECOMPILE);
        DECLARE @l TABLE (ID int PRIMARY KEY, ProcessID int, Archived bit, INDEX IX_P (ProcessID));
        INSERT @l SELECT l.ID, l.MyWorkProcessID, ISNULL(l.isArchived, 0) FROM dbo.MyWorkProcessLine l WHERE l.MyWorkProcessID IN (SELECT ProcessID FROM @p) OPTION (RECOMPILE);
        DECLARE @c TABLE (P int, L int, Last datetime, PRIMARY KEY (P, L));
        INSERT @c SELECT c.MyWorkExecutedProcessID, c.MyWorkProcessLineID, MAX(c.EntryDate) FROM dbo.MyWorkExecutedProcessLine c
            WHERE c.MyWorkExecutedProcessID IN (SELECT ID FROM @p) GROUP BY c.MyWorkExecutedProcessID, c.MyWorkProcessLineID OPTION (RECOMPILE);
        SELECT r.ID AS ExecutionId, ISNULL(t.Total, 0) AS Total, ISNULL(t.Checked, 0) AS Checked, t.LastDate, ISNULL(d.N, 0) AS Defects, ISNULL(a.N, 0) AS Adders
        FROM @r r
        LEFT JOIN (SELECT p.X, COUNT(*) AS Total, COUNT(c.L) AS Checked, MAX(c.Last) AS LastDate
                   FROM @p p JOIN @l l ON l.ProcessID = p.ProcessID LEFT JOIN @c c ON c.P = p.ID AND c.L = l.ID
                   WHERE l.Archived = 0 OR c.L IS NOT NULL GROUP BY p.X) t ON t.X = r.ID
        LEFT JOIN (SELECT p.X, COUNT(*) AS N FROM dbo.MyWorkExecutedProcessDefect x JOIN @p p ON p.ID = x.MyWorkExecutedProcessID GROUP BY p.X) d ON d.X = r.ID
        LEFT JOIN (SELECT p.X, COUNT(*) AS N FROM dbo.MyWorkExecutedProcessAdder x JOIN @p p ON p.ID = x.MyWorkExecutedProcessID GROUP BY p.X) a ON a.X = r.ID OPTION (RECOMPILE);
        """;

    public sealed class RunSummary
    {
        public int ExecutionId { get; set; }
        public int Total { get; set; }
        public int Checked { get; set; }
        public DateTime? LastDate { get; set; }
        public int Defects { get; set; }
        public int Adders { get; set; }
    }

    /// <summary>The run's checklist lines (no checks loaded).</summary>
    public static Task<List<WorkExecutionLine>> RunLinesAsync(DbContext db, int executionId) =>
        db.Set<WorkExecutionLine>().FromSqlRaw(RunLinesSql, P("@x", executionId)).AsNoTracking().ToListAsync();

    /// <summary>The checks of the run's lines.</summary>
    public static Task<List<WorkLineCheck>> RunChecksAsync(DbContext db, int executionId) =>
        db.Set<WorkLineCheck>().FromSqlRaw(RunChecksSql, P("@x", executionId)).AsNoTracking().ToListAsync();

    /// <summary>One checklist line by id (its run is decoded from the id).</summary>
    public static async Task<WorkExecutionLine?> RunLineAsync(DbContext db, int lineId)
    {
        var run = (await db.Database.SqlQueryRaw<int?>("SELECT MyWorkExecutionID AS Value FROM dbo.MyWorkExecutedProcess WHERE ID = @p",
            P("@p", lineId / LineSlots)).ToListAsync()).FirstOrDefault();
        return run == null ? null : (await RunLinesAsync(db, run.Value)).FirstOrDefault(l => l.Id == lineId);
    }

    /// <summary>Summaries of the runs MyWorkController lists: the group's first 2000 runs in list order, optionally by status.</summary>
    public static async Task<Dictionary<int, RunSummary>> RunSummariesAsync(DbContext db, int groupId, ExecutionStatus? status)
    {
        const string runs = """
            SELECT TOP 2000 x.ID FROM dbo.MyWorkExecution x JOIN dbo.FinishingSchedules sc ON sc.ID = x.FinishingScheduleID
            WHERE sc.GroupID = @g AND (@complete < 0 OR CAST(ISNULL(x.Complete, 0) AS int) = @complete) ORDER BY ISNULL(x.[Order], 0), x.ID
            """;
        var complete = status == null ? -1 : status == ExecutionStatus.Completed ? 1 : 0;
        var rows = await db.Database.SqlQueryRaw<RunSummary>(RunSummarySql(runs), P("@g", groupId), P("@complete", complete)).ToListAsync();
        return rows.ToDictionary(r => r.ExecutionId);
    }

    public static async Task<RunSummary> RunSummaryAsync(DbContext db, int executionId) =>
        (await db.Database.SqlQueryRaw<RunSummary>(RunSummarySql("SELECT @x"), P("@x", executionId)).ToListAsync()).FirstOrDefault()
        ?? new RunSummary { ExecutionId = executionId };

    // ------------------------------------------------------------------ save hooks

    private sealed class ProcessLink
    {
        public int ExecutedProcessId { get; set; }
        public int LinkId { get; set; }
    }

    /// <summary>First executed process of the run that has the defect / adder type set up, with that set-up row.</summary>
    private static ProcessLink? FindProcessLink(DbContext db, string table, string typeColumn, int executionId, int typeId) =>
        db.Database.SqlQueryRaw<ProcessLink>($"""
            SELECT TOP 1 p.ID AS ExecutedProcessId, x.ID AS LinkId
            FROM dbo.MyWorkExecutedProcess p JOIN dbo.{table} x ON x.MyWorkProcessID = p.MyWorkProcessID AND x.{typeColumn} = @t
            WHERE p.MyWorkExecutionID = @x ORDER BY p.ID
            """, new SqlParameter("@t", typeId), new SqlParameter("@x", executionId)).AsEnumerable().FirstOrDefault();

    private static bool VirtualShopFloor(DbContext db, EntityEntry entry, int userId, string label, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case WorkExecutionLine line when entry.State == EntityState.Added:
                // The checklist comes from the My Work processes, created with the run (see RelatedShopFloor).
                foreach (var x in db.ChangeTracker.Entries<WorkExecution>()) x.Entity.Lines.Remove(line);
                break;
            case WorkExecutionLine:
                throw ApiException.Bad("Checklist lines are set up on the My Work process.");

            case WorkExecutionDefect d when entry.State == EntityState.Added:
                if (!string.IsNullOrWhiteSpace(d.Notes))
                    throw new ApiException(StatusCodes.Status409Conflict, $"Defect notes aren't available on the {label} database yet.");
                var dl = FindProcessLink(db, "MyWorkProcessDefect", "MyWorkDefectID", d.ExecutionId, d.DefectTypeId)
                         ?? throw ApiException.Bad("This defect type isn't set up on any My Work process of this run.");
                var q = d.Quantity;
                side.Add(new SideWrite(true, () => (
                    "INSERT INTO dbo.MyWorkExecutedProcessDefect (MyWorkExecutedProcessID, MyWorkProcessDefectID, Quantity, UserID, EntryDate) VALUES (@p, @l, @q, @u, GETDATE())",
                    [P("@p", dl.ExecutedProcessId), P("@l", dl.LinkId), P("@q", q), P("@u", userId)])));
                break;
            case WorkExecutionDefect when entry.State == EntityState.Deleted:
                var defectId = (int)entry.Property(nameof(WorkExecutionDefect.Id)).OriginalValue!;
                side.Add(new SideWrite(false, () => ("DELETE FROM dbo.MyWorkExecutedProcessDefect WHERE ID = @id", [P("@id", defectId)])));
                break;
            case WorkExecutionDefect:
                throw ApiException.Bad("A recorded defect can't be changed; remove it and record it again.");

            case WorkExecutionAdder a when entry.State == EntityState.Added:
                var al = FindProcessLink(db, "MyWorkProcessAdder", "MyWorkAdderID", a.ExecutionId, a.AdderTypeId)
                         ?? throw ApiException.Bad("This adder type isn't set up on any My Work process of this run.");
                var value = a.Value;
                side.Add(new SideWrite(true, () => (
                    "INSERT INTO dbo.MyWorkExecutedProcessAdder (MyWorkExecutedProcessID, MyWorkProcessAdderID, Value, UserID, EntryDate) VALUES (@p, @l, @v, @u, GETDATE())",
                    [P("@p", al.ExecutedProcessId), P("@l", al.LinkId), P("@v", value), P("@u", userId)])));
                break;
            case WorkExecutionAdder when entry.State == EntityState.Deleted:
                var adderId = (int)entry.Property(nameof(WorkExecutionAdder.Id)).OriginalValue!;
                side.Add(new SideWrite(false, () => ("DELETE FROM dbo.MyWorkExecutedProcessAdder WHERE ID = @id", [P("@id", adderId)])));
                break;
            case WorkExecutionAdder:
                throw ApiException.Bad("A recorded adder can't be changed; remove it and record it again.");

            default:
                return VirtualWorkInstructions(db, entry, userId, label, side);
        }
        entry.State = EntityState.Detached;
        return true;
    }

    private static void FillShopFloor(DbContext db, EntityEntry entry, int userId)
    {
        switch (entry.Entity)
        {
            case WorkLineCheck c when entry.State == EntityState.Added:
                // The line is normally tracked (MyWorkController loads it); otherwise decode its id.
                var line = db.ChangeTracker.Entries<WorkExecutionLine>().FirstOrDefault(l => l.Entity.Id == c.LineId);
                var executedProcess = c.LineId / LineSlots;
                var processLine = line != null
                    ? (int)line.Property("ProcessLineID").CurrentValue!
                    : db.Database.SqlQueryRaw<int>($"""
                          SELECT pl.ID AS Value FROM {RankedLines} pl
                          WHERE pl.MyWorkProcessID = (SELECT MyWorkProcessID FROM dbo.MyWorkExecutedProcess WHERE ID = @p) AND pl.rn = @r
                          """, new SqlParameter("@p", executedProcess), new SqlParameter("@r", c.LineId % LineSlots)).AsEnumerable().FirstOrDefault();
                if (processLine == 0) throw ApiException.NotFound("Checklist line");
                entry.Property("MyWorkExecutedProcessID").CurrentValue = executedProcess;
                entry.Property("MyWorkProcessLineID").CurrentValue = processLine;
                if (c.RecordedValue != null && !db.Database.SqlQueryRaw<int>(
                        "SELECT COUNT(*) AS Value FROM dbo.MyWorkProcessLineMetric WHERE MyWorkProcessLineID = @l", new SqlParameter("@l", processLine)).AsEnumerable().Any(n => n > 0))
                    throw ApiException.Bad("This checklist line has no input on the My Work process, so a value can't be recorded.");
                break;
            default:
                FillWorkInstructions(db, entry, userId);
                break;
        }
    }

    private static void RelatedShopFloor(EntityEntry entry, int userId, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case WorkExecution x when entry.State == EntityState.Added:
                // The old site's "Start Process": one executed process per active My Work process of the schedule's steps.
                side.Add(new SideWrite(true, () => ($"""
                    INSERT INTO dbo.MyWorkExecutedProcess (MyWorkProcessID, MyWorkProcessVersionID, UserID, EntryDate, Complete, MyWorkExecutionID)
                    SELECT p.ID, v.VersionID, @u, @d, 0, @x
                    {RunProcessesSql}
                    ORDER BY st.Ordering, st.ID, p.ID
                    """, [P("@u", x.UserId), P("@d", x.StartedAt), P("@x", x.Id), P("@s", x.ScheduleId)])));
                break;

            case WorkExecution x when entry.State == EntityState.Modified && entry.Property(nameof(WorkExecution.Status)).IsModified
                                      && x.Status == ExecutionStatus.Completed:
                side.Add(new SideWrite(true, () => ("UPDATE dbo.MyWorkExecutedProcess SET Complete = 1 WHERE MyWorkExecutionID = @x", [P("@x", x.Id)])));
                break;

            case WorkLineCheck c when entry.State == EntityState.Added && c.RecordedValue != null:
                // Stored like the old site: whole numbers as IntValue, decimals as DecimalValue, anything else as text.
                var raw = c.RecordedValue;
                int? whole = int.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : null;
                decimal? dec = whole == null && decimal.TryParse(raw, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
                var text = whole == null && dec == null ? raw : null;
                side.Add(new SideWrite(true, () => (
                    """
                    INSERT INTO dbo.MyWorkExecutedProcessLineMetric (MyWorkExecutedProcessLineID, MyWorkProcessLineMetricID, IntValue, StringValue, DateTimeValue, DecimalValue)
                    SELECT TOP 1 @id, lm.ID, @i, @t, GETDATE(), @d FROM dbo.MyWorkProcessLineMetric lm WHERE lm.MyWorkProcessLineID = @l ORDER BY lm.ID
                    """,
                    [P("@id", c.Id), P("@i", whole), P("@t", text), P("@d", dec), P("@l", entry.Property("MyWorkProcessLineID").CurrentValue)])));
                break;

            default:
                RelatedWorkInstructions(entry, userId, side);
                break;
        }
    }

    private static void DeletingShopFloor(EntityEntry entry, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case WorkExecution x:
                // Executed processes and their entries, defects and adders go by cascade; rollups and parts have none.
                var id = x.Id;
                side.Add(new SideWrite(false, () => (
                    """
                    IF OBJECT_ID('dbo.MyWorkExecutedParts') IS NOT NULL
                        DELETE pa FROM dbo.MyWorkExecutedParts pa JOIN dbo.MyWorkExecutedProcess p ON p.ID = pa.MyWorkExecutedProcessID WHERE p.MyWorkExecutionID = @x;
                    DELETE r FROM dbo.MyWorkExecutedProcessRollup r JOIN dbo.MyWorkExecutedProcess p ON p.ID = r.MyWorkExecutedProcessID WHERE p.MyWorkExecutionID = @x;
                    """, [P("@x", id)])));
                break;
            default:
                DeletingWorkInstructions(entry, side);
                break;
        }
    }
}
