using System.Globalization;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinishGenius.Api.Data;

/// <summary>SQL that LegacyAppDbContext runs before or after SaveChanges for changes the old tables cannot take directly.</summary>
public sealed record SideWrite(bool AfterSave, Func<(string Sql, SqlParameter[] Args)?> Build, Action? Then = null);

/// <summary>Materials &amp; formulas on the old tables: formulas, ingredients, the inventory ledger, documents, dispensing.</summary>
public static partial class LegacyModel
{
    /// <summary>Entity the old site owns: any change is refused with this description ("… isn't available on the X database yet").</summary>
    public const string ReadOnlyEntity = "Legacy:ReadOnlyEntity";

    private static void MaterialsAndFormulas(ModelBuilder b)
    {
        Formulas(b);
        Inventory(b);
        Documents(b);
        Dispensing(b);
    }

    // ------------------------------------------------------------------ formulas

    private static void Formulas(ModelBuilder b)
    {
        b.Entity<Formula>(e =>
        {
            // A formulation is a dbo.Materials row (Discriminator 'Formulation'), so the formula IS its own "mirror" material.
            // "MATERIALS" names the same table on the case-insensitive legacy database; the different spelling stops EF from
            // treating Formula and Material (both on dbo.Materials) as table splitting.
            e.ToTable("MATERIALS", "dbo");
            e.ToSqlQuery("""
                SELECT m.ID, ISNULL(m.GroupID, 1066) AS GroupID, m.CategoryId,
                       ISNULL(NULLIF(LTRIM(RTRIM(m.ManufacturersProductName)), ''), CONCAT('(unnamed #', m.ID, ')')) AS ManufacturersProductName,
                       LTRIM(RTRIM(m.ManufacturersProductCode)) AS ManufacturersProductCode, m.CustomerName,
                       ISNULL(m.Status, 2) AS Status, ISNULL(m.GramsInBatch, 0) AS GramsInBatch,
                       CASE WHEN m.BatchType BETWEEN 1 AND 5 THEN m.BatchType ELSE 2 END AS BatchType, ISNULL(m.BatchValue, 0) AS BatchValue,
                       m.EmployeeName, m.PurchaseOrderNumber, m.MixedOn, m.DispenserID,
                       NULLIF(m.ContainerType, 0) AS ContainerType, ISNULL(m.ContainerPrice, 0) AS ContainerPrice, ISNULL(m.MarkUp, 0) AS MarkUp,
                       m.Substrate, m.Notes,
                       CAST(TRY_CAST(m.SpinDeltaL AS decimal(18,4)) AS nvarchar(40)) AS SpinDeltaL, CAST(TRY_CAST(m.SpinDeltaA AS decimal(18,4)) AS nvarchar(40)) AS SpinDeltaA,
                       CAST(TRY_CAST(m.SpinDeltaB AS decimal(18,4)) AS nvarchar(40)) AS SpinDeltaB, CAST(TRY_CAST(m.SpinDeltaE AS decimal(18,4)) AS nvarchar(40)) AS SpinDeltaE,
                       CAST(TRY_CAST(m.SpexDeltaL AS decimal(18,4)) AS nvarchar(40)) AS SpexDeltaL, CAST(TRY_CAST(m.SpexDeltaA AS decimal(18,4)) AS nvarchar(40)) AS SpexDeltaA,
                       CAST(TRY_CAST(m.SpexDeltaB AS decimal(18,4)) AS nvarchar(40)) AS SpexDeltaB, CAST(TRY_CAST(m.SpexDeltaE AS decimal(18,4)) AS nvarchar(40)) AS SpexDeltaE,
                       CAST(NULL AS int) AS CreatedBy, ISNULL(m.IsDeleted, 0) AS IsDeleted,
                       COALESCE(fc.CreatedDate, m.MixedOn, CAST('2000-01-01' AS datetime)) AS CreatedAt, CAST(NULL AS datetime) AS UpdatedAt,
                       m.ID AS MaterialId, m.Discriminator, m.MaterialType, m.FormulaCost, m.PricePerGallon, m.VOC, m.HAP, m.TAP
                FROM dbo.Materials m
                LEFT JOIN (SELECT MaterialsId, MIN(CreatedDate) AS CreatedDate FROM dbo.FormulaCreated GROUP BY MaterialsId) fc ON fc.MaterialsId = m.ID
                WHERE m.Discriminator = 'Formulation'
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.Name).HasColumnName("ManufacturersProductName");
            e.Property(x => x.Number).HasColumnName("ManufacturersProductCode");
            e.Property(x => x.IsComplete).HasColumnName("Status").HasConversion(new ValueConverter<bool, int>(v => v ? 1 : 2, v => v == 1));
            e.Property(x => x.BatchSize).HasColumnName("GramsInBatch");
            e.Property(x => x.DispenserId).HasColumnName("DispenserID");
            // Always write BatchType (the fg model lets the database default it; the old table has no default).
            e.Property(x => x.BatchType).ValueGeneratedNever().Metadata.RemoveAnnotation(RelationalAnnotationNames.DefaultValue);
            e.Property(x => x.ContainerType).HasConversion(new ValueConverter<string?, int>(
                v => v == "1 Gallon" ? 1 : v == "5 Gallons" ? 2 : v == "Drum" ? 3 : v == "Quartz" ? 4 : 0,
                v => v == 1 ? "1 Gallon" : v == 2 ? "5 Gallons" : v == 3 ? "Drum" : v == 4 ? "Quartz" : "")).HasColumnType("int");
            var text = new ValueConverter<decimal, string>(v => v.ToString(CultureInfo.InvariantCulture), v => decimal.Parse(v, CultureInfo.InvariantCulture));
            foreach (var p in new[] { "SpinDeltaL", "SpinDeltaA", "SpinDeltaB", "SpinDeltaE", "SpexDeltaL", "SpexDeltaA", "SpexDeltaB", "SpexDeltaE" })
            {
                var pb = e.Property(p).HasConversion(text).HasColumnType("nvarchar(max)");
                pb.Metadata.SetPrecision(null);
                pb.Metadata.SetScale(null);
            }
            ReadOnly(e.Property(x => x.CreatedBy));   // the old site does not record the creator
            ReadOnly(e.Property(x => x.CreatedAt));   // dbo.FormulaCreated, written on insert
            ReadOnly(e.Property(x => x.UpdatedAt));
            ReadOnly(e.Property(x => x.MaterialId));  // = ID: the formula row is the material
            e.Property<string>("Discriminator").HasAnnotation(InsertValue, "Formulation");
            e.Property<int?>("MaterialType").HasAnnotation(InsertValue, (int)MaterialType.Formula);
            e.Property<decimal?>("FormulaCost").HasAnnotation(InsertValue, 0m);
            e.Property<decimal?>("PricePerGallon");   // mixed-batch figures, set by FormulaMirror
            e.Property<double?>("VOC").HasColumnType("float");
            e.Property<double?>("HAP").HasColumnType("float");
            e.Property<double?>("TAP").HasColumnType("float");
        });

        b.Entity<FormulaIngredient>(e =>
        {
            // The old site has no ordering column (rows keep insert order) and keeps the chosen batch in FormulationMaterialBatch.
            e.ToTable("FormulationMaterials", "dbo");
            e.ToSqlQuery("""
                SELECT fm.ID, fm.FormulationID, fm.ColourID, fm.Grams, ISNULL(fm.DispenseAmmount, 0) AS DispenseAmmount,
                       ISNULL(fm.TotalDispensedAmmount, 0) AS TotalDispensedAmmount, ISNULL(fm.isDispensed, 0) AS isDispensed,
                       bn.BatchNum AS BatchNumber, CAST(ROW_NUMBER() OVER (PARTITION BY fm.FormulationID ORDER BY fm.ID) AS int) AS Sequence
                FROM dbo.FormulationMaterials fm
                LEFT JOIN (SELECT fmb.FormulationID, fmb.MaterialID, MAX(b.BatchNum) AS BatchNum
                           FROM dbo.FormulationMaterialBatch fmb JOIN dbo.MaterialBatches b ON b.BatchID = fmb.BatchID
                           GROUP BY fmb.FormulationID, fmb.MaterialID) bn ON bn.FormulationID = fm.FormulationID AND bn.MaterialID = fm.ColourID
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.FormulaId).HasColumnName("FormulationID");
            e.Property(x => x.MaterialId).HasColumnName("ColourID");
            Float(e.Property(x => x.Grams));
            Float(e.Property(x => x.DispenseAmount).HasColumnName("DispenseAmmount"));
            Float(e.Property(x => x.DispensedGrams).HasColumnName("TotalDispensedAmmount"));
            e.Property(x => x.IsDispensed).HasColumnName("isDispensed");
            ReadOnly(e.Property(x => x.BatchNumber)); // saved to FormulationMaterialBatch after SaveChanges
            ReadOnly(e.Property(x => x.Sequence));
        });
    }

    // ------------------------------------------------------------------ inventory

    private static void Inventory(ModelBuilder b) => b.Entity<InventoryTransaction>(e =>
    {
        // The old site keeps stock per batch (MaterialBatches.BatchQty, gallons) plus a change log (MaterialQuantityChanges).
        // The ledger is: one opening row per batch (current quantity minus the logged changes) plus one row per change, so
        // SUM(Quantity) per material/batch equals the stored BatchQty. New rows are written by InventorySql (never directly).
        // Quantities are clamped like the legacy import: a few old float values do not fit decimal(18,4).
        e.ToTable((string?)null);
        e.Property(x => x.Id).ValueGeneratedOnAdd(); // temporary keys for new rows (written by InventorySql)
        e.ToSqlQuery("""
            SELECT b.BatchID - 2000000000 AS Id, b.GroupID, b.MaterialID, b.LocationID, b.BatchNum AS BatchNumber,
                   ISNULL(TRY_CAST(b.BatchQty + ISNULL(c.Net, 0) AS decimal(18,4)), 0) AS Quantity,
                   CAST('Opening balance' AS nvarchar(400)) AS Reason,
                   CAST(NULL AS nvarchar(400)) AS CustomerName, CAST(NULL AS int) AS FormulaID, b.CreatedBy,
                   ISNULL(DATEADD(second, -1, c.FirstDate), CAST('2021-01-01' AS datetime)) AS CreatedAt
            FROM dbo.MaterialBatches b
            LEFT JOIN (SELECT BatchID, SUM(CASE WHEN ABS(QuantityChange) >= 100000000000000 THEN 0 WHEN isDispense = 1 THEN QuantityChange ELSE -QuantityChange END) AS Net,
                              MIN(DateDispensed) AS FirstDate
                       FROM dbo.MaterialQuantityChanges GROUP BY BatchID) c ON c.BatchID = b.BatchID
            UNION ALL
            SELECT q.MaterialDispensedID, q.GroupID, q.MaterialID, q.LocationID, b.BatchNum,
                   ISNULL(TRY_CAST(CASE WHEN q.isDispense = 1 THEN -q.QuantityChange ELSE q.QuantityChange END AS decimal(18,4)), 0),
                   CAST(CASE WHEN q.isDispense = 0 THEN 'Added' WHEN q.FormulaID IS NULL THEN 'Dispensed' ELSE CONCAT('Dispensed: formula #', q.FormulaID) END AS nvarchar(400)),
                   CAST(NULL AS nvarchar(400)), q.FormulaID, q.CreatedBy, q.DateDispensed
            FROM dbo.MaterialQuantityChanges q LEFT JOIN dbo.MaterialBatches b ON b.BatchID = q.BatchID
            """);
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        e.Property(x => x.MaterialId).HasColumnName("MaterialID");
        e.Property(x => x.LocationId).HasColumnName("LocationID");
        e.Property(x => x.FormulaId).HasColumnName("FormulaID");
    });

    /// <summary>Old-site stock change: an existing batch gets BatchQty += q and a change row; a new batch starts at q (no change row).</summary>
    private static (string, SqlParameter[]) InventorySql(InventoryTransaction t, int userId) => (
        """
        DECLARE @b int = (SELECT TOP 1 BatchID FROM dbo.MaterialBatches WHERE MaterialID = @m AND BatchNum = @num ORDER BY BatchID DESC);
        IF @b IS NULL
            INSERT INTO dbo.MaterialBatches (MaterialID, BatchNum, GroupID, BatchQty, CreatedBy, LocationID) VALUES (@m, @num, @g, @q, @u, @loc);
        ELSE
        BEGIN
            UPDATE dbo.MaterialBatches SET BatchQty = BatchQty + @q, LocationID = ISNULL(@loc, LocationID) WHERE BatchID = @b;
            INSERT INTO dbo.MaterialQuantityChanges (MaterialID, BatchID, GroupID, FormulaID, CreatedBy, QuantityChange, DateDispensed, LocationID, isDispense)
            VALUES (@m, @b, @g, @f, @u, ABS(@q), GETDATE(), @loc, CASE WHEN @q < 0 THEN 1 ELSE 0 END);
        END
        """,
        [P("@m", t.MaterialId), P("@num", (t.BatchNumber ?? "").Trim()), P("@g", t.GroupId), P("@q", (double)t.Quantity),
         P("@u", t.CreatedBy != 0 ? t.CreatedBy : userId), P("@loc", t.LocationId), P("@f", t.FormulaId)]);

    /// <summary>The ingredient's chosen batch (FormulationMaterialBatch), replaced after the ingredient is saved.</summary>
    private static (string, SqlParameter[]) BatchLinkSql(int formulaId, int materialId, string? batch) => (
        """
        DELETE FROM dbo.FormulationMaterialBatch WHERE FormulationID = @f AND MaterialID = @m;
        IF ISNULL(@num, '') <> ''
            INSERT INTO dbo.FormulationMaterialBatch (FormulationID, MaterialID, BatchID)
            SELECT TOP 1 @f, @m, b.BatchID FROM dbo.MaterialBatches b WHERE b.MaterialID = @m AND b.BatchNum = @num ORDER BY b.BatchID DESC;
        """,
        [P("@f", formulaId), P("@m", materialId), P("@num", batch?.Trim())]);

    // ------------------------------------------------------------------ documents

    private static readonly (string Type, string Table, string Key, string Owner)[] LinkTables =
    [
        (LinkEntityTypes.Material, "MaterialDocs", "MaterialDocID", "MaterialID"),
        (LinkEntityTypes.Formula, "FormulaDocs", "FormulaDocID", "FormulaID"),
        (LinkEntityTypes.ProcessStep, "ProcessStepDocs", "ProcessStepDocID", "ProcessStepID"),
        (LinkEntityTypes.ProcessSchedule, "ProcessScheduleDocs", "ProcessScheduleDocID", "ProcessScheduleID"),
    ];

    private static void Documents(ModelBuilder b)
    {
        b.Entity<Document>(e =>
        {
            // Old files live on the old server (AppData/Documents/{group}/{DocFile}); like the legacy import they are expected
            // under uploads/documents/{group}/legacy/. Files uploaded here keep their own relative path in DocFile.
            e.ToTable("Documents", "dbo");
            e.ToSqlQuery("""
                SELECT d.DocID, ISNULL(d.GroupID, 0) AS GroupID,
                       ISNULL(NULLIF(LTRIM(RTRIM(d.DocName)), ''), ISNULL(NULLIF(d.DocFile, ''), CONCAT('Document ', d.DocID))) AS DocName,
                       CASE WHEN d.DocFile LIKE 'documents/%' THEN RIGHT(d.DocFile, CHARINDEX('/', REVERSE(d.DocFile)) - 1) ELSE d.DocFile END AS FileName,
                       CASE WHEN ISNULL(d.DocFile, '') = '' THEN NULL WHEN d.DocFile LIKE 'documents/%' THEN d.DocFile
                            ELSE CONCAT('documents/', d.GroupID, '/legacy/', d.DocFile) END AS DocFile,
                       CAST(NULL AS nvarchar(200)) AS ContentType, CAST(0 AS bigint) AS FileSize, d.CreatedBy,
                       ISNULL(d.CreatedDate, CAST('2000-01-01' AS datetime)) AS CreatedDate, d.UpdatedDate
                FROM dbo.Documents d
                """);
            e.Property(x => x.Id).HasColumnName("DocID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.Name).HasColumnName("DocName");
            e.Property(x => x.StoredFile).HasColumnName("DocFile");
            e.Property(x => x.CreatedAt).HasColumnName("CreatedDate");
            e.Property(x => x.UpdatedAt).HasColumnName("UpdatedDate");
            ReadOnly(e.Property(x => x.FileName));    // derived from DocFile
            ReadOnly(e.Property(x => x.ContentType)); // not stored by the old site (served by extension anyway)
            ReadOnly(e.Property(x => x.FileSize));
            Max(e.Property(x => x.Name), 255);
            Max(e.Property(x => x.StoredFile), 255);
        });

        b.Entity<DocumentLink>(e =>
        {
            // Four legacy link tables; Id = row id * 4 + table index. Material links of formulations are Formula links.
            // Changes are written by LinkSql / UnlinkSql.
            e.ToTable((string?)null);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.ToSqlQuery("""
                SELECT md.MaterialDocID * 4 AS Id, md.DocID AS DocumentId,
                       CAST(CASE WHEN fm.ID IS NULL THEN 'Material' ELSE 'Formula' END AS nvarchar(400)) AS EntityType, md.MaterialID AS EntityId
                FROM dbo.MaterialDocs md LEFT JOIN dbo.Materials fm ON fm.ID = md.MaterialID AND fm.Discriminator = 'Formulation'
                WHERE md.DocID IS NOT NULL AND md.MaterialID IS NOT NULL
                UNION ALL
                SELECT fd.FormulaDocID * 4 + 1, fd.DocID, CAST('Formula' AS nvarchar(400)), fd.FormulaID
                FROM dbo.FormulaDocs fd WHERE fd.DocID IS NOT NULL AND fd.FormulaID IS NOT NULL
                UNION ALL
                SELECT pd.ProcessStepDocID * 4 + 2, pd.DocID, CAST('ProcessStep' AS nvarchar(400)), pd.ProcessStepID
                FROM dbo.ProcessStepDocs pd WHERE pd.DocID IS NOT NULL AND pd.ProcessStepID IS NOT NULL
                UNION ALL
                SELECT sd.ProcessScheduleDocID * 4 + 3, sd.DocID, CAST('ProcessSchedule' AS nvarchar(400)), sd.ProcessScheduleID
                FROM dbo.ProcessScheduleDocs sd WHERE sd.DocID IS NOT NULL AND sd.ProcessScheduleID IS NOT NULL
                """);
        });
    }

    private static (string, SqlParameter[]) LinkSql(string type, int entityId, int documentId, int userId)
    {
        var t = LinkTables.First(x => x.Type == type);
        return ($"INSERT INTO dbo.{t.Table} ({t.Owner}, DocID, CreatedBy, CreatedDate) VALUES (@e, @d, @u, GETDATE())",
            [P("@e", entityId), P("@d", documentId), P("@u", userId)]);
    }

    private static (string, SqlParameter[]) UnlinkSql(int linkId)
    {
        var t = LinkTables[linkId % 4];
        return ($"DELETE FROM dbo.{t.Table} WHERE {t.Key} = @id", [P("@id", linkId / 4)]);
    }

    // ------------------------------------------------------------------ dispensing

    private static void Dispensing(ModelBuilder b)
    {
        b.Entity<DispenseSetting>(e =>
        {
            e.ToTable("GroupDispenseTimeSpanMapping", "dbo");
            e.ToSqlQuery("""
                SELECT t.Id, ISNULL(t.GroupId, 0) AS GroupId, NULLIF(t.DespenseTimeSpan, 0) AS DespenseTimeSpan,
                       ISNULL(t.IsCleanNozzle, 0) AS IsCleanNozzle, CAST(NULL AS datetime) AS LastDispensedAt
                FROM dbo.GroupDispenseTimeSpanMapping t
                """);
            e.Property(x => x.CleanNozzleHours).HasColumnName("DespenseTimeSpan");
            e.Property(x => x.IsNozzleCleaned).HasColumnName("IsCleanNozzle");
            ReadOnly(e.Property(x => x.LastDispensedAt)); // the app falls back to the last dispense in the inventory ledger
        });

        b.Entity<FormulaDevicePreference>(e =>
        {
            // FormulationScales + FormulationPrinters (one row each per user and formula). Changes: PreferenceSql.
            e.ToTable((string?)null);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.ToSqlQuery("""
                SELECT CAST(ROW_NUMBER() OVER (ORDER BY x.UserID, x.MaterialID) AS int) AS Id, x.UserID AS UserId, x.MaterialID AS FormulaId,
                       x.ScaleId AS ScaleDeviceId, x.PrinterId AS PrinterDeviceId, CAST('2000-01-01' AS datetime) AS UpdatedAt
                FROM (SELECT COALESCE(s.UserID, p.UserID) AS UserID, COALESCE(s.MaterialID, p.MaterialID) AS MaterialID, s.ScaleId, p.PrinterId
                      FROM (SELECT UserID, MaterialID, MAX(LastUsedScaleDeviceID) AS ScaleId FROM dbo.FormulationScales
                            WHERE UserID IS NOT NULL AND MaterialID IS NOT NULL GROUP BY UserID, MaterialID) s
                      FULL OUTER JOIN (SELECT UserID, MaterialID, MAX(LastUsedPrinterDeviceID) AS PrinterId FROM dbo.FormulationPrinters
                            WHERE UserID IS NOT NULL AND MaterialID IS NOT NULL GROUP BY UserID, MaterialID) p
                        ON p.UserID = s.UserID AND p.MaterialID = s.MaterialID) x
                """);
        });

        b.Entity<FormulaDispenseSnapshot>(e =>
        {
            e.ToTable("DispenseFormulationMaterials", "dbo");
            e.ToSqlQuery("""
                SELECT s.Id, ISNULL(s.FormulationID, 0) AS FormulationID, ISNULL(s.FormulationMaterialsId, 0) AS FormulationMaterialsId,
                       ISNULL(s.Grams, 0) AS Grams, ISNULL(s.DispenseAmmount, 0) AS DispenseAmmount, ISNULL(s.TotalDispensedAmmount, 0) AS TotalDispensedAmmount,
                       ISNULL(s.isDispensed, 0) AS isDispensed, ISNULL(CAST(s.UpdateDt AS datetime), GETUTCDATE()) AS UpdateDt, ISNULL(s.ColourID, 0) AS ColourID
                FROM dbo.DispenseFormulationMaterials s
                """);
            e.Property(x => x.FormulaId).HasColumnName("FormulationID");
            e.Property(x => x.IngredientId).HasColumnName("FormulationMaterialsId");
            Float(e.Property(x => x.Grams));
            Float(e.Property(x => x.DispenseAmount).HasColumnName("DispenseAmmount"));
            Float(e.Property(x => x.DispensedGrams).HasColumnName("TotalDispensedAmmount"));
            e.Property(x => x.IsDispensed).HasColumnName("isDispensed");
            ReadOnly(e.Property(x => x.CreatedAt).HasColumnName("UpdateDt")); // date column with a default
            e.Property<int>("ColourID"); // the ingredient's material, filled on insert
        });

        // Purges run on the machines through the old site: history and settings are shown, not changed, from here.
        const string purges = "Changing purge settings or running purges";
        b.Entity<PurgeSetting>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, purges);
            e.ToSqlQuery("""
                SELECT CAST(ROW_NUMBER() OVER (ORDER BY d.ID) AS int) AS Id, d.ID AS BridgeDeviceId,
                       ISNULL(p.FromDate, CAST('2000-01-01' AS date)) AS FromDate, ISNULL(p.ToDate, CAST('2000-01-01' AS date)) AS ToDate,
                       ISNULL(p.[Time], CAST('00:00' AS time)) AS [Time], p.CreatedBy, ISNULL(p.CreatedDate, CAST('2000-01-01' AS datetime)) AS CreatedAt,
                       p.UpdatedBy, p.UpdatedDate AS UpdatedAt
                FROM dbo.PurgeSettings p JOIN dbo.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier)
                """);
        });
        b.Entity<PurgeFailure>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, purges);
            e.ToSqlQuery("""
                SELECT CAST(ROW_NUMBER() OVER (ORDER BY p.CreatedDate) AS int) AS Id, d.ID AS BridgeDeviceId, ISNULL(p.CanisterNumber, 0) AS CanisterNumber,
                       CAST(p.Message AS nvarchar(400)) AS Message, ISNULL(p.IsActive, 0) AS IsActive, ISNULL(p.CreatedDate, CAST('2000-01-01' AS datetime)) AS CreatedAt
                FROM dbo.PurgeFailed p JOIN dbo.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier)
                """);
        });
        b.Entity<PurgeSuccess>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, purges);
            e.ToSqlQuery("""
                SELECT CAST(ROW_NUMBER() OVER (ORDER BY p.CreatedDate) AS int) AS Id, d.ID AS BridgeDeviceId, ISNULL(p.CanisterNumber, 0) AS CanisterNumber,
                       CAST(p.Message AS nvarchar(400)) AS Message, CAST(p.PurgeType AS nvarchar(400)) AS PurgeType,
                       ISNULL(p.ExecuedDate, ISNULL(p.CreatedDate, CAST('2000-01-01' AS datetime))) AS ExecutedAt,
                       ISNULL(p.CreatedDate, CAST('2000-01-01' AS datetime)) AS CreatedAt
                FROM dbo.PurgeSucess p JOIN dbo.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier)
                """);
        });

        // Machine jobs (dispense, weigh, tare, print): the machines are connected to the old site, which has no job table.
        b.Entity<DeviceCommand>(e =>
        {
            e.ToTable((string?)null);
            e.HasAnnotation(ReadOnlyEntity, "Sending jobs to dispense machines, scales and label printers from this app");
            e.ToSqlQuery("""
                SELECT TOP 0 CAST(0 AS int) AS Id, CAST(0 AS int) AS GroupId, CAST(0 AS int) AS DeviceId, CAST(0 AS int) AS BridgeDeviceId,
                       CAST('' AS nvarchar(400)) AS CommandType, CAST(NULL AS nvarchar(max)) AS Payload, CAST('' AS nvarchar(400)) AS Status,
                       CAST(NULL AS nvarchar(4000)) AS ResultMessage, CAST(NULL AS nvarchar(4000)) AS ResultData, CAST(NULL AS int) AS FormulaId,
                       CAST(NULL AS int) AS CreatedBy, CAST(NULL AS datetime2) AS CreatedAt, CAST(NULL AS datetime2) AS SentAt,
                       CAST(NULL AS datetime2) AS CompletedAt, CAST(NULL AS datetime2) AS ExpiresAt
                """);
        });
    }

    private static (string, SqlParameter[]) PreferenceSql(int userId, int formulaId, int? scale, int? printer, bool delete) => (
        delete
            ? """
              DELETE FROM dbo.FormulationScales WHERE UserID = @u AND MaterialID = @f;
              DELETE FROM dbo.FormulationPrinters WHERE UserID = @u AND MaterialID = @f;
              """
            : """
              IF EXISTS (SELECT 1 FROM dbo.FormulationScales WHERE UserID = @u AND MaterialID = @f)
                  UPDATE dbo.FormulationScales SET LastUsedScaleDeviceID = @s WHERE UserID = @u AND MaterialID = @f;
              ELSE IF @s IS NOT NULL
                  INSERT INTO dbo.FormulationScales (UserID, MaterialID, LastUsedScaleDeviceID) VALUES (@u, @f, @s);
              IF EXISTS (SELECT 1 FROM dbo.FormulationPrinters WHERE UserID = @u AND MaterialID = @f)
                  UPDATE dbo.FormulationPrinters SET LastUsedPrinterDeviceID = @p WHERE UserID = @u AND MaterialID = @f;
              ELSE IF @p IS NOT NULL
                  INSERT INTO dbo.FormulationPrinters (UserID, MaterialID, LastUsedPrinterDeviceID) VALUES (@u, @f, @p);
              """,
        [P("@u", userId), P("@f", formulaId), P("@s", scale), P("@p", printer)]);

    // ------------------------------------------------------------------ save hooks

    /// <summary>
    /// Entities with no table of their own on the old database: the change becomes SQL on the old tables and the entry is
    /// detached (and removed from its parent's collection) so EF does not try to save it. Returns false for normal entities.
    /// </summary>
    private static bool Virtual(DbContext db, EntityEntry entry, int userId, string label, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case InventoryTransaction t when entry.State == EntityState.Added:
                side.Add(new SideWrite(true, () => InventorySql(t, userId)));
                break;
            case InventoryTransaction:
                throw ApiException.Bad("Inventory history can't be changed; record a correcting adjustment instead.");

            case DocumentLink link:
                if (entry.State is EntityState.Deleted or EntityState.Modified)
                {
                    var storedId = (int)entry.Property(nameof(DocumentLink.Id)).OriginalValue!;
                    side.Add(new SideWrite(false, () => UnlinkSql(storedId)));
                }
                if (entry.State is EntityState.Added or EntityState.Modified)
                {
                    var type = LinkTables.Select(x => x.Type).FirstOrDefault(x => string.Equals(x, link.EntityType, StringComparison.OrdinalIgnoreCase))
                               ?? throw ApiException.Bad($"Documents can't be linked to {link.EntityType} on the {label} database yet.");
                    var doc = db.ChangeTracker.Entries<Document>().Select(d => d.Entity).FirstOrDefault(d => d.Links.Contains(link));
                    var entityId = link.EntityId;
                    side.Add(new SideWrite(true, () => LinkSql(type, entityId, doc?.Id ?? link.DocumentId, userId)));
                }
                foreach (var d in db.ChangeTracker.Entries<Document>()) d.Entity.Links.Remove(link);
                break;

            case FormulaDevicePreference p:
                var u = (int)entry.Property(nameof(p.UserId)).OriginalValue!;
                var f = (int)entry.Property(nameof(p.FormulaId)).OriginalValue!;
                if (entry.State == EntityState.Modified && (u != p.UserId || f != p.FormulaId))
                    side.Add(new SideWrite(true, () => PreferenceSql(u, f, null, null, delete: true)));
                var (scale, printer, remove) = (p.ScaleDeviceId, p.PrinterDeviceId, entry.State == EntityState.Deleted);
                side.Add(new SideWrite(true, () => PreferenceSql(remove ? u : p.UserId, remove ? f : p.FormulaId, scale, printer, remove)));
                break;

            default:
                return VirtualProcess(db, entry, userId, label, side);
        }
        entry.State = EntityState.Detached;
        return true;
    }

    /// <summary>Extra old-table rows that go with a normal save.</summary>
    private static void Related(EntityEntry entry, List<SideWrite> side)
    {
        switch (entry.Entity)
        {
            case Formula f when entry.State == EntityState.Added:
                // The old site records every new formula in dbo.FormulaCreated; the formula is its own material.
                side.Add(new SideWrite(true,
                    () => ("INSERT INTO dbo.FormulaCreated (MaterialsId, GroupId, CreatedDate) VALUES (@id, @g, GETDATE())", [P("@id", f.Id), P("@g", f.GroupId)]),
                    () =>
                    {
                        // Original value too: marking a property unmodified reverts it to the original value.
                        var p = entry.Property(nameof(Formula.MaterialId));
                        p.CurrentValue = f.Id;
                        p.OriginalValue = f.Id;
                        p.IsModified = false;
                    }));
                break;
            case FormulaIngredient i when (entry.State == EntityState.Added && !string.IsNullOrWhiteSpace(i.BatchNumber))
                                          || (entry.State == EntityState.Modified && entry.Property(nameof(FormulaIngredient.BatchNumber)).IsModified):
                side.Add(new SideWrite(true, () => BatchLinkSql(i.FormulaId, i.MaterialId, i.BatchNumber)));
                break;
        }
    }

    private static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
}
