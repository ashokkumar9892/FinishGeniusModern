using System.Globalization;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FinishGenius.Api.Data;

/// <summary>
/// Process &amp; pricing on the old tables: sub steps (SubStep + PullDownDefinition), process step contents
/// (FinishingStepsPullDowns = the category chosen per sub step / pass, FinishingStepsDetails = the values) and schedule
/// contents (FinishingSchedulesSteps + FinishingSchedulesStepsValues, a full copy of the step values per schedule step).
/// </summary>
public static partial class LegacyModel
{
    /// <summary>App property the old tables cannot store: changing it is refused with this description.</summary>
    public const string Unsupported = "Legacy:Unsupported";

    /// <summary>Latest pull-down row per (step, sub step, pull-down, pass): the old site leaves duplicates behind.</summary>
    private const string EntrySql = """
        SELECT x.ID, x.StepID, x.SubStepID, ISNULL(x.PassTrought, 1) AS Pass, x.PullDownID, TRY_CAST(NULLIF(LTRIM(x.Value), '') AS int) AS CategoryId
        FROM (SELECT p.ID, p.StepID, p.SubStepID, p.PassTrought, p.PullDownID, p.Value,
                     ROW_NUMBER() OVER (PARTITION BY p.StepID, p.SubStepID, p.PullDownID, ISNULL(p.PassTrought, 1) ORDER BY p.ID DESC) AS rn
              FROM dbo.FinishingStepsPullDowns p) x
        WHERE x.rn = 1
        """;

    private static void Processes(ModelBuilder b)
    {
        SubSteps(b);
        StepContents(b);
        ScheduleContents(b);
    }

    // ------------------------------------------------------------------ sub steps

    private static void SubSteps(ModelBuilder b)
    {
        b.Entity<SubStep>(e =>
        {
            // Sequence is numeric text in the old table; int parameters convert implicitly when saving.
            e.ToTable("SubStep", "dbo");
            e.ToSqlQuery("""
                SELECT s.ID, ISNULL(s.ConfigurationID, (SELECT TOP 1 c.ID FROM dbo.ConfigurationName c WHERE c.ConfigName = 'Wood')) AS ConfigurationID,
                       ISNULL(NULLIF(s.UserRole, ''), 'User') AS UserRole, LTRIM(RTRIM(s.Name)) AS Name, LTRIM(RTRIM(s.ShortName)) AS ShortName,
                       ISNULL(TRY_CAST(s.Sequence AS int), s.ID) AS Sequence, CASE WHEN s.PassThroughsNumber > 0 THEN s.PassThroughsNumber ELSE 1 END AS PassThroughsNumber,
                       s.WebLink, s.Instruction
                FROM dbo.SubStep s
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.IndustrySectorId).HasColumnName("ConfigurationID");
            e.Property(x => x.PassThroughs).HasColumnName("PassThroughsNumber");
            e.Property(x => x.Instruction).HasColumnType("nvarchar(max)");
        });

        b.Entity<SubStepPullDown>(e =>
        {
            // The old site stores a raw SQL query per pull-down (it must return ID, Name from Categories). Material type and
            // filters are read out of it; when they change a query of the same form is written (PullDownQuery).
            e.ToTable("PullDownDefinition", "dbo");
            e.ToSqlQuery("""
                SELECT p.ID, p.SubStepID, ISNULL(TRY_CAST(p.Sequence AS int), 1) AS Sequence,
                       ISNULL(NULLIF(LTRIM(RTRIM(p.Header)), ''), ISNULL(p.ChoiceName, 'Select category')) AS Header, p.ChoiceName,
                       CASE WHEN mt.v BETWEEN 1 AND 7 THEN mt.v END AS MaterialType,
                       REPLACE(f1.v, CHAR(1), '''') AS CategoryFilter1, REPLACE(f2.v, CHAR(1), '''') AS CategoryFilter2, p.Query
                FROM dbo.PullDownDefinition p
                CROSS APPLY (SELECT q = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(ISNULL(p.Query, ''), '''''', CHAR(1)), CHAR(13), ' '), CHAR(10), ' '), '[', ''), ']', '')) n
                CROSS APPLY (SELECT r = CASE WHEN CHARINDEX('MaterialType', n.q) > 0 THEN LTRIM(SUBSTRING(n.q, CHARINDEX('MaterialType', n.q) + 12, 40)) END) m1
                CROSS APPLY (SELECT v = CASE WHEN LEFT(m1.r, 1) = '=' THEN TRY_CAST(LEFT(LTRIM(SUBSTRING(m1.r, 2, 40)), 1) AS int) END) mt
                CROSS APPLY (SELECT r = CASE WHEN CHARINDEX('Filter1', n.q) > 0 THEN LTRIM(SUBSTRING(n.q, CHARINDEX('Filter1', n.q) + 7, 400)) END) a1
                CROSS APPLY (SELECT r = CASE WHEN LEFT(a1.r, 1) = '=' THEN LTRIM(SUBSTRING(a1.r, 2, 400)) END) b1
                CROSS APPLY (SELECT v = CASE WHEN LEFT(b1.r, 1) = '''' AND CHARINDEX('''', b1.r, 2) > 2 THEN SUBSTRING(b1.r, 2, CHARINDEX('''', b1.r, 2) - 2) END) f1
                CROSS APPLY (SELECT r = CASE WHEN CHARINDEX('Filter2', n.q) > 0 THEN LTRIM(SUBSTRING(n.q, CHARINDEX('Filter2', n.q) + 7, 400)) END) a2
                CROSS APPLY (SELECT r = CASE WHEN LEFT(a2.r, 1) = '=' THEN LTRIM(SUBSTRING(a2.r, 2, 400)) END) b2
                CROSS APPLY (SELECT v = CASE WHEN LEFT(b2.r, 1) = '''' AND CHARINDEX('''', b2.r, 2) > 2 THEN SUBSTRING(b2.r, 2, CHARINDEX('''', b2.r, 2) - 2) END) f2
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.SubStepId).HasColumnName("SubStepID");
            ReadOnly(e.Property(x => x.MaterialType));    // written as part of Query
            ReadOnly(e.Property(x => x.CategoryFilter1));
            ReadOnly(e.Property(x => x.CategoryFilter2));
            e.Property<string?>("Query").HasColumnType("nvarchar(max)");
        });
    }

    /// <summary>Pull-down query in the old site's form.</summary>
    private static string PullDownQuery(MaterialType? type, string? filter1, string? filter2)
    {
        static string Quote(string s) => "'" + s.Trim().Replace("'", "''") + "'";
        var where = new List<string>();
        if (type != null) where.Add($"MaterialType = {(int)type}");
        if (!string.IsNullOrWhiteSpace(filter1)) where.Add($"Filter1 = {Quote(filter1)}");
        if (!string.IsNullOrWhiteSpace(filter2)) where.Add($"Filter2 = {Quote(filter2)}");
        return "Select ID, Name From Categories" + (where.Count > 0 ? " Where " + string.Join(" And ", where) : "");
    }

    // ------------------------------------------------------------------ process step contents

    private static void StepContents(ModelBuilder b)
    {
        b.Entity<ProcessStepEntry>(e =>
        {
            // One FinishingStepsPullDowns row = the category chosen for one pull-down in one pass of a sub step.
            // Changes are written by VirtualProcess (the row is kept with a blank value when a category is cleared).
            e.ToTable((string?)null);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.ToSqlQuery(EntrySql);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.ProcessStepId).HasColumnName("StepID");
            e.Property(x => x.SubStepId).HasColumnName("SubStepID");
            e.Property(x => x.PullDownId).HasColumnName("PullDownID");
        });

        b.Entity<ProcessStepValue>(e =>
        {
            // FinishingStepsDetails: one row per characteristic of the chosen category, keyed by step / sub step / pass.
            // Material values are stored as the material id in Value. The entry is found by (step, sub step, pass, category).
            e.ToTable("FinishingStepsDetails", "dbo");
            // Driven from the entries and de-duplicated after the join (the details table has no StepID index; ~0.85 s per
            // schedule on Prod instead of ~1 s when ranking all 266k rows first).
            e.ToSqlQuery($"""
                SELECT z.ID, z.EntryId, z.CharacteristicsID, z.Value, z.PickedMaterialId, z.StepID, z.SubStepID, z.PassTrought, z.CalcVariable, z.MaterialID
                FROM (SELECT d.ID, en.ID AS EntryId, d.CharacteristicsID,
                             CASE WHEN ch.IsMaterial = 1 THEN NULL ELSE d.Value END AS Value,
                             CASE WHEN ch.IsMaterial = 1 THEN TRY_CAST(NULLIF(LTRIM(d.Value), '') AS int) END AS PickedMaterialId,
                             d.StepID, d.SubStepID, d.PassTrought, d.CalcVariable, d.MaterialID,
                             ROW_NUMBER() OVER (PARTITION BY en.ID, d.CharacteristicsID ORDER BY d.ID DESC) AS rn
                      FROM ({EntrySql}) en
                      JOIN dbo.FinishingStepsDetails d ON d.StepID = en.StepID AND d.SubStepID = en.SubStepID AND ISNULL(d.PassTrought, 1) = en.Pass
                      JOIN (SELECT c.ID, c.CategoryID, CASE WHEN c.PDQuery IS NOT NULL OR c.CalcVariable LIKE '%[_]ID' THEN 1 ELSE 0 END AS IsMaterial
                            FROM dbo.Characteristics c) ch ON ch.ID = d.CharacteristicsID AND ch.CategoryID = en.CategoryId) z
                WHERE z.rn = 1
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.CharacteristicId).HasColumnName("CharacteristicsID");
            e.Property(x => x.Value).HasColumnType("nvarchar(max)");
            e.Property(x => x.MaterialId).HasColumnName("PickedMaterialId");
            ReadOnly(e.Property(x => x.EntryId));    // derived from step / sub step / pass / category
            ReadOnly(e.Property(x => x.MaterialId)); // saved as Value (see FillStepValue)
            e.Property<int?>("StepID");
            e.Property<int?>("SubStepID");
            e.Property<int?>("PassTrought");
            e.Property<string?>("CalcVariable").HasColumnType("nvarchar(max)");
            e.Property<int?>("MaterialID").HasAnnotation(InsertValue, 0);
        });
    }

    /// <summary>A step value's old-table columns: the entry's step / sub step / pass (NULL for single-pass sub steps),
    /// the characteristic's calculation tag, and the material id as the value.</summary>
    private static void FillStepValue(DbContext db, EntityEntry entry, ProcessStepEntry? parent)
    {
        var v = (ProcessStepValue)entry.Entity;
        if (entry.State == EntityState.Added && parent != null && entry.Property("StepID").CurrentValue == null)
        {
            var passes = db.Database.SqlQueryRaw<int?>("SELECT PassThroughsNumber AS Value FROM dbo.SubStep WHERE ID = {0}", parent.SubStepId).AsEnumerable().FirstOrDefault();
            entry.Property("StepID").CurrentValue = parent.ProcessStepId;
            entry.Property("SubStepID").CurrentValue = parent.SubStepId;
            entry.Property("PassTrought").CurrentValue = passes > 1 ? parent.Pass : null;
            entry.Property("CalcVariable").CurrentValue = db.Database
                .SqlQueryRaw<string?>("SELECT CalcVariable AS Value FROM dbo.Characteristics WHERE ID = {0}", v.CharacteristicId).AsEnumerable().FirstOrDefault();
        }
        if (v.MaterialId is { } material && (entry.State == EntityState.Added || entry.Property(nameof(ProcessStepValue.MaterialId)).IsModified))
            v.Value = material.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ schedule contents

    private static void ScheduleContents(ModelBuilder b)
    {
        b.Entity<ProcessScheduleStep>(e =>
        {
            // Ordering is 0-based in the old table. A rename is the Description (the old site copies the step name there).
            e.ToTable("FinishingSchedulesSteps", "dbo");
            e.ToSqlQuery("""
                SELECT st.ID, st.FinishingScheduleID, st.FinishingStepID, st.Ordering,
                       CASE WHEN NULLIF(LTRIM(RTRIM(st.Description)), '') IS NOT NULL AND LTRIM(RTRIM(st.Description)) <> LTRIM(RTRIM(ISNULL(fs.Description, '')))
                            THEN LTRIM(RTRIM(st.Description)) END AS NameOverride,
                       st.Description
                FROM dbo.FinishingSchedulesSteps st LEFT JOIN dbo.FinishingSteps fs ON fs.ID = st.FinishingStepID
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.ScheduleId).HasColumnName("FinishingScheduleID");
            e.Property(x => x.ProcessStepId).HasColumnName("FinishingStepID");
            e.Property(x => x.Ordering).HasConversion(v => v - 1, v => v + 1);
            ReadOnly(e.Property(x => x.NameOverride)); // saved as Description (FillProcess)
            e.Property<string?>("Description").HasColumnType("nvarchar(max)");
        });

        b.Entity<ScheduleStepOverride>(e =>
        {
            // Old schedules hold a full copy of the step values; an "override" is a copy that differs from the step value
            // (the old site reads the first copy by id). The 3.7M-row value table is only indexed by FinishingStepsDetailID,
            // so the query goes schedule step → the step's details → their copies (8.6 s → 0.25 s on Prod).
            // Changes are written by VirtualProcess.
            e.ToTable((string?)null);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.ToSqlQuery("""
                SELECT v.ID AS Id, v.FinishingSchedulesStepID AS ScheduleStepId, v.FinishingStepsDetailID AS ProcessStepValueId, v.Value,
                       CAST(NULL AS decimal(18,4)) AS MinValue, CAST(NULL AS decimal(18,4)) AS MaxValue
                FROM dbo.FinishingSchedulesSteps ss
                JOIN dbo.FinishingStepsDetails d ON d.StepID = ss.FinishingStepID
                JOIN dbo.FinishingSchedulesStepsValues v ON v.FinishingStepsDetailID = d.ID AND v.FinishingSchedulesStepID = ss.ID
                WHERE ISNULL(v.Value, '') <> ISNULL(d.Value, '')
                  AND v.ID = (SELECT MIN(y.ID) FROM dbo.FinishingSchedulesStepsValues y
                              WHERE y.FinishingStepsDetailID = v.FinishingStepsDetailID AND y.FinishingSchedulesStepID = v.FinishingSchedulesStepID)
                """);
        });
    }

    // ------------------------------------------------------------------ save hooks

    private static bool VirtualProcess(DbContext db, EntityEntry entry, int userId, string label, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case ProcessStepEntry pe:
                // Values of a new / changed entry are saved as normal rows; give them their old-table columns now,
                // and detach them from the entry (it is not an EF-saved row).
                foreach (var v in pe.Values.ToList()) // changing EntryId makes EF fix up pe.Values
                {
                    var ve = db.Entry(v);
                    if (ve.State != EntityState.Added) continue;
                    FillStepValue(db, ve, pe);
                    ve.Property(nameof(ProcessStepValue.EntryId)).CurrentValue = 0;
                }
                var id = entry.State == EntityState.Added ? 0 : (int)entry.Property(nameof(ProcessStepEntry.Id)).OriginalValue!;
                var state = entry.State;
                side.Add(new SideWrite(true, () => state switch
                {
                    EntityState.Added => (
                        """
                        INSERT INTO dbo.FinishingStepsPullDowns (StepID, SubStepID, PullDownID, Value, PassTrought)
                        VALUES (@step, @sub, @pd, ISNULL(CAST(@cat AS nvarchar(20)), ''),
                                CASE WHEN (SELECT PassThroughsNumber FROM dbo.SubStep WHERE ID = @sub) > 1 THEN @pass END)
                        """,
                        [P("@step", pe.ProcessStepId), P("@sub", pe.SubStepId), P("@pd", pe.PullDownId), P("@cat", pe.CategoryId), P("@pass", pe.Pass)]),
                    EntityState.Modified => (
                        "UPDATE dbo.FinishingStepsPullDowns SET Value = ISNULL(CAST(@cat AS nvarchar(20)), ''), PullDownID = ISNULL(@pd, PullDownID) WHERE ID = @id",
                        [P("@cat", pe.CategoryId), P("@pd", pe.PullDownId), P("@id", id)]),
                    _ => ("UPDATE dbo.FinishingStepsPullDowns SET Value = '' WHERE ID = @id", [P("@id", id)]),
                }));
                break;

            case ScheduleStepOverride o:
                if (entry.State != EntityState.Deleted && (o.MinValue != null || o.MaxValue != null))
                    throw new ApiException(StatusCodes.Status409Conflict,
                        $"Allowed ranges for schedule values aren't available on the {label} database yet (the old site keeps them per My Work metric).");
                var ss = (int)entry.Property(nameof(o.ScheduleStepId)).OriginalValue!;
                var detail = (int)entry.Property(nameof(o.ProcessStepValueId)).OriginalValue!;
                var reset = entry.State == EntityState.Deleted;
                var value = o.Value;
                side.Add(new SideWrite(true, () => (
                    """
                    DECLARE @v nvarchar(max) = CASE WHEN @reset = 1 OR @val IS NULL THEN (SELECT ISNULL(d.Value, '') FROM dbo.FinishingStepsDetails d WHERE d.ID = @d) ELSE @val END;
                    UPDATE dbo.FinishingSchedulesStepsValues SET Value = @v
                    WHERE ID = (SELECT MIN(x.ID) FROM dbo.FinishingSchedulesStepsValues x WHERE x.FinishingSchedulesStepID = @ss AND x.FinishingStepsDetailID = @d);
                    IF @@ROWCOUNT = 0
                        INSERT INTO dbo.FinishingSchedulesStepsValues (FinishingSchedulesStepID, FinishingStepsDetailID, Value) VALUES (@ss, @d, @v);
                    """,
                    [P("@ss", ss), P("@d", detail), P("@val", value), P("@reset", reset)])));
                break;

            default:
                return VirtualShopFloor(db, entry, userId, label, side);
        }
        entry.State = EntityState.Detached;
        return true;
    }

    private static void FillProcess(DbContext db, EntityEntry entry)
    {
        switch (entry.Entity)
        {
            case SubStepPullDown p when entry.State == EntityState.Added
                                        || entry.Property(nameof(p.MaterialType)).IsModified
                                        || entry.Property(nameof(p.CategoryFilter1)).IsModified
                                        || entry.Property(nameof(p.CategoryFilter2)).IsModified:
                entry.Property("Query").CurrentValue = PullDownQuery(p.MaterialType, p.CategoryFilter1, p.CategoryFilter2);
                break;

            case ProcessStepValue v:
                var parent = db.ChangeTracker.Entries<ProcessStepEntry>().Select(x => x.Entity).FirstOrDefault(x => x.Values.Contains(v));
                FillStepValue(db, entry, parent);
                break;

            case ProcessScheduleStep s when entry.State == EntityState.Added || entry.Property(nameof(s.NameOverride)).IsModified:
                var stepName = s.ProcessStep?.Name
                               ?? db.ChangeTracker.Entries<ProcessStep>().Select(x => x.Entity).FirstOrDefault(x => x.Id == s.ProcessStepId)?.Name
                               ?? db.Database.SqlQueryRaw<string?>("SELECT Description AS Value FROM dbo.FinishingSteps WHERE ID = {0}", s.ProcessStepId).AsEnumerable().FirstOrDefault();
                entry.Property("Description").CurrentValue = string.IsNullOrWhiteSpace(s.NameOverride) ? stepName : s.NameOverride;
                break;

            case ProcessSchedule sc when entry.State == EntityState.Modified:
                // One stored area per side: the pricing "price areas" are the same columns.
                if (entry.Property(nameof(sc.OneSidedPriceArea)).IsModified) sc.OneSidedArea = sc.OneSidedPriceArea;
                if (entry.Property(nameof(sc.TwoSidedPriceArea)).IsModified) sc.TwoSidedArea = sc.TwoSidedPriceArea;
                break;
        }
    }

    private static void RelatedProcess(EntityEntry entry, List<SideWrite> side)
    {
        if (entry.Entity is ProcessScheduleStep s && entry.State == EntityState.Added)
            // Like the old site: a new schedule step gets a copy of every value and pull-down choice of its step.
            side.Add(new SideWrite(true, () => (
                """
                INSERT INTO dbo.FinishingSchedulesStepsValues (FinishingSchedulesStepID, FinishingStepsDetailID, Value)
                SELECT @ss, d.ID, ISNULL(d.Value, '') FROM dbo.FinishingStepsDetails d
                WHERE d.StepID = @step AND NOT EXISTS (SELECT 1 FROM dbo.FinishingSchedulesStepsValues x WHERE x.FinishingSchedulesStepID = @ss AND x.FinishingStepsDetailID = d.ID);
                INSERT INTO dbo.FinishingSchedulesStepsValues (FinishingSchedulesStepID, FinishingStepsPullDownsId, Value)
                SELECT @ss, p.ID, ISNULL(p.Value, '') FROM dbo.FinishingStepsPullDowns p
                WHERE p.StepID = @step AND NOT EXISTS (SELECT 1 FROM dbo.FinishingSchedulesStepsValues x WHERE x.FinishingSchedulesStepID = @ss AND x.FinishingStepsPullDownsId = p.ID);
                """,
                [P("@ss", s.Id), P("@step", s.ProcessStepId)])));
    }

    private static void DeletingProcess(EntityEntry entry, List<SideWrite> side)
    {
        if (entry.Entity is ProcessScheduleStep s)
        {
            // Values go by cascade; My Work tolerances (conditionals) have no cascade.
            var id = s.Id;
            side.Add(new SideWrite(false, () => ("DELETE FROM dbo.FinishingSchedulesStepCondtionals WHERE FinishingSchedulesStepID = @id", [P("@id", id)])));
        }
        else
            DeletingShopFloor(entry, side);
    }
}
