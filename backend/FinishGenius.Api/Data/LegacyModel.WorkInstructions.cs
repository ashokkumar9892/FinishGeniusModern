using System.Globalization;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FinishGenius.Api.Data;

/// <summary>
/// Work instructions on the old tables. dbo.WorkInstructions is the header; every content table (steps, equipment,
/// materials, related documents, signatures, approvals) also points at a row of dbo.WorkInstructionsVersions — always the
/// latest one on Prod. A version row is the edit trail: its Log collects "date - user text" entries. Version names are
/// "DRAFT vN" or "Issue A/B/…" (released). Many older documents have no version row until they are first changed.
/// </summary>
public static partial class LegacyModel
{
    /// <summary>Creates the document's first version row when it has none; @w = document, @u = user.</summary>
    private const string EnsureVersionSql = """
        IF NOT EXISTS (SELECT 1 FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w)
            INSERT INTO dbo.WorkInstructionsVersions (WorkInstructionsID, UserID, Version, VersionDate, Log) VALUES (@w, @u, 'DRAFT v1', GETDATE(), '');
        DECLARE @v int = (SELECT MAX(ID) FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w);
        """;

    /// <summary>Content tables and their version column.</summary>
    private static readonly (string Table, string VersionColumn)[] WiContentTables =
    [
        ("WorkInstructionsSteps", "WorkInstructionsVersionID"), ("WorkInstructionsEquipment", "WorkInstructionsVersionsID"),
        ("WorkInstructionsMaterials", "WorkInstructionsVersionsID"), ("WorkInstructionsRelatedDocuments", "WorkInstructionsVersionID"),
        ("WorkInstructionsSignatures", "WorkInstructionsVersionsID"), ("WorkInstructionsApprovals", "WorkInstructionsVersionsID"),
        ("WorkInstructionsCheckItems", "WorkInstructionsVersionsID"),
    ];

    /// <summary>The old text columns hold 50 characters.</summary>
    private const int WiText = 50;

    private static void WorkInstructionTables(ModelBuilder b)
    {
        b.Entity<WorkInstruction>(e =>
        {
            // Version = number of version rows; released = the latest version is not a DRAFT (same rules as the legacy import).
            e.ToTable("WorkInstructions", "dbo");
            e.ToSqlQuery("""
                SELECT w.ID, w.GroupID, w.Name, w.DocumentNumber, w.IssueDate, w.Controlled, w.Location, w.Purpose, w.Scope, w.Terminology,
                       ISNULL(NULLIF(v.Versions, 0), 1) AS Version,
                       CAST(CASE WHEN v.Latest IS NOT NULL AND v.Latest NOT LIKE 'DRAFT%' THEN 1 ELSE 0 END AS bit) AS IsReleased,
                       CAST(0 AS bit) AS IsDeleted,
                       CASE WHEN w.IssueDate < '1900-01-02' THEN ISNULL(v.FirstDate, CAST('2000-01-01' AS datetime)) ELSE w.IssueDate END AS CreatedAt,
                       v.LatestDate AS UpdatedAt
                FROM dbo.WorkInstructions w
                OUTER APPLY (SELECT COUNT(*) AS Versions, MIN(x.VersionDate) AS FirstDate, MAX(x.VersionDate) AS LatestDate,
                                    (SELECT TOP 1 y.Version FROM dbo.WorkInstructionsVersions y WHERE y.WorkInstructionsID = w.ID ORDER BY y.ID DESC) AS Latest
                             FROM dbo.WorkInstructionsVersions x WHERE x.WorkInstructionsID = w.ID) v
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            Max(e.Property(x => x.DocumentNumber), WiText);
            // Changed through the version rows (see RelatedWorkInstructions), never as columns.
            ReadOnly(e.Property(x => x.Version));
            ReadOnly(e.Property(x => x.IsReleased));
            ReadOnly(e.Property(x => x.IsDeleted)); // no such flag: WorkInstructionsController deletes the rows
            ReadOnly(e.Property(x => x.CreatedAt));
            ReadOnly(e.Property(x => x.UpdatedAt));
        });

        b.Entity<WorkInstructionStep>(e =>
        {
            // Steps are numbered Level1[.Level2[.Level3[.Level4]]]; a sub step shows its full number in front of its text.
            // The step text is WorkInstruction; there is no separate body (a body is appended to the text).
            e.ToTable("WorkInstructionsSteps", "dbo");
            e.ToSqlQuery("""
                SELECT s.ID, s.WorkInstructionsID, s.Level1, s.Level2, s.Level3, s.Level4, s.WorkInstruction, s.WorkInstructionsVersionID,
                       CAST(CONCAT(CASE WHEN s.Level2 IS NOT NULL THEN CONCAT(s.Level1, '.', s.Level2,
                                        CASE WHEN s.Level3 IS NOT NULL THEN CONCAT('.', s.Level3) ELSE '' END,
                                        CASE WHEN s.Level4 IS NOT NULL THEN CONCAT('.', s.Level4) ELSE '' END, ' ') ELSE '' END,
                                   ISNULL(s.WorkInstruction, '')) AS nvarchar(max)) AS Title,
                       CAST(NULL AS nvarchar(max)) AS Body
                FROM dbo.WorkInstructionsSteps s
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.WorkInstructionId).HasColumnName("WorkInstructionsID");
            e.Property(x => x.Level).HasColumnName("Level1");
            ReadOnly(e.Property(x => x.Title)); // saved as WorkInstruction (FillWorkInstructions)
            ReadOnly(e.Property(x => x.Body));
            e.Property<int?>("Level2");
            e.Property<int?>("Level3");
            e.Property<int?>("Level4");
            e.Property<string?>("WorkInstruction");
            e.Property<int>("WorkInstructionsVersionID");
        });

        b.Entity<WorkInstructionMedia>(e =>
        {
            // Up to three files per step (Attach1..3); id = step id * 4 + slot. Old files live on the old server
            // (AppData/WorkInstructions/{group}/{document}/) and are expected under uploads/work-instructions/{group}/legacy/{document}/.
            // Files added here are stored as "fg:{file name}" (the columns hold 50 characters) in work-instructions/{group}/.
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT s.ID * 4 + a.Slot AS Id, s.ID AS StepId,
                       CAST(CASE WHEN a.F LIKE 'fg:%' THEN SUBSTRING(a.F, 4, 50) ELSE a.F END AS nvarchar(400)) AS FileName,
                       CAST(CASE WHEN a.F LIKE 'fg:%' THEN CONCAT('work-instructions/', w.GroupID, '/', SUBSTRING(a.F, 4, 50))
                                 ELSE CONCAT('work-instructions/', w.GroupID, '/legacy/', w.ID, '/', a.F) END AS nvarchar(400)) AS StoredFile,
                       CAST(CASE WHEN a.F LIKE '%.mp4' THEN 'video/mp4' WHEN a.F LIKE '%.mov' THEN 'video/quicktime' WHEN a.F LIKE '%.png' THEN 'image/png'
                                 WHEN a.F LIKE '%.gif' THEN 'image/gif' ELSE 'image/jpeg' END AS nvarchar(100)) AS ContentType
                FROM dbo.WorkInstructionsSteps s
                JOIN dbo.WorkInstructions w ON w.ID = s.WorkInstructionsID
                CROSS APPLY (VALUES (1, s.Attach1), (2, s.Attach2), (3, s.Attach3)) a(Slot, F)
                WHERE ISNULL(a.F, '') <> ''
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        b.Entity<WorkInstructionTrail>(e =>
        {
            // One trail row per version; new entries are appended to the latest version's Log (RelatedWorkInstructions).
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT v.ID AS Id, v.WorkInstructionsID AS WorkInstructionId, CAST(v.Version AS nvarchar(400)) AS Version,
                       CAST(ISNULL(ISNULL(u.Email, u.Username), '(unknown)') AS nvarchar(400)) AS Author, CAST(v.Log AS nvarchar(max)) AS Log, v.VersionDate AS Date
                FROM dbo.WorkInstructionsVersions v LEFT JOIN dbo.[User] u ON u.ID = v.UserID
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        b.Entity<WorkInstructionRelatedDoc>(e =>
        {
            e.ToTable("WorkInstructionsRelatedDocuments", "dbo");
            e.ToSqlQuery("""
                SELECT r.ID, r.WorkInstructionsID, r.DocumentNumber, r.DocumentName, r.Author, CAST(NULL AS int) AS DocumentId,
                       r.WorkInstructionsVersionID, r.RelatedDocumentFile
                FROM dbo.WorkInstructionsRelatedDocuments r
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.WorkInstructionId).HasColumnName("WorkInstructionsID");
            ReadOnly(e.Property(x => x.DocumentId)); // the old site uploads a file instead of linking a library document
            e.Property(x => x.Author).HasAnnotation(NotNullText, true);
            foreach (var p in new[] { nameof(WorkInstructionRelatedDoc.DocumentNumber), nameof(WorkInstructionRelatedDoc.DocumentName), nameof(WorkInstructionRelatedDoc.Author) })
                Max(e.Property(p), WiText);
            e.Property<int>("WorkInstructionsVersionID");
            e.Property<string>("RelatedDocumentFile").HasAnnotation(InsertValue, "");
        });

        b.Entity<WorkInstructionSignature>(e =>
        {
            // Signatures (name + signature image) and approvals (name + position); id = row id * 2 (+1 for an approval).
            // New ones are approvals.
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT s.ID * 2 AS Id, s.WorkInstructionsID AS WorkInstructionId, CAST(s.Name AS nvarchar(400)) AS Name, CAST(NULL AS nvarchar(400)) AS Position, s.Date
                FROM dbo.WorkInstructionsSignatures s
                UNION ALL
                SELECT a.ID * 2 + 1, a.WorkInstructionsID, CAST(a.Name AS nvarchar(400)), CAST(NULLIF(a.Position, '') AS nvarchar(400)), a.Date
                FROM dbo.WorkInstructionsApprovals a
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        b.Entity<WorkInstructionItem>(e =>
        {
            // Tools & equipment and materials; id = row id * 2 (+1 for a material). The old tables keep the text only.
            e.ToTable((string?)null);
            e.ToSqlQuery("""
                SELECT q.ID * 2 AS Id, q.WorkInstructionsID AS WorkInstructionId, CAST('Equipment' AS nvarchar(20)) AS Kind,
                       CAST(q.Equipment AS nvarchar(400)) AS Description, CAST(NULL AS int) AS MaterialId
                FROM dbo.WorkInstructionsEquipment q
                UNION ALL
                SELECT m.ID * 2 + 1, m.WorkInstructionsID, CAST('Material' AS nvarchar(20)), CAST(m.Description AS nvarchar(400)), CAST(NULL AS int)
                FROM dbo.WorkInstructionsMaterials m
                """);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });
    }

    // ------------------------------------------------------------------ save hooks

    /// <summary>The document a content entity belongs to: tracked parent first (its id is set once it is saved).</summary>
    private static Func<int> WiOwner<T>(DbContext db, T child, Func<WorkInstruction, List<T>> children, int storedId)
    {
        var parent = db.ChangeTracker.Entries<WorkInstruction>().Select(x => x.Entity).FirstOrDefault(w => children(w).Contains(child));
        if (parent != null) children(parent).Remove(child); // or DetectChanges re-adds the detached entry
        return () => parent?.Id ?? storedId;
    }

    private static void CheckWiText(string? value, string field)
    {
        if (value is { Length: > WiText }) throw ApiException.Bad($"{field} must be {WiText} characters or fewer.");
    }

    private static bool VirtualWorkInstructions(DbContext db, EntityEntry entry, int userId, string label, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case WorkInstructionTrail t when entry.State == EntityState.Added:
            {
                var owner = WiOwner(db, t, w => w.Trail, t.WorkInstructionId);
                // The old site's format: "7/3/2023 10:50:28 AM - Name text" + blank line.
                var text = $"{DateTime.Now.ToString(CultureInfo.GetCultureInfo("en-US"))} - {t.Author} {t.Log}\n\n";
                var version = t.Version.Length > WiText ? t.Version[..WiText] : t.Version;
                side.Add(new SideWrite(true, () => (
                    """
                    IF NOT EXISTS (SELECT 1 FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w)
                        INSERT INTO dbo.WorkInstructionsVersions (WorkInstructionsID, UserID, Version, VersionDate, Log) VALUES (@w, @u, @ver, GETDATE(), @t);
                    ELSE
                        UPDATE dbo.WorkInstructionsVersions SET Log = CONCAT(Log, @t)
                        WHERE ID = (SELECT MAX(ID) FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w);
                    """,
                    [P("@w", owner()), P("@u", userId), P("@ver", version), P("@t", text)])));
                break;
            }
            case WorkInstructionTrail:
                throw ApiException.Bad("The edit trail can't be changed.");

            case WorkInstructionItem i:
            {
                var owner = WiOwner(db, i, w => w.Items, i.WorkInstructionId);
                if (entry.State != EntityState.Deleted) CheckWiText(i.Description, "Description");
                var stored = entry.State == EntityState.Added ? 0 : (int)entry.Property(nameof(i.Id)).OriginalValue!;
                var wasMaterial = stored % 2 == 1;
                var (state, isMaterial, description) = (entry.State, i.Kind == "Material", i.Description);
                if (state is EntityState.Deleted || (state == EntityState.Modified && wasMaterial != isMaterial))
                    side.Add(new SideWrite(false, () => (
                        $"DELETE FROM dbo.{(wasMaterial ? "WorkInstructionsMaterials" : "WorkInstructionsEquipment")} WHERE ID = @id", [P("@id", stored / 2)])));
                if (state == EntityState.Modified && wasMaterial == isMaterial)
                    side.Add(new SideWrite(false, () => (isMaterial
                        ? "UPDATE dbo.WorkInstructionsMaterials SET Description = @d WHERE ID = @id"
                        : "UPDATE dbo.WorkInstructionsEquipment SET Equipment = @d WHERE ID = @id", [P("@d", description), P("@id", stored / 2)])));
                if (state == EntityState.Added || (state == EntityState.Modified && wasMaterial != isMaterial))
                    side.Add(new SideWrite(true, () => (EnsureVersionSql + (isMaterial
                        ? "INSERT INTO dbo.WorkInstructionsMaterials (WorkInstructionsID, WorkInstructionsVersionsID, Description, MSDSFile, TDSFile) VALUES (@w, @v, @d, '', '')"
                        : "INSERT INTO dbo.WorkInstructionsEquipment (WorkInstructionsID, WorkInstructionsVersionsID, Equipment) VALUES (@w, @v, @d)"),
                        [P("@w", owner()), P("@u", userId), P("@d", description)])));
                break;
            }

            case WorkInstructionSignature s:
            {
                var owner = WiOwner(db, s, w => w.Signatures, s.WorkInstructionId);
                if (entry.State != EntityState.Deleted)
                {
                    CheckWiText(s.Name, "Name");
                    CheckWiText(s.Position, "Position");
                }
                var stored = entry.State == EntityState.Added ? 0 : (int)entry.Property(nameof(s.Id)).OriginalValue!;
                var (state, name, position, date) = (entry.State, s.Name, s.Position ?? "", s.Date);
                var table = stored % 2 == 1 ? "WorkInstructionsApprovals" : "WorkInstructionsSignatures";
                side.Add(state switch
                {
                    EntityState.Added => new SideWrite(true, () => (EnsureVersionSql +
                        "INSERT INTO dbo.WorkInstructionsApprovals (WorkInstructionsID, WorkInstructionsVersionsID, Name, Position, Date) VALUES (@w, @v, @n, @p, @d)",
                        [P("@w", owner()), P("@u", userId), P("@n", name), P("@p", position), P("@d", date)])),
                    EntityState.Modified => new SideWrite(false, () => (stored % 2 == 1
                        ? "UPDATE dbo.WorkInstructionsApprovals SET Name = @n, Position = @p, Date = @d WHERE ID = @id"
                        : "UPDATE dbo.WorkInstructionsSignatures SET Name = @n, Date = @d WHERE ID = @id", // a signature has no position
                        [P("@n", name), P("@p", position), P("@d", date), P("@id", stored / 2)])),
                    _ => new SideWrite(false, () => ($"DELETE FROM dbo.{table} WHERE ID = @id", [P("@id", stored / 2)])),
                });
                break;
            }

            case WorkInstructionMedia m:
            {
                var step = db.ChangeTracker.Entries<WorkInstructionStep>().Select(x => x.Entity).FirstOrDefault(x => x.Media.Contains(m));
                step?.Media.Remove(m);
                if (entry.State == EntityState.Deleted)
                {
                    var stored = (int)entry.Property(nameof(m.Id)).OriginalValue!;
                    side.Add(new SideWrite(false, () => ($"UPDATE dbo.WorkInstructionsSteps SET Attach{stored % 4} = NULL WHERE ID = @s", [P("@s", stored / 4)])));
                }
                else if (entry.State == EntityState.Added)
                {
                    var file = "fg:" + Path.GetFileName(m.StoredFile);
                    if (file.Length > WiText) throw ApiException.Bad($"File name \"{m.FileName}\" is too long for the {label} database.");
                    var stepId = m.StepId;
                    // Fills the first free slot (WorkInstructionsController allows three files per step).
                    side.Add(new SideWrite(true, () => (
                        """
                        UPDATE dbo.WorkInstructionsSteps SET
                            Attach1 = CASE WHEN ISNULL(Attach1, '') = '' THEN @f ELSE Attach1 END,
                            Attach2 = CASE WHEN ISNULL(Attach1, '') <> '' AND ISNULL(Attach2, '') = '' THEN @f ELSE Attach2 END,
                            Attach3 = CASE WHEN ISNULL(Attach1, '') <> '' AND ISNULL(Attach2, '') <> '' AND ISNULL(Attach3, '') = '' THEN @f ELSE Attach3 END
                        WHERE ID = @s
                        """, [P("@f", file), P("@s", step?.Id ?? stepId)])));
                }
                else throw ApiException.Bad("A step file can't be changed; delete it and upload it again.");
                break;
            }

            default:
                return false;
        }
        entry.State = EntityState.Detached;
        return true;
    }

    /// <summary>The document's latest version row (created when it has none): contents rows must point at it.</summary>
    private static int WiVersionId(DbContext db, int workInstructionId, int userId)
    {
        if (workInstructionId <= 0)
            throw new InvalidOperationException("Save a new work instruction before adding its contents: the old tables need its version row.");
        db.Database.ExecuteSqlRaw(EnsureVersionSql, P("@w", workInstructionId), P("@u", userId));
        return db.Database.SqlQueryRaw<int>("SELECT MAX(ID) AS Value FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w", P("@w", workInstructionId))
            .AsEnumerable().First();
    }

    private static string StepNumberPrefix(int level1, int? level2, int? level3, int? level4) =>
        level2 == null ? "" : $"{level1}.{level2}{(level3 != null ? $".{level3}" : "")}{(level4 != null ? $".{level4}" : "")} ";

    private static void FillWorkInstructions(DbContext db, EntityEntry entry, int userId)
    {
        switch (entry.Entity)
        {
            case WorkInstructionStep s:
                if (entry.State == EntityState.Added)
                    entry.Property("WorkInstructionsVersionID").CurrentValue = WiVersionId(db, s.WorkInstructionId, userId);
                if (entry.State == EntityState.Added || entry.Property(nameof(s.Title)).IsModified || entry.Property(nameof(s.Body)).IsModified)
                {
                    // The shown number of a sub step is not part of its text.
                    var prefix = entry.State == EntityState.Added ? "" : StepNumberPrefix((int)entry.Property(nameof(s.Level)).OriginalValue!,
                        (int?)entry.Property("Level2").CurrentValue, (int?)entry.Property("Level3").CurrentValue, (int?)entry.Property("Level4").CurrentValue);
                    var text = prefix.Length > 0 && s.Title.StartsWith(prefix, StringComparison.Ordinal) ? s.Title[prefix.Length..] : s.Title;
                    entry.Property("WorkInstruction").CurrentValue = string.IsNullOrWhiteSpace(s.Body) ? text : $"{text}\n\n{s.Body}";
                }
                break;
            case WorkInstructionRelatedDoc d when entry.State == EntityState.Added:
                entry.Property("WorkInstructionsVersionID").CurrentValue = WiVersionId(db, d.WorkInstructionId, userId);
                break;
        }
    }

    private static void RelatedWorkInstructions(EntityEntry entry, int userId, List<SideWrite> side)
    {
        if (entry.Entity is not WorkInstruction w || entry.State != EntityState.Modified) return;
        var released = entry.Property(nameof(w.IsReleased));
        if ((bool)released.OriginalValue! == w.IsReleased) return;
        var id = w.Id;
        if (w.IsReleased)
            // Release: the latest version becomes the next issue (Issue A, B, …), like the old site's "Controlled".
            side.Add(new SideWrite(true, () => (
                """
                DECLARE @v int = (SELECT MAX(ID) FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w);
                DECLARE @n int = 1 + (SELECT COUNT(*) FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w AND ID <> ISNULL(@v, 0) AND Version NOT LIKE 'DRAFT%');
                DECLARE @name nvarchar(50) = CASE WHEN @n <= 26 THEN CONCAT('Issue ', CHAR(64 + @n)) ELSE CONCAT('Issue v', @n) END;
                IF @v IS NULL
                    INSERT INTO dbo.WorkInstructionsVersions (WorkInstructionsID, UserID, Version, VersionDate, Log) VALUES (@w, @u, @name, GETDATE(), '');
                ELSE
                    UPDATE dbo.WorkInstructionsVersions SET Version = @name, VersionDate = GETDATE() WHERE ID = @v;
                """, [P("@w", id), P("@u", userId)])));
        else
            // A change to a released document starts the next DRAFT version; its contents move to it (as the old site does).
            side.Add(new SideWrite(true, () => (
                $"""
                DECLARE @name nvarchar(50) = CONCAT('DRAFT v', 1 + (SELECT COUNT(*) FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w));
                INSERT INTO dbo.WorkInstructionsVersions (WorkInstructionsID, UserID, Version, VersionDate, Log) VALUES (@w, @u, @name, GETDATE(), '');
                DECLARE @v int = SCOPE_IDENTITY();
                {string.Concat(WiContentTables.Select(t => $"UPDATE dbo.{t.Table} SET {t.VersionColumn} = @v WHERE WorkInstructionsID = @w;\n"))}
                """, [P("@w", id), P("@u", userId)])));
    }

    private static void DeletingWorkInstructions(EntityEntry entry, List<SideWrite> side)
    {
        if (entry.Entity is not WorkInstruction w) return;
        // No cascades from the document except steps and versions, and steps point at versions: delete in order.
        var id = w.Id;
        side.Add(new SideWrite(false, () => (
            $"""
            {string.Concat(WiContentTables.Select(t => $"DELETE FROM dbo.{t.Table} WHERE WorkInstructionsID = @w;\n"))}
            DELETE FROM dbo.WorkInstructionsVersions WHERE WorkInstructionsID = @w;
            DELETE FROM dbo.ProcessStepWorkInstructions WHERE WorkInstructionID = @w;
            UPDATE dbo.MyWorkProcess SET WorkInstructionID = NULL WHERE WorkInstructionID = @w;
            """, [P("@w", id)])));
    }
}
