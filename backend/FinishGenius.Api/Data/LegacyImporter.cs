using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

/// <summary>
/// Copies the legacy Finish Genius database (FGAPP) into the "fg" schema of the configured database.
///
/// * Runs server-side with cross-database INSERT … SELECT, so the legacy database must be on the same SQL Server
///   as the target database and readable by the configured login.
/// * Legacy primary keys are preserved (IDENTITY_INSERT) so ids match the old app.
/// * Re-runnable: every run first DELETES all rows in the fg schema of the target database (never touches other schemas).
/// * Uploaded files (documents, photos) are not in the database; their paths point to
///   uploads/documents/{groupId}/legacy/{file} and uploads/photos/{groupId}/legacy/{file} — copy the legacy upload
///   folders there to make them available.
/// </summary>
public class LegacyImporter(AppDbContext db, IConfiguration config, ILogger log)
{
    private string _s = "";           // "[FGAPP].dbo."
    private DbConnection _conn = null!;

    // Tables in delete order (children first).
    private static readonly string[] WipeOrder =
    [
        "DeviceCommands", "FormulaDevicePreferences", "FormulaDispenseSnapshots", "DispenseSettings", "PurgeSettings", "PurgeFailures", "PurgeSuccesses",
        "WorkLineChecks", "WorkExecutionLines", "WorkExecutionDefects", "WorkExecutionAdders", "WorkExecutions", "DefectTypes", "AdderTypes",
        "WorkInstructionMedia", "WorkInstructionSteps", "WorkInstructionTrails", "WorkInstructionRelatedDocs", "WorkInstructionSignatures",
        "WorkInstructionItems", "WorkInstructions",
        "ScheduleStepOverrides", "ProcessScheduleSteps", "ProcessSchedules", "ProcessStepValues", "ProcessStepEntries", "ProcessSteps",
        "SubStepPullDowns", "SubSteps", "IndustrySectors",
        "DocumentLinks", "Documents", "PhotoTags", "Photos",
        "DeviceMetrics", "DeviceCanisters", "Devices",
        "PurchaseOrderLines", "PurchaseOrders", "InventoryTransactions", "MaterialLocations",
        "FormulaIngredients", "Formulas", "Materials", "Characteristics", "MaterialCategories", "Vendors",
        "MessageRecipients", "Messages", "AuditLogs", "Departments", "UserGroups", "UserRoles", "Users", "Groups",
    ];

    public async Task RunAsync(string sourceDb, bool confirmed)
    {
        if (!Regex.IsMatch(sourceDb, @"^[A-Za-z0-9_\-]+$")) throw new ArgumentException("Invalid source database name.");
        // The source may be the target database itself (legacy tables in dbo, new app in fg): only fg is ever written.
        var target = db.Database.GetDbConnection().Database;

        Console.WriteLine($"Legacy import: [{sourceDb}].dbo -> [{target}].fg");
        if (!confirmed)
        {
            Console.WriteLine("This DELETES all Finish Genius data (schema fg) in the target database and re-imports it from the source.");
            Console.WriteLine("Re-run with --yes to continue.");
            return;
        }

        _s = $"[{sourceDb}].dbo.";
        await db.Database.OpenConnectionAsync(); // keeps #temp tables alive across steps
        _conn = db.Database.GetDbConnection();
        var total = Stopwatch.StartNew();
        try
        {
            await Exec("Check source", $"SELECT TOP 1 1 FROM {_s}Groups");
            await Exec("Wipe fg schema", "UPDATE fg.Devices SET NetworkBridgeId = NULL;\n" +
                                         string.Join("\n", WipeOrder.Select(t => $"DELETE FROM fg.[{t}];")));
            await ImportCore();
            await ImportCatalog();
            await ImportProcess();
            await ImportFilesAndDevices();
            await ImportMyWork();
            await ImportWorkInstructionsAndMessages();
            await ImportFormulaExtrasAsync();
            await EnsureAdminAsync();
            await ReportAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        Console.WriteLine($"Legacy import finished in {total.Elapsed:mm\\:ss}.");
    }

    /// <summary>
    /// <c>import-legacy-formulas</c>: additive, re-runnable import of the formula workspace data only (no wipe). Fills new
    /// columns where they are still empty, inserts rows that do not exist yet, and corrects the Status / Container Type
    /// mapping of formulas that were never edited in the new app.
    /// </summary>
    public async Task RunFormulaExtrasAsync(string sourceDb, bool confirmed)
    {
        if (!Regex.IsMatch(sourceDb, @"^[A-Za-z0-9_\-]+$")) throw new ArgumentException("Invalid source database name.");
        var target = db.Database.GetDbConnection().Database;
        Console.WriteLine($"Legacy formula extras import: [{sourceDb}].dbo -> [{target}].fg (additive, nothing is deleted)");
        if (!confirmed)
        {
            Console.WriteLine("Imports: formula status/container fix, batch type, employee, PO #, mixed on, dispenser, ingredient dispense amounts and batches,");
            Console.WriteLine("material colour codes, scale/printer preferences, clean-nozzle settings, purge settings/history and the formula View History.");
            Console.WriteLine("Re-run with --yes to continue.");
            return;
        }
        _s = $"[{sourceDb}].dbo.";
        await db.Database.OpenConnectionAsync();
        _conn = db.Database.GetDbConnection();
        var total = Stopwatch.StartNew();
        try
        {
            await Exec("Check source", $"SELECT TOP 1 1 FROM {_s}Materials");
            await ImportFormulaExtrasAsync();
            Console.WriteLine("\nRow counts:");
            foreach (var (label, sql) in new[]
                     {
                         ("Formulas (Complete)", "SELECT COUNT_BIG(*) FROM fg.Formulas WHERE IsComplete = 1"),
                         ("Formulas with employee", "SELECT COUNT_BIG(*) FROM fg.Formulas WHERE EmployeeName IS NOT NULL"),
                         ("Ingredients with a batch", "SELECT COUNT_BIG(*) FROM fg.FormulaIngredients WHERE BatchNumber IS NOT NULL"),
                         ("Ingredients to dispense", "SELECT COUNT_BIG(*) FROM fg.FormulaIngredients WHERE DispenseAmount > 0"),
                         ("Materials with colour", "SELECT COUNT_BIG(*) FROM fg.Materials WHERE ColorCode IS NOT NULL"),
                         ("FormulaDevicePreferences", "SELECT COUNT_BIG(*) FROM fg.FormulaDevicePreferences"),
                         ("DispenseSettings", "SELECT COUNT_BIG(*) FROM fg.DispenseSettings"),
                         ("PurgeSettings", "SELECT COUNT_BIG(*) FROM fg.PurgeSettings"),
                         ("PurgeSuccesses", "SELECT COUNT_BIG(*) FROM fg.PurgeSuccesses"),
                         ("PurgeFailures", "SELECT COUNT_BIG(*) FROM fg.PurgeFailures"),
                         ("Formula history rows", "SELECT COUNT_BIG(*) FROM fg.AuditLogs WHERE EntityType = 'Formula'"),
                     })
            {
                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = sql;
                var n = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
                Console.WriteLine($"  {label,-28} {n,12:N0}");
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
        Console.WriteLine($"Legacy formula extras import finished in {total.Elapsed:mm\\:ss}.");
    }

    // ------------------------------------------------------------------ formula workspace extras (additive)

    private const string ContainerCase = "CASE m.ContainerType WHEN 1 THEN '1 Gallon' WHEN 2 THEN '5 Gallons' WHEN 3 THEN 'Drum' WHEN 4 THEN 'Quartz' END";

    private async Task ImportFormulaExtrasAsync()
    {
        var s = _s;
        // Legacy StatusEnum: Complete = 1, Incomplete = 2; ContainerTypesEnum: 1 Gallon, 5 Gallons, Drum, Quartz.
        await Exec("Formulas: status & container type", $"""
            UPDATE f SET IsComplete = CASE WHEN m.Status = 1 THEN 1 ELSE 0 END, ContainerType = {ContainerCase}
            FROM fg.Formulas f JOIN {s}Materials m ON m.ID = f.Id AND m.Discriminator = 'Formulation'
            WHERE f.UpdatedAt IS NULL
              AND (f.IsComplete <> CASE WHEN m.Status = 1 THEN 1 ELSE 0 END OR ISNULL(f.ContainerType, '') <> ISNULL({ContainerCase}, ''));
            """);

        await Exec("Formulas: batch type, employee, PO, mixed on", $"""
            UPDATE f SET
                BatchType = CASE WHEN m.BatchType BETWEEN 1 AND 5 THEN m.BatchType ELSE 2 END,
                BatchValue = CASE WHEN ISNULL(m.BatchValue, 0) > 0 THEN {D("m.BatchValue")}
                                  WHEN m.BatchType = 4 THEN {D("m.GramsInBatch / 1000.0")}
                                  WHEN ISNULL(m.BatchType, 2) = 2 THEN {D("m.GramsInBatch")} ELSE f.BatchValue END,
                EmployeeName = ISNULL(f.EmployeeName, LEFT(NULLIF(LTRIM(RTRIM(m.EmployeeName)), ''), 400)),
                PurchaseOrderNumber = ISNULL(f.PurchaseOrderNumber, LEFT(NULLIF(LTRIM(RTRIM(m.PurchaseOrderNumber)), ''), 400)),
                MixedOn = ISNULL(f.MixedOn, m.MixedOn),
                DispenserId = ISNULL(f.DispenserId, d.Id)
            FROM fg.Formulas f
            JOIN {s}Materials m ON m.ID = f.Id AND m.Discriminator = 'Formulation'
            LEFT JOIN fg.Devices d ON d.Id = m.DispenserID AND d.DeviceType = 7
            WHERE f.UpdatedAt IS NULL
              AND (f.BatchType <> CASE WHEN m.BatchType BETWEEN 1 AND 5 THEN m.BatchType ELSE 2 END
                   OR (f.BatchValue = 0 AND (ISNULL(m.BatchValue, 0) > 0 OR ISNULL(m.GramsInBatch, 0) > 0))
                   OR (f.EmployeeName IS NULL AND NULLIF(LTRIM(RTRIM(m.EmployeeName)), '') IS NOT NULL)
                   OR (f.PurchaseOrderNumber IS NULL AND NULLIF(LTRIM(RTRIM(m.PurchaseOrderNumber)), '') IS NOT NULL)
                   OR (f.MixedOn IS NULL AND m.MixedOn IS NOT NULL)
                   OR (f.DispenserId IS NULL AND d.Id IS NOT NULL));
            """);

        // Rows are matched like the main import numbered them (Sequence = ROW_NUMBER by legacy id per formula).
        await Exec("Ingredients: dispense amounts & batches", $"""
            WITH src AS (
                SELECT fm.FormulationID, fm.ColourID, fm.DispenseAmmount, fm.TotalDispensedAmmount, fm.isDispensed,
                       ROW_NUMBER() OVER (PARTITION BY fm.FormulationID ORDER BY fm.ID) AS Seq
                FROM {s}FormulationMaterials fm
                JOIN fg.Formulas f ON f.Id = fm.FormulationID
                JOIN fg.Materials m ON m.Id = fm.ColourID),
            bat AS (
                SELECT fmb.FormulationID, fmb.MaterialID, MAX(b.BatchNum) AS BatchNum
                FROM {s}FormulationMaterialBatch fmb JOIN {s}MaterialBatches b ON b.BatchID = fmb.BatchID
                GROUP BY fmb.FormulationID, fmb.MaterialID)
            UPDATE i SET
                DispenseAmount = {D("src.DispenseAmmount")},
                DispensedGrams = {D("src.TotalDispensedAmmount")},
                IsDispensed = ISNULL(src.isDispensed, 0),
                BatchNumber = ISNULL(i.BatchNumber, LEFT(NULLIF(LTRIM(RTRIM(bat.BatchNum)), ''), 400))
            FROM fg.FormulaIngredients i
            JOIN fg.Formulas f ON f.Id = i.FormulaId AND f.UpdatedAt IS NULL
            JOIN src ON src.FormulationID = i.FormulaId AND src.Seq = i.Sequence AND src.ColourID = i.MaterialId
            LEFT JOIN bat ON bat.FormulationID = i.FormulaId AND bat.MaterialID = i.MaterialId
            WHERE i.DispenseAmount <> {D("src.DispenseAmmount")} OR i.DispensedGrams <> {D("src.TotalDispensedAmmount")}
               OR (i.BatchNumber IS NULL AND NULLIF(LTRIM(RTRIM(bat.BatchNum)), '') IS NOT NULL);
            """);

        await Exec("Materials: colour codes", $"""
            UPDATE m SET ColorCode = LEFT(LTRIM(RTRIM(lm.ColorCode)), 400)
            FROM fg.Materials m JOIN {s}Materials lm ON lm.ID = m.Id
            WHERE m.ColorCode IS NULL AND NULLIF(LTRIM(RTRIM(lm.ColorCode)), '') IS NOT NULL;
            """);

        await Exec("Scale / printer preferences", $"""
            WITH sc AS (SELECT UserID, MaterialID, MAX(LastUsedScaleDeviceID) AS ScaleId FROM {s}FormulationScales
                        WHERE UserID IS NOT NULL AND MaterialID IS NOT NULL GROUP BY UserID, MaterialID),
                 pr AS (SELECT UserID, MaterialID, MAX(LastUsedPrinterDeviceID) AS PrinterId FROM {s}FormulationPrinters
                        WHERE UserID IS NOT NULL AND MaterialID IS NOT NULL GROUP BY UserID, MaterialID),
                 x AS (SELECT COALESCE(sc.UserID, pr.UserID) AS UserId, COALESCE(sc.MaterialID, pr.MaterialID) AS FormulaId, sc.ScaleId, pr.PrinterId
                       FROM sc FULL OUTER JOIN pr ON pr.UserID = sc.UserID AND pr.MaterialID = sc.MaterialID)
            INSERT INTO fg.FormulaDevicePreferences (UserId, FormulaId, ScaleDeviceId, PrinterDeviceId, UpdatedAt)
            SELECT x.UserId, x.FormulaId, ds.Id, dp.Id, SYSUTCDATETIME()
            FROM x
            JOIN fg.Users u ON u.Id = x.UserId
            JOIN fg.Formulas f ON f.Id = x.FormulaId
            LEFT JOIN fg.Devices ds ON ds.Id = x.ScaleId AND ds.DeviceType IN (2, 4)
            LEFT JOIN fg.Devices dp ON dp.Id = x.PrinterId AND dp.DeviceType = 5
            WHERE (ds.Id IS NOT NULL OR dp.Id IS NOT NULL)
              AND NOT EXISTS (SELECT 1 FROM fg.FormulaDevicePreferences p WHERE p.UserId = x.UserId AND p.FormulaId = x.FormulaId);
            """);

        await Exec("Clean nozzle settings", $"""
            INSERT INTO fg.DispenseSettings (GroupId, CleanNozzleHours, IsNozzleCleaned, LastDispensedAt)
            SELECT t.GroupId, NULLIF(MAX(ISNULL(t.DespenseTimeSpan, 0)), 0), CAST(MAX(CASE WHEN t.IsCleanNozzle = 1 THEN 1 ELSE 0 END) AS bit), NULL
            FROM {s}GroupDispenseTimeSpanMapping t JOIN fg.Groups g ON g.Id = t.GroupId
            WHERE NOT EXISTS (SELECT 1 FROM fg.DispenseSettings d WHERE d.GroupId = t.GroupId)
            GROUP BY t.GroupId;
            """);

        // Purge tables are keyed by the Network Bridge API key; devices kept their legacy keys.
        await Exec("Purge settings", $"""
            INSERT INTO fg.PurgeSettings (BridgeDeviceId, FromDate, ToDate, Time, CreatedBy, CreatedAt, UpdatedBy, UpdatedAt)
            SELECT d.Id, p.FromDate, p.ToDate, p.Time, p.CreatedBy, ISNULL(p.CreatedDate, SYSUTCDATETIME()), p.UpdatedBy, p.UpdatedDate
            FROM {s}PurgeSettings p JOIN fg.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier) AND d.DeviceType = 6
            WHERE NOT EXISTS (SELECT 1 FROM fg.PurgeSettings x WHERE x.BridgeDeviceId = d.Id);
            """);

        await Exec("Purge history (success)", $"""
            INSERT INTO fg.PurgeSuccesses (BridgeDeviceId, CanisterNumber, Message, PurgeType, ExecutedAt, CreatedAt)
            SELECT d.Id, p.CanisterNumber, LEFT(p.Message, 400), LEFT(p.PurgeType, 400), p.ExecuedDate, p.CreatedDate
            FROM {s}PurgeSucess p JOIN fg.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier)
            WHERE NOT EXISTS (SELECT 1 FROM fg.PurgeSuccesses x WHERE x.BridgeDeviceId = d.Id AND x.CanisterNumber = p.CanisterNumber AND x.CreatedAt = p.CreatedDate);
            """);

        await Exec("Purge history (failed)", $"""
            INSERT INTO fg.PurgeFailures (BridgeDeviceId, CanisterNumber, Message, IsActive, CreatedAt)
            SELECT d.Id, p.CanisterNumber, LEFT(p.Message, 400), p.IsActive, p.CreatedDate
            FROM {s}PurgeFailed p JOIN fg.Devices d ON d.ApiKey = TRY_CAST(p.ApiKey AS uniqueidentifier)
            WHERE NOT EXISTS (SELECT 1 FROM fg.PurgeFailures x WHERE x.BridgeDeviceId = d.Id AND x.CanisterNumber = p.CanisterNumber AND x.CreatedAt = p.CreatedDate);
            """);

        await Exec("Formula View History", $"""
            INSERT INTO fg.AuditLogs (GroupId, UserId, UserName, EntityType, EntityId, Action, Details, OldValue, NewValue, CreatedAt)
            SELECT f.GroupId, u.Id, LEFT(lu.Username, 400), 'Formula', h.CategoryId, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(h.Field)), ''), 'History'), 400),
                   LEFT(h.Description, 4000), LEFT(h.OldValue, 400), LEFT(h.NewValue, 400), h.CreatedDate
            FROM {s}History h
            JOIN fg.Formulas f ON f.Id = h.CategoryId
            LEFT JOIN {s}[User] lu ON lu.ID = h.UserId
            LEFT JOIN fg.Users u ON u.Id = h.UserId
            WHERE h.HistoryCategory = 0 AND h.CategoryId > 0
              AND NOT EXISTS (SELECT 1 FROM fg.AuditLogs a WHERE a.EntityType = 'Formula' AND a.EntityId = h.CategoryId AND a.CreatedAt = h.CreatedDate
                              AND a.Action = LEFT(ISNULL(NULLIF(LTRIM(RTRIM(h.Field)), ''), 'History'), 400));
            """);
    }

    private async Task<int> Exec(string name, string sql)
    {
        var sw = Stopwatch.StartNew();
        await using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 3600;
        var rows = await cmd.ExecuteNonQueryAsync();
        Console.WriteLine($"  {name,-38} {(rows >= 0 ? rows.ToString("N0") : "-"),12} rows  {sw.Elapsed.TotalSeconds,6:0.0}s");
        return rows;
    }

    private static string Ident(string table, string insertSql) =>
        $"SET IDENTITY_INSERT fg.[{table}] ON;\n{insertSql}\nSET IDENTITY_INSERT fg.[{table}] OFF;";

    /// <summary>Float/decimal source value clamped into decimal(18,4).</summary>
    private static string D(string expr) =>
        $"CAST(CASE WHEN ABS(ISNULL(CAST({expr} AS float), 0)) < 100000000000000 THEN ISNULL(CAST({expr} AS float), 0) ELSE 0 END AS decimal(18,4))";

    // ------------------------------------------------------------------ groups, users

    private async Task ImportCore()
    {
        var s = _s;
        await Exec("Groups", Ident("Groups", $"""
            INSERT INTO fg.Groups (Id, Name, Address1, Address2, City, State, Zip, Country, TimeZone, ApiKey, LogoFile, ChecklistDeletionEnabled, IsDeleted, CreatedAt, UpdatedAt)
            SELECT g.ID, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(g.Name)), ''), CONCAT('Group ', g.ID)), 400), g.Address1, g.Address2, g.City, g.State, g.Zip, g.Country,
                   -- the old site's "delete group" sets deletionEnabled = 1 and hides the group everywhere
                   LEFT(ISNULL(NULLIF(g.TimeZone, ''), 'Eastern Standard Time'), 400), g.ApiKey, NULL, 0,
                   CASE WHEN g.deletionEnabled = 1 OR g.IsDeleted = 1 THEN 1 ELSE 0 END,
                   ISNULL(g.UpdatedDt, SYSUTCDATETIME()), g.UpdatedDt
            FROM {s}Groups g;
            """));

        await Exec("Users", Ident("Users", $"""
            DECLARE @fb int = ISNULL((SELECT Id FROM fg.Groups WHERE Id = 3070), (SELECT MIN(Id) FROM fg.Groups));
            INSERT INTO fg.Users (Id, GroupId, Email, Username, PasswordHash, FirstName, LastName, PhoneNumber, Disabled, AgreementAccepted,
                                  AgreementAcceptedAt, DefaultGroupId, IsDeleted, CreatedAt, LastLoginAt)
            SELECT u.ID, COALESCE(g1.Id, g2.Id, @fb), LEFT(u.Email, 400), LEFT(u.Username, 400), LEFT(u.Password, 400), LEFT(u.FirstName, 400),
                   LEFT(u.LastName, 400), LEFT(u.PhoneNumber, 400), u.Disabled, 0, NULL, g2.Id, 0, u.CreateDate, NULL
            FROM {s}[User] u
            LEFT JOIN fg.Groups g1 ON g1.Id = u.GroupID
            LEFT JOIN fg.Groups g2 ON g2.Id = u.DefaultGroupID;
            """));

        await Exec("User roles", $"""
            INSERT INTO fg.UserRoles (UserId, Role)
            SELECT DISTINCT r.UserID, CASE r.Role WHEN 'Admin' THEN 'SystemAdmin' ELSE r.Role END
            FROM {s}UserRoles r JOIN fg.Users u ON u.Id = r.UserID
            WHERE r.Role IN ('Admin', 'FGPro', 'FGProPlus', 'GroupAdmin', 'SupportAgent');
            """);

        await Exec("User groups", $"""
            INSERT INTO fg.UserGroups (UserId, GroupId)
            SELECT DISTINCT j.UserID, j.GroupID FROM {s}User_Group_Junction j
            JOIN fg.Users u ON u.Id = j.UserID JOIN fg.Groups g ON g.Id = j.GroupID;
            """);

        await Exec("Departments", Ident("Departments", $"""
            INSERT INTO fg.Departments (Id, GroupId, Name)
            SELECT d.ID, d.GroupId, LEFT(d.Name, 400) FROM {s}Departments d JOIN fg.Groups g ON g.Id = d.GroupId;
            """));

        await Exec("Vendors", Ident("Vendors", $"""
            INSERT INTO fg.Vendors (Id, GroupId, VendorName, Address, City, State, Zip, Country, PaymentTerms, AccountNumber, ContactName,
                                    OfficePhone, MobilePhone, VendorEmail, RequestorEmail, IsDeleted)
            SELECT v.VendorID, v.GroupID, LEFT(v.VendorName, 400), LEFT(v.Address, 400), LEFT(v.City, 400), v.State, v.Zip, v.Country,
                   LEFT(v.PaymentTerms, 400), v.AccountNumber, v.ContactName, v.OfficePhone, v.MobilePhone, v.VendorEmail, v.RequestorEmail, ISNULL(v.IsDeleted, 0)
            FROM {s}Vendors v JOIN fg.Groups g ON g.Id = v.GroupID;
            """));
    }

    // ------------------------------------------------------------------ categories, materials, formulas, inventory

    private async Task ImportCatalog()
    {
        var s = _s;
        await Exec("Material categories (shared)", Ident("MaterialCategories", $"""
            INSERT INTO fg.MaterialCategories (Id, GroupId, Name, MaterialType, Filter1, Filter2)
            SELECT c.ID, NULL, LEFT(LTRIM(RTRIM(c.Name)), 400), CASE WHEN c.MaterialType BETWEEN 1 AND 7 THEN c.MaterialType ELSE 1 END,
                   LEFT(c.Filter1, 400), LEFT(c.Filter2, 400)
            FROM {s}Categories c;
            """));

        await Exec("Characteristics", Ident("Characteristics", $"""
            INSERT INTO fg.Characteristics (Id, CategoryId, Name, Unit, InputType, CalcVariable, DefaultValue, Sequence)
            SELECT ch.ID, ch.CategoryID, LEFT(LTRIM(RTRIM(ISNULL(ch.Name, ''))), 400),
                   NULLIF(LEFT(LTRIM(RTRIM(REPLACE(REPLACE(ch.Unit, CHAR(9), ''), ':', ''))), 400), ''),
                   CASE WHEN ch.PDQuery IS NOT NULL OR ch.CalcVariable LIKE '%[_]ID' THEN 'Material'
                        WHEN ch.CalcVariable IN ('Material_Coverage', 'Material_Qty', 'Step_Labor', 'Step_Setup') OR ch.CalcVariable LIKE 'Misc%[_]Qty' THEN 'Number'
                        WHEN ch.PrintStyle = 3 OR ch.Name LIKE 'Notes%' THEN 'Notes'
                        WHEN TRY_CAST(ch.DefaultVar AS float) IS NOT NULL THEN 'Number'
                        ELSE 'Text' END,
                   CASE WHEN LTRIM(RTRIM(ISNULL(ch.CalcVariable, ''))) IN ('', 'N_A', 'null') THEN NULL ELSE LEFT(LTRIM(RTRIM(ch.CalcVariable)), 400) END,
                   LEFT(ch.DefaultVar, 400), ISNULL(ch.Sequence, ch.ID)
            FROM {s}Characteristics ch JOIN fg.MaterialCategories c ON c.Id = ch.CategoryID;
            """));

        await Exec("Materials (incl. formulations)", Ident("Materials", $"""
            DECLARE @master int = ISNULL((SELECT Id FROM fg.Groups WHERE Id = 1066), (SELECT MIN(Id) FROM fg.Groups));
            INSERT INTO fg.Materials (Id, GroupId, MaterialType, CategoryId, ProductCode, ProductName, Density, Price, Voc, Hap, Tap, MinQuantity,
                                      VendorId, Notes, IsDeleted, CreatedAt, UpdatedAt)
            SELECT m.ID, ISNULL(g.Id, @master),
                   CASE WHEN m.Discriminator = 'Formulation' THEN 6 WHEN m.MaterialType BETWEEN 1 AND 7 THEN m.MaterialType ELSE 1 END,
                   c.Id, LEFT(LTRIM(RTRIM(m.ManufacturersProductCode)), 400),
                   LEFT(ISNULL(NULLIF(LTRIM(RTRIM(m.ManufacturersProductName)), ''), CONCAT('(unnamed #', m.ID, ')')), 400),
                   {D("m.GramsPerCubicCentiMetres")}, {D("ISNULL(m.PricePerGallon, m.FormulaCost)")}, {D("m.VOC")}, {D("m.HAP")}, {D("m.TAP")},
                   {D("m.MinQuantity")}, NULL, LEFT(m.Notes, 4000), ISNULL(m.IsDeleted, 0), ISNULL(m.MixedOn, SYSUTCDATETIME()), NULL
            FROM {s}Materials m
            LEFT JOIN fg.Groups g ON g.Id = m.GroupID
            LEFT JOIN fg.MaterialCategories c ON c.Id = m.CategoryId;
            """));

        await Exec("Formulas", Ident("Formulas", $"""
            DECLARE @master int = ISNULL((SELECT Id FROM fg.Groups WHERE Id = 1066), (SELECT MIN(Id) FROM fg.Groups));
            INSERT INTO fg.Formulas (Id, GroupId, CategoryId, Name, Number, CustomerName, IsComplete, BatchSize, ContainerType, ContainerPrice, MarkUp,
                                     Substrate, Notes, SpinDeltaL, SpinDeltaA, SpinDeltaB, SpinDeltaE, SpexDeltaL, SpexDeltaA, SpexDeltaB, SpexDeltaE,
                                     CreatedBy, IsDeleted, CreatedAt, UpdatedAt)
            SELECT m.ID, ISNULL(g.Id, @master), c.Id,
                   LEFT(ISNULL(NULLIF(LTRIM(RTRIM(m.ManufacturersProductName)), ''), CONCAT('(unnamed #', m.ID, ')')), 400),
                   LEFT(LTRIM(RTRIM(m.ManufacturersProductCode)), 400), LEFT(m.CustomerName, 400), CASE WHEN m.Status = 1 THEN 1 ELSE 0 END,
                   {D("m.GramsInBatch")},
                   {ContainerCase},
                   {D("m.ContainerPrice")}, {D("m.MarkUp")}, LEFT(m.Substrate, 400), LEFT(m.Notes, 4000),
                   TRY_CAST(m.SpinDeltaL AS decimal(18,4)), TRY_CAST(m.SpinDeltaA AS decimal(18,4)), TRY_CAST(m.SpinDeltaB AS decimal(18,4)), TRY_CAST(m.SpinDeltaE AS decimal(18,4)),
                   TRY_CAST(m.SpexDeltaL AS decimal(18,4)), TRY_CAST(m.SpexDeltaA AS decimal(18,4)), TRY_CAST(m.SpexDeltaB AS decimal(18,4)), TRY_CAST(m.SpexDeltaE AS decimal(18,4)),
                   NULL, ISNULL(m.IsDeleted, 0), ISNULL(m.MixedOn, SYSUTCDATETIME()), NULL
            FROM {s}Materials m
            LEFT JOIN fg.Groups g ON g.Id = m.GroupID
            LEFT JOIN fg.MaterialCategories c ON c.Id = m.CategoryId
            WHERE m.Discriminator = 'Formulation';
            """));

        await Exec("Formula ingredients", $"""
            INSERT INTO fg.FormulaIngredients (FormulaId, MaterialId, Grams, DispensedGrams, IsDispensed, Sequence)
            SELECT fm.FormulationID, fm.ColourID, {D("fm.Grams")}, 0, ISNULL(fm.isDispensed, 0),
                   ROW_NUMBER() OVER (PARTITION BY fm.FormulationID ORDER BY fm.ID)
            FROM {s}FormulationMaterials fm
            JOIN fg.Formulas f ON f.Id = fm.FormulationID
            JOIN fg.Materials m ON m.Id = fm.ColourID;
            """);

        await Exec("Material locations", Ident("MaterialLocations", $"""
            DECLARE @master int = ISNULL((SELECT Id FROM fg.Groups WHERE Id = 1066), (SELECT MIN(Id) FROM fg.Groups));
            INSERT INTO fg.MaterialLocations (Id, GroupId, Name, MaterialType, IsDeleted)
            SELECT l.MaterialLocationID, ISNULL(g.Id, @master), LEFT(l.LocationName, 400), CASE WHEN l.MaterialType BETWEEN 1 AND 7 THEN l.MaterialType ELSE 1 END, l.isDeleted
            FROM {s}MaterialLocations l LEFT JOIN fg.Groups g ON g.Id = l.GroupID;
            """));

        // Opening balance per batch = current BatchQty plus everything later dispensed from it, then the dispense history.
        await Exec("Inventory: opening balances", $"""
            WITH chg AS (
                SELECT BatchID, SUM(CASE WHEN isDispense = 1 THEN QuantityChange ELSE -QuantityChange END) AS net, MIN(DateDispensed) AS firstDate
                FROM {s}MaterialQuantityChanges GROUP BY BatchID)
            INSERT INTO fg.InventoryTransactions (GroupId, MaterialId, LocationId, BatchNumber, Quantity, Reason, CustomerName, FormulaId, CreatedBy, CreatedAt)
            SELECT b.GroupID, b.MaterialID, loc.Id, LEFT(b.BatchNum, 400), {D("b.BatchQty + ISNULL(c.net, 0)")}, 'Opening balance (legacy import)',
                   NULL, NULL, b.CreatedBy, ISNULL(DATEADD(second, -1, c.firstDate), '2021-01-01')
            FROM {s}MaterialBatches b
            JOIN fg.Materials m ON m.Id = b.MaterialID
            JOIN fg.Groups g ON g.Id = b.GroupID
            LEFT JOIN fg.MaterialLocations loc ON loc.Id = b.LocationID
            LEFT JOIN chg c ON c.BatchID = b.BatchID;
            """);

        await Exec("Inventory: dispense history", $"""
            INSERT INTO fg.InventoryTransactions (GroupId, MaterialId, LocationId, BatchNumber, Quantity, Reason, CustomerName, FormulaId, CreatedBy, CreatedAt)
            SELECT q.GroupID, q.MaterialID, loc.Id, LEFT(b.BatchNum, 400),
                   {D("CASE WHEN q.isDispense = 1 THEN -q.QuantityChange ELSE q.QuantityChange END")},
                   CASE WHEN q.isDispense = 1 THEN 'Dispensed (legacy)' ELSE 'Adjustment (legacy)' END, NULL, f.Id, q.CreatedBy, q.DateDispensed
            FROM {s}MaterialQuantityChanges q
            JOIN fg.Materials m ON m.Id = q.MaterialID
            JOIN fg.Groups g ON g.Id = q.GroupID
            LEFT JOIN {s}MaterialBatches b ON b.BatchID = q.BatchID
            LEFT JOIN fg.MaterialLocations loc ON loc.Id = q.LocationID
            LEFT JOIN fg.Formulas f ON f.Id = q.FormulaID;
            """);

        await Exec("Purchase orders", Ident("PurchaseOrders", $"""
            INSERT INTO fg.PurchaseOrders (Id, GroupId, VendorId, PoNumber, DeliveryDate, ShipName, ShipAddress1, ShipAddress2, ShipCity, ShipState,
                                           ShipZip, ShipCountry, CreatedBy, CreatedAt)
            SELECT h.POHeaderID, h.GroupID, h.VendorID, LEFT(h.PONum, 400), h.DeliveryDate, h.Name, h.Address1, h.Address2, h.City, h.State, h.Zip,
                   h.Country, h.CreatedBy, h.DateCreated
            FROM {s}POHeaders h JOIN fg.Groups g ON g.Id = h.GroupID JOIN fg.Vendors v ON v.Id = h.VendorID;
            """));

        await Exec("Purchase order lines", $"""
            INSERT INTO fg.PurchaseOrderLines (PurchaseOrderId, MaterialId, Quantity, QuantityType, UnitPrice)
            SELECT d.OrderID, d.MaterialID, {D("d.Quantity")}, LEFT(d.QuantityType, 400), m.Price
            FROM {s}PODetails d JOIN fg.PurchaseOrders o ON o.Id = d.OrderID JOIN fg.Materials m ON m.Id = d.MaterialID;
            """);
    }

    // ------------------------------------------------------------------ sub steps, process steps, schedules

    private async Task ImportProcess()
    {
        var s = _s;
        await Exec("Industry sectors", Ident("IndustrySectors", $"""
            INSERT INTO fg.IndustrySectors (Id, Name) SELECT c.ID, LEFT(LTRIM(RTRIM(c.ConfigName)), 400) FROM {s}ConfigurationName c;
            """));

        // Sub steps without a sector go to Wood. Newer legacy databases contain unused duplicate sub steps, so keep one
        // per (sector, sequence): the one in use, then the one with a sector, then the most used.
        const string subStepCandidates = """
            DECLARE @wood int = ISNULL((SELECT TOP 1 Id FROM fg.IndustrySectors WHERE Name = 'Wood'), (SELECT MIN(Id) FROM fg.IndustrySectors));
            IF OBJECT_ID('tempdb..#ss') IS NOT NULL DROP TABLE #ss;
            WITH used AS (SELECT SubStepID, COUNT(*) AS n FROM {0}FinishingStepsDetails WHERE SubStepID IS NOT NULL GROUP BY SubStepID)
            SELECT ss.ID, ISNULL(sec.Id, @wood) AS SectorId, ISNULL(TRY_CAST(ss.Sequence AS int), ss.ID) AS Seq, ISNULL(u.n, 0) AS Used,
                   ROW_NUMBER() OVER (PARTITION BY ISNULL(sec.Id, @wood), ISNULL(TRY_CAST(ss.Sequence AS int), ss.ID)
                                      ORDER BY CASE WHEN ISNULL(u.n, 0) > 0 THEN 0 ELSE 1 END, CASE WHEN sec.Id IS NOT NULL THEN 0 ELSE 1 END,
                                               ISNULL(u.n, 0) DESC, ss.ID) AS rn
            INTO #ss
            FROM {0}SubStep ss LEFT JOIN fg.IndustrySectors sec ON sec.Id = ss.ConfigurationID LEFT JOIN used u ON u.SubStepID = ss.ID;
            """;
        await Exec("Sub steps (dedupe)", string.Format(subStepCandidates, s));
        await Exec("Sub steps", Ident("SubSteps", $"""
            INSERT INTO fg.SubSteps (Id, IndustrySectorId, UserRole, Name, ShortName, Sequence, PassThroughs, WebLink, Instruction)
            SELECT ss.ID, c.SectorId, LEFT(ISNULL(NULLIF(ss.UserRole, ''), 'User'), 400), LEFT(LTRIM(RTRIM(ss.Name)), 400),
                   LEFT(LTRIM(RTRIM(ss.ShortName)), 400), c.Seq,
                   CASE WHEN ss.PassThroughsNumber > 0 THEN ss.PassThroughsNumber ELSE 1 END, LEFT(ss.WebLink, 400), LEFT(ss.Instruction, 4000)
            FROM {s}SubStep ss JOIN #ss c ON c.ID = ss.ID AND c.rn = 1;
            """));
        await using (var dropped = _conn.CreateCommand())
        {
            dropped.CommandText = "SELECT COUNT(*), ISNULL(SUM(Used), 0) FROM #ss WHERE rn > 1";
            await using var r = await dropped.ExecuteReaderAsync();
            if (await r.ReadAsync() && r.GetInt32(0) > 0)
                Console.WriteLine($"  Skipped {r.GetInt32(0)} duplicate sub steps ({r.GetInt32(1):N0} values referenced them).");
        }

        await ImportPullDownsAsync();

        await Exec("Process steps", Ident("ProcessSteps", $"""
            DECLARE @wood int = ISNULL((SELECT TOP 1 Id FROM fg.IndustrySectors WHERE Name = 'Wood'), (SELECT MIN(Id) FROM fg.IndustrySectors));
            INSERT INTO fg.ProcessSteps (Id, GroupId, Name, IndustrySectorId, IsDeleted, CreatedAt, UpdatedAt)
            SELECT fs.ID, fs.GroupID, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(fs.Description)), ''), CONCAT('Step #', fs.ID)), 400), ISNULL(sec.Id, @wood),
                   ISNULL(fs.IsDeleted, 0), SYSUTCDATETIME(), NULL
            FROM {s}FinishingSteps fs JOIN fg.Groups g ON g.Id = fs.GroupID LEFT JOIN fg.IndustrySectors sec ON sec.Id = fs.ConfigurationID;
            """));

        // One entry per (step, sub step, pass): category from the legacy pull-down selection, else from the filled characteristics.
        await Exec("Process step entries (staging)", $"""
            IF OBJECT_ID('tempdb..#e') IS NOT NULL DROP TABLE #e;
            SELECT IDENTITY(int, 1, 1) AS NewId, x.StepID, x.SubStepID, x.Pass, x.PullDownID, x.CategoryId
            INTO #e
            FROM (
                SELECT COALESCE(p.StepID, d.StepID) AS StepID, COALESCE(p.SubStepID, d.SubStepID) AS SubStepID, COALESCE(p.Pass, d.Pass) AS Pass,
                       p.PullDownID, COALESCE(p.CategoryId, d.CategoryId) AS CategoryId
                FROM (SELECT StepID, SubStepID, ISNULL(PassTrought, 1) AS Pass, MAX(PullDownID) AS PullDownID,
                             MAX(TRY_CAST(NULLIF(LTRIM(Value), '') AS int)) AS CategoryId
                      FROM {s}FinishingStepsPullDowns GROUP BY StepID, SubStepID, ISNULL(PassTrought, 1)) p
                FULL OUTER JOIN
                     (SELECT d.StepID, d.SubStepID, ISNULL(d.PassTrought, 1) AS Pass, MIN(ch.CategoryID) AS CategoryId
                      FROM {s}FinishingStepsDetails d JOIN {s}Characteristics ch ON ch.ID = d.CharacteristicsID
                      WHERE d.StepID IS NOT NULL AND d.SubStepID IS NOT NULL
                      GROUP BY d.StepID, d.SubStepID, ISNULL(d.PassTrought, 1)) d
                  ON d.StepID = p.StepID AND d.SubStepID = p.SubStepID AND d.Pass = p.Pass
            ) x
            WHERE EXISTS (SELECT 1 FROM fg.ProcessSteps ps WHERE ps.Id = x.StepID) AND EXISTS (SELECT 1 FROM fg.SubSteps ss WHERE ss.Id = x.SubStepID);
            UPDATE #e SET CategoryId = NULL WHERE CategoryId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fg.MaterialCategories c WHERE c.Id = #e.CategoryId);
            UPDATE #e SET PullDownID = NULL WHERE PullDownID IS NOT NULL AND NOT EXISTS (SELECT 1 FROM fg.SubStepPullDowns p WHERE p.Id = #e.PullDownID);
            UPDATE #e SET PullDownID = (SELECT MIN(p.Id) FROM fg.SubStepPullDowns p WHERE p.SubStepId = #e.SubStepID) WHERE PullDownID IS NULL;
            CREATE CLUSTERED INDEX IX_e ON #e (StepID, SubStepID, Pass);
            """);

        await Exec("Process step entries", Ident("ProcessStepEntries", """
            INSERT INTO fg.ProcessStepEntries (Id, ProcessStepId, SubStepId, Pass, PullDownId, CategoryId)
            SELECT NewId, StepID, SubStepID, Pass, PullDownID, CategoryId FROM #e;
            """));

        // Values keep the legacy detail id (schedule overrides reference it). Material pickers are re-pointed to the
        // material of the step's own group (legacy copies often kept ids from the template group).
        await Exec("Process step values (staging)", $"""
            IF OBJECT_ID('tempdb..#v') IS NOT NULL DROP TABLE #v;
            IF OBJECT_ID('tempdb..#mn') IS NOT NULL DROP TABLE #mn;
            SELECT GroupId, ProductName, MIN(Id) AS Id INTO #mn FROM fg.Materials WHERE IsDeleted = 0 GROUP BY GroupId, ProductName;
            CREATE CLUSTERED INDEX IX_mn ON #mn (GroupId, ProductName);
            SELECT d.ID, e.NewId AS EntryId, d.CharacteristicsID AS CharacteristicId, CASE WHEN c.InputType = 'Material' THEN 1 ELSE 0 END AS IsMaterial,
                   LEFT(d.Value, 4000) AS RawValue, TRY_CAST(d.Value AS int) AS ValMat, d.MaterialID AS ColMat, fs.GroupID AS StepGroup, c.CalcVariable AS Calc,
                   CAST(NULL AS int) AS MaterialId,
                   ROW_NUMBER() OVER (PARTITION BY e.NewId, d.CharacteristicsID ORDER BY d.ID DESC) AS rn
            INTO #v
            FROM {s}FinishingStepsDetails d
            JOIN {s}FinishingSteps fs ON fs.ID = d.StepID
            JOIN #e e ON e.StepID = d.StepID AND e.SubStepID = d.SubStepID AND e.Pass = ISNULL(d.PassTrought, 1)
            JOIN fg.Characteristics c ON c.Id = d.CharacteristicsID
            WHERE e.CategoryId IS NULL OR c.CategoryId = e.CategoryId;
            DELETE FROM #v WHERE rn > 1;
            UPDATE v SET MaterialId = CASE
                    WHEN vm.Id IS NOT NULL AND vm.GroupId = v.StepGroup THEN vm.Id
                    WHEN v.Calc = 'Material_ID' AND cm.Id IS NOT NULL THEN cm.Id
                    WHEN same.Id IS NOT NULL THEN same.Id
                    ELSE vm.Id END
            FROM #v v
            LEFT JOIN fg.Materials vm ON vm.Id = v.ValMat
            LEFT JOIN fg.Materials cm ON cm.Id = v.ColMat
            LEFT JOIN #mn same ON same.GroupId = v.StepGroup AND same.ProductName = vm.ProductName
            WHERE v.IsMaterial = 1;
            """);

        await Exec("Process step values", Ident("ProcessStepValues", """
            INSERT INTO fg.ProcessStepValues (Id, EntryId, CharacteristicId, Value, MaterialId)
            SELECT ID, EntryId, CharacteristicId, CASE WHEN IsMaterial = 1 THEN NULL ELSE RawValue END, MaterialId FROM #v;
            """));

        await Exec("Process schedules", Ident("ProcessSchedules", $"""
            INSERT INTO fg.ProcessSchedules (Id, GroupId, Name, Number, CustomerName, DepartmentId, IsArchived, CreatedAt, UpdatedAt, OneSidedArea, TwoSidedArea,
                                             LaborRate, MarkUp, PremiumMarkUp, OneSidedComplexity, OneSidedPriceArea, TwoSidedComplexity, TwoSidedPriceArea,
                                             HighComplexity, HighComplexityArea, TotalJobPrice)
            SELECT sc.ID, sc.GroupID, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(sc.Name)), ''), CONCAT('Schedule ', sc.ID)), 400), LEFT(ISNULL(LTRIM(RTRIM(sc.Number)), ''), 400),
                   LEFT(sc.CustomerName, 400), NULL, 0, SYSUTCDATETIME(), NULL, ISNULL(sc.OneSidedArea, 0), ISNULL(sc.TwoSidedArea, 0),
                   ISNULL(sc.LaborRate, 0), ISNULL(sc.MarkUp, 0), ISNULL(sc.PremiumMarkUp, 0), ISNULL(sc.OneSided, 0), ISNULL(sc.OneSidedArea, 0),
                   ISNULL(sc.TwoSided, 0), ISNULL(sc.TwoSidedArea, 0), ISNULL(sc.Complexity, 0), ISNULL(sc.HighComplexityArea, 0), ISNULL(sc.TotalJobPrice, 0)
            FROM {s}FinishingSchedules sc JOIN fg.Groups g ON g.Id = sc.GroupID;
            """));

        await Exec("Schedule steps", Ident("ProcessScheduleSteps", $"""
            INSERT INTO fg.ProcessScheduleSteps (Id, ScheduleId, ProcessStepId, Ordering, NameOverride)
            SELECT st.ID, st.FinishingScheduleID, st.FinishingStepID, st.Ordering + 1,
                   CASE WHEN NULLIF(LTRIM(RTRIM(st.Description)), '') IS NOT NULL AND LTRIM(RTRIM(st.Description)) <> LTRIM(RTRIM(ISNULL(fs.Description, '')))
                        THEN LEFT(LTRIM(RTRIM(st.Description)), 400) END
            FROM {s}FinishingSchedulesSteps st
            JOIN fg.ProcessSchedules ps ON ps.Id = st.FinishingScheduleID
            JOIN fg.ProcessSteps p ON p.Id = st.FinishingStepID
            LEFT JOIN {s}FinishingSteps fs ON fs.ID = st.FinishingStepID;
            """));

        // Only schedule values that really differ from the step value become schedule-level edits.
        await Exec("Schedule step edits (overrides)", $"""
            WITH sv AS (
                SELECT v.FinishingSchedulesStepID AS SchedStepId, v.FinishingStepsDetailID AS DetailId, v.Value,
                       ROW_NUMBER() OVER (PARTITION BY v.FinishingSchedulesStepID, v.FinishingStepsDetailID ORDER BY v.ID DESC) AS rn
                FROM {s}FinishingSchedulesStepsValues v WHERE v.FinishingStepsDetailID IS NOT NULL)
            INSERT INTO fg.ScheduleStepOverrides (ScheduleStepId, ProcessStepValueId, Value, MinValue, MaxValue)
            SELECT sv.SchedStepId, sv.DetailId,
                   CASE WHEN c.InputType = 'Material' THEN CAST(COALESCE(same.Id, vm.Id) AS nvarchar(20)) ELSE LEFT(sv.Value, 4000) END,
                   NULL, NULL
            FROM sv
            JOIN {s}FinishingStepsDetails d ON d.ID = sv.DetailId
            JOIN fg.ProcessStepValues pv ON pv.Id = sv.DetailId
            JOIN fg.Characteristics c ON c.Id = pv.CharacteristicId
            JOIN fg.ProcessScheduleSteps pss ON pss.Id = sv.SchedStepId
            JOIN fg.ProcessSchedules ps ON ps.Id = pss.ScheduleId
            LEFT JOIN fg.Materials vm ON vm.Id = TRY_CAST(sv.Value AS int)
            LEFT JOIN #mn same ON same.GroupId = ps.GroupId AND same.ProductName = vm.ProductName
            WHERE sv.rn = 1
              AND ((c.InputType = 'Material' AND vm.Id IS NOT NULL AND TRY_CAST(sv.Value AS int) <> ISNULL(TRY_CAST(d.Value AS int), -1))
                OR (c.InputType <> 'Material' AND ISNULL(sv.Value, '') <> ISNULL(d.Value, '')));
            """);
    }

    /// <summary>Legacy pull downs store a raw SQL query; convert it to the structured Material Type / Filter1 / Filter2 source.</summary>
    private async Task ImportPullDownsAsync()
    {
        var rows = new List<(int Id, int SubStepId, string? Sequence, string? Header, string? Query, string? Choice)>();
        await using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT p.ID, p.SubStepID, p.Sequence, p.Header, p.Query, p.ChoiceName FROM {_s}PullDownDefinition p WHERE EXISTS (SELECT 1 FROM fg.SubSteps s WHERE s.Id = p.SubStepID)";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                rows.Add((r.GetInt32(0), r.GetInt32(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                          r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        }

        static string? Filter(string? q, string col)
        {
            if (q == null) return null;
            var m = Regex.Match(q, $@"\b{col}\s*(?<![!<>])=\s*'([^']*)'", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : null;
        }

        foreach (var row in rows)
        {
            var typeMatch = row.Query == null ? null : Regex.Match(row.Query, @"MaterialType\]?\s*=\s*(\d+)", RegexOptions.IgnoreCase);
            int? type = typeMatch is { Success: true } ? int.Parse(typeMatch.Groups[1].Value) : null;
            if (type is < 1 or > 7) type = null;
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = """
                SET IDENTITY_INSERT fg.SubStepPullDowns ON;
                INSERT INTO fg.SubStepPullDowns (Id, SubStepId, Sequence, Header, ChoiceName, MaterialType, CategoryFilter1, CategoryFilter2)
                VALUES (@id, @sub, @seq, @header, @choice, @type, @f1, @f2);
                SET IDENTITY_INSERT fg.SubStepPullDowns OFF;
                """;
            void P(string n, object? v) { var p = cmd.CreateParameter(); p.ParameterName = n; p.Value = v ?? DBNull.Value; cmd.Parameters.Add(p); }
            P("@id", row.Id);
            P("@sub", row.SubStepId);
            P("@seq", int.TryParse(row.Sequence, out var seq) ? seq : 1);
            P("@header", Trunc(row.Header ?? row.Choice ?? "Select category", 400));
            P("@choice", Trunc(row.Choice, 400));
            P("@type", type);
            P("@f1", Trunc(Filter(row.Query, "Filter1"), 400));
            P("@f2", Trunc(Filter(row.Query, "Filter2"), 400));
            await cmd.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"  {"Sub step pull downs",-38} {rows.Count,12:N0} rows");
    }

    private static string? Trunc(string? v, int max) => v == null ? null : v.Length > max ? v[..max] : v;

    // ------------------------------------------------------------------ documents, photos, devices

    private async Task ImportFilesAndDevices()
    {
        var s = _s;
        await Exec("Documents (metadata)", Ident("Documents", $"""
            INSERT INTO fg.Documents (Id, GroupId, Name, FileName, StoredFile, ContentType, FileSize, CreatedBy, CreatedAt, UpdatedAt)
            SELECT d.DocID, d.GroupID, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(d.DocName)), ''), d.DocFile), 400), LEFT(d.DocFile, 400),
                   LEFT(CONCAT('documents/', d.GroupID, '/legacy/', d.DocFile), 400), NULL, 0, d.CreatedBy, ISNULL(d.CreatedDate, SYSUTCDATETIME()), d.UpdatedDate
            FROM {s}Documents d JOIN fg.Groups g ON g.Id = d.GroupID
            WHERE ISNULL(d.DocFile, '') <> '';
            """));

        await Exec("Document links", $"""
            INSERT INTO fg.DocumentLinks (DocumentId, EntityType, EntityId)
            SELECT DocumentId, EntityType, EntityId FROM (
                SELECT md.DocID AS DocumentId, CASE WHEN f.Id IS NOT NULL THEN 'Formula' ELSE 'Material' END AS EntityType, md.MaterialID AS EntityId
                FROM {s}MaterialDocs md LEFT JOIN fg.Formulas f ON f.Id = md.MaterialID WHERE md.MaterialID IS NOT NULL
                UNION SELECT fd.DocID, 'Formula', fd.FormulaID FROM {s}FormulaDocs fd WHERE fd.FormulaID IS NOT NULL
                UNION SELECT pd.DocID, 'ProcessStep', pd.ProcessStepID FROM {s}ProcessStepDocs pd WHERE pd.ProcessStepID IS NOT NULL
                UNION SELECT sd.DocID, 'ProcessSchedule', sd.ProcessScheduleID FROM {s}ProcessScheduleDocs sd WHERE sd.ProcessScheduleID IS NOT NULL
            ) x WHERE EXISTS (SELECT 1 FROM fg.Documents d WHERE d.Id = x.DocumentId);
            """);

        await Exec("Photos (metadata)", Ident("Photos", $"""
            INSERT INTO fg.Photos (Id, GroupId, Name, StoredFile, ContentType, CreatedBy, CreatedAt)
            SELECT p.PhotoID, p.GroupID, LEFT(ISNULL(NULLIF(LTRIM(RTRIM(p.PhotoName)), ''), p.PhotoFile), 400),
                   LEFT(CONCAT('photos/', p.GroupID, '/legacy/', p.PhotoFile), 400), NULL, p.CreatedBy, ISNULL(p.CreatedDate, SYSUTCDATETIME())
            FROM {s}PhotoGallery p JOIN fg.Groups g ON g.Id = p.GroupID;
            """));

        await Exec("Photo tags", $"""
            INSERT INTO fg.PhotoTags (PhotoId, Tag)
            SELECT DISTINCT t.PhotoID, LEFT(LTRIM(RTRIM(t.TagName)), 400)
            FROM {s}PhotoTags t JOIN fg.Photos p ON p.Id = t.PhotoID WHERE LTRIM(RTRIM(ISNULL(t.TagName, ''))) <> '';
            """);

        await Exec("Devices", Ident("Devices", $"""
            INSERT INTO fg.Devices (Id, GroupId, Name, Description, DeviceType, NetworkBridgeId, IpAddress, ApiKey, IsArchived, LastSeenAt, CreatedAt)
            SELECT d.ID, d.GroupId, LEFT(d.Name, 400), LEFT(d.Description, 400), CASE WHEN d.DeviceTypeID BETWEEN 1 AND 7 THEN d.DeviceTypeID ELSE 1 END,
                   NULL, LEFT(d.IPAddress, 400),
                   CASE WHEN ROW_NUMBER() OVER (PARTITION BY d.ApiKey ORDER BY d.ID) = 1 THEN d.ApiKey ELSE NEWID() END,
                   d.IsArchived, NULL, SYSUTCDATETIME()
            FROM {s}Devices d JOIN fg.Groups g ON g.Id = d.GroupId;
            UPDATE t SET NetworkBridgeId = sd.NetworkBridgeDeviceId
            FROM fg.Devices t JOIN {s}Devices sd ON sd.ID = t.Id
            WHERE sd.NetworkBridgeDeviceId IS NOT NULL AND EXISTS (SELECT 1 FROM fg.Devices b WHERE b.Id = sd.NetworkBridgeDeviceId);
            """));

        await Exec("Device canisters", $"""
            INSERT INTO fg.DeviceCanisters (DeviceId, CanisterNo, MaterialId)
            SELECT c.DeviceId, c.CanisterNo, m.Id FROM {s}DeviceCanisterConfiguration c
            JOIN fg.Devices d ON d.Id = c.DeviceId LEFT JOIN fg.Materials m ON m.Id = c.MaterialId;
            """);

        await Exec("Device metrics (last 30 days of data)", $"""
            DECLARE @max datetime = (SELECT MAX([DateTime]) FROM {s}DeviceMetrics);
            INSERT INTO fg.DeviceMetrics (DeviceId, Name, Unit, Timestamp, Value, TextValue)
            SELECT m.DeviceID, LEFT(m.Name, 400), LEFT(dt.Name, 400), m.[DateTime],
                   COALESCE(m.FloatValue, CAST(m.IntValue AS float), CASE WHEN m.BoolValue IS NULL THEN NULL WHEN m.BoolValue = 1 THEN 1.0 ELSE 0.0 END),
                   LEFT(m.StringValue, 400)
            FROM {s}DeviceMetrics m
            JOIN fg.Devices d ON d.Id = m.DeviceID
            LEFT JOIN {s}DataType dt ON dt.ID = m.DataTypeID
            WHERE m.[DateTime] > DATEADD(day, -30, @max);
            UPDATE d SET LastSeenAt = x.LastSeen
            FROM fg.Devices d JOIN (SELECT DeviceId, MAX(Timestamp) AS LastSeen FROM fg.DeviceMetrics GROUP BY DeviceId) x ON x.DeviceId = d.Id;
            """);
    }

    // ------------------------------------------------------------------ my work

    private async Task ImportMyWork()
    {
        var s = _s;
        await Exec("Defect types", Ident("DefectTypes", $"""
            INSERT INTO fg.DefectTypes (Id, GroupId, Name, ChartColor, IsArchived)
            SELECT d.ID, d.GroupID, LEFT(d.Name, 400), LEFT(d.ChartColor, 400), 0 FROM {s}MyWorkDefect d JOIN fg.Groups g ON g.Id = d.GroupID;
            """));

        await Exec("Adder types", Ident("AdderTypes", $"""
            INSERT INTO fg.AdderTypes (Id, GroupId, Name, IsArchived)
            SELECT a.ID, a.GroupID, LEFT(a.Name, 400), 0 FROM {s}MyWorkAdder a JOIN fg.Groups g ON g.Id = a.GroupID;
            """));

        await Exec("Executions", Ident("WorkExecutions", $"""
            WITH lastCheck AS (
                SELECT p.MyWorkExecutionID AS ExecId, MAX(l.EntryDate) AS LastDate
                FROM {s}MyWorkExecutedProcess p JOIN {s}MyWorkExecutedProcessLine l ON l.MyWorkExecutedProcessID = p.ID
                WHERE p.MyWorkExecutionID IS NOT NULL GROUP BY p.MyWorkExecutionID)
            INSERT INTO fg.WorkExecutions (Id, GroupId, ScheduleId, UserId, Status, Ordering, Notes, StartedAt, CompletedAt)
            SELECT e.ID, ps.GroupId, e.FinishingScheduleID, e.UserID, CASE WHEN e.Complete = 1 THEN 1 ELSE 0 END, ISNULL(e.[Order], 0), LEFT(e.Notes, 4000),
                   e.EntryDate, CASE WHEN e.Complete = 1 THEN ISNULL(lc.LastDate, e.EntryDate) END
            FROM {s}MyWorkExecution e
            JOIN fg.ProcessSchedules ps ON ps.Id = e.FinishingScheduleID
            JOIN fg.Users u ON u.Id = e.UserID
            LEFT JOIN lastCheck lc ON lc.ExecId = e.ID;
            """));

        await Exec("Execution lines (staging)", $"""
            IF OBJECT_ID('tempdb..#l') IS NOT NULL DROP TABLE #l;
            SELECT IDENTITY(int, 1, 1) AS NewId, x.* INTO #l FROM (
                SELECT p.ID AS ExecProcId, p.MyWorkExecutionID AS ExecId, pl.ID AS ProcLineId,
                       DENSE_RANK() OVER (PARTITION BY p.MyWorkExecutionID ORDER BY p.ID) AS StepNumber,
                       LEFT(ISNULL(mp.Name, ''), 400) AS StepName, pl.Sequence, LEFT(ISNULL(pl.Description, ''), 4000) AS Description
                FROM {s}MyWorkExecutedProcess p
                JOIN fg.WorkExecutions we ON we.Id = p.MyWorkExecutionID
                JOIN {s}MyWorkProcessLine pl ON pl.MyWorkProcessVersionID = p.MyWorkProcessVersionID
                JOIN {s}MyWorkProcess mp ON mp.ID = p.MyWorkProcessID
            ) x;
            CREATE CLUSTERED INDEX IX_l ON #l (ExecProcId, ProcLineId);
            """);

        await Exec("Execution lines", Ident("WorkExecutionLines", """
            INSERT INTO fg.WorkExecutionLines (Id, ExecutionId, StepNumber, StepName, Sequence, Description, Value, Unit, MinValue, MaxValue)
            SELECT NewId, ExecId, StepNumber, StepName, Sequence, Description, NULL, NULL, NULL, NULL FROM #l;
            """));

        await Exec("Execution line checks", $"""
            WITH metric AS (
                SELECT MyWorkExecutedProcessLineID AS LineId,
                       MAX(COALESCE(StringValue, CAST(DecimalValue AS nvarchar(40)), CAST(IntValue AS nvarchar(20)),
                                    CASE WHEN BoolValue = 1 THEN N'Yes' WHEN BoolValue = 0 THEN N'No' END,
                                    CONVERT(nvarchar(30), DateTimeValue, 120))) AS Recorded
                FROM {s}MyWorkExecutedProcessLineMetric GROUP BY MyWorkExecutedProcessLineID)
            INSERT INTO fg.WorkLineChecks (LineId, UserId, UserName, RecordedValue, CheckedAt)
            SELECT l.NewId, el.UserID, LEFT(u.Username, 400), LEFT(m.Recorded, 400), el.EntryDate
            FROM {s}MyWorkExecutedProcessLine el
            JOIN #l l ON l.ExecProcId = el.MyWorkExecutedProcessID AND l.ProcLineId = el.MyWorkProcessLineID
            JOIN fg.Users u ON u.Id = el.UserID
            LEFT JOIN metric m ON m.LineId = el.ID;
            """);

        await Exec("Execution defects", $"""
            INSERT INTO fg.WorkExecutionDefects (ExecutionId, DefectTypeId, Quantity, Notes, UserId, CreatedAt)
            SELECT p.MyWorkExecutionID, pd.MyWorkDefectID, d.Quantity, NULL, d.UserID, d.EntryDate
            FROM {s}MyWorkExecutedProcessDefect d
            JOIN {s}MyWorkExecutedProcess p ON p.ID = d.MyWorkExecutedProcessID
            JOIN {s}MyWorkProcessDefect pd ON pd.ID = d.MyWorkProcessDefectID
            JOIN fg.WorkExecutions we ON we.Id = p.MyWorkExecutionID
            JOIN fg.DefectTypes dt ON dt.Id = pd.MyWorkDefectID
            JOIN fg.Users u ON u.Id = d.UserID;
            """);

        await Exec("Execution adders", $"""
            INSERT INTO fg.WorkExecutionAdders (ExecutionId, AdderTypeId, Value, UserId, CreatedAt)
            SELECT p.MyWorkExecutionID, pa.MyWorkAdderID, LEFT(a.Value, 400), a.UserID, a.EntryDate
            FROM {s}MyWorkExecutedProcessAdder a
            JOIN {s}MyWorkExecutedProcess p ON p.ID = a.MyWorkExecutedProcessID
            JOIN {s}MyWorkProcessAdder pa ON pa.ID = a.MyWorkProcessAdderID
            JOIN fg.WorkExecutions we ON we.Id = p.MyWorkExecutionID
            JOIN fg.AdderTypes at ON at.Id = pa.MyWorkAdderID
            JOIN fg.Users u ON u.Id = a.UserID;
            """);
    }

    // ------------------------------------------------------------------ work instructions, messages

    private async Task ImportWorkInstructionsAndMessages()
    {
        var s = _s;
        const string latest = """
            IF OBJECT_ID('tempdb..#lv') IS NOT NULL DROP TABLE #lv;
            SELECT v.ID, v.WorkInstructionsID, v.Version, v.VersionDate,
                   ROW_NUMBER() OVER (PARTITION BY v.WorkInstructionsID ORDER BY v.VersionDate DESC, v.ID DESC) AS rn,
                   COUNT(*) OVER (PARTITION BY v.WorkInstructionsID) AS cnt
            INTO #lv FROM {0}WorkInstructionsVersions v;
            """;
        await Exec("Work instruction versions (staging)", string.Format(latest, s));

        await Exec("Work instructions", Ident("WorkInstructions", $"""
            INSERT INTO fg.WorkInstructions (Id, GroupId, DocumentNumber, Name, IssueDate, Version, IsReleased, Controlled, Location, Purpose, Scope,
                                             Terminology, IsDeleted, CreatedAt, UpdatedAt)
            SELECT w.ID, w.GroupID, LEFT(w.DocumentNumber, 400), LEFT(w.Name, 400), w.IssueDate, ISNULL(lv.cnt, 1),
                   CASE WHEN lv.Version IS NOT NULL AND lv.Version NOT LIKE 'DRAFT%' THEN 1 ELSE 0 END, w.Controlled,
                   LEFT(w.Location, 4000), LEFT(w.Purpose, 4000), LEFT(w.Scope, 4000), LEFT(w.Terminology, 4000), 0,
                   CASE WHEN w.IssueDate < '1900-01-02' THEN SYSUTCDATETIME() ELSE w.IssueDate END, lv.VersionDate
            FROM {s}WorkInstructions w JOIN fg.Groups g ON g.Id = w.GroupID LEFT JOIN #lv lv ON lv.WorkInstructionsID = w.ID AND lv.rn = 1;
            """));

        await Exec("Work instruction edit trail", $"""
            INSERT INTO fg.WorkInstructionTrails (WorkInstructionId, Version, Author, Log, Date)
            SELECT v.WorkInstructionsID, LEFT(v.Version, 400), LEFT(ISNULL(ISNULL(u.Email, u.Username), '(unknown)'), 400), LEFT(v.Log, 4000), v.VersionDate
            FROM {s}WorkInstructionsVersions v JOIN fg.WorkInstructions w ON w.Id = v.WorkInstructionsID LEFT JOIN {s}[User] u ON u.ID = v.UserID;
            """);

        await Exec("Work instruction steps (latest version)", $"""
            INSERT INTO fg.WorkInstructionSteps (WorkInstructionId, Level, Title, Body)
            SELECT st.WorkInstructionsID, st.Level1,
                   LEFT(CONCAT(CASE WHEN st.Level2 IS NOT NULL THEN CONCAT(st.Level1, '.', st.Level2,
                                   CASE WHEN st.Level3 IS NOT NULL THEN CONCAT('.', st.Level3) ELSE '' END,
                                   CASE WHEN st.Level4 IS NOT NULL THEN CONCAT('.', st.Level4) ELSE '' END, ' ') ELSE '' END,
                               ISNULL(st.WorkInstruction, '')), 4000),
                   NULL
            FROM {s}WorkInstructionsSteps st
            JOIN #lv lv ON lv.ID = st.WorkInstructionsVersionID AND lv.rn = 1
            JOIN fg.WorkInstructions w ON w.Id = st.WorkInstructionsID
            ORDER BY st.WorkInstructionsID, st.Level1, ISNULL(st.Level2, 0), ISNULL(st.Level3, 0), ISNULL(st.Level4, 0), st.ID;
            """);

        await Exec("Work instruction tools & materials", $"""
            INSERT INTO fg.WorkInstructionItems (WorkInstructionId, Kind, Description, MaterialId)
            SELECT e.WorkInstructionsID, 'Equipment', LEFT(e.Equipment, 400), NULL
            FROM {s}WorkInstructionsEquipment e JOIN #lv lv ON lv.ID = e.WorkInstructionsVersionsID AND lv.rn = 1 JOIN fg.WorkInstructions w ON w.Id = e.WorkInstructionsID
            UNION ALL
            SELECT m.WorkInstructionsID, 'Material', LEFT(m.Description, 400), NULL
            FROM {s}WorkInstructionsMaterials m JOIN #lv lv ON lv.ID = m.WorkInstructionsVersionsID AND lv.rn = 1 JOIN fg.WorkInstructions w ON w.Id = m.WorkInstructionsID;
            """);

        await Exec("Work instruction related docs", $"""
            INSERT INTO fg.WorkInstructionRelatedDocs (WorkInstructionId, DocumentNumber, DocumentName, Author, DocumentId)
            SELECT r.WorkInstructionsID, LEFT(r.DocumentNumber, 400), LEFT(r.DocumentName, 400), LEFT(r.Author, 400), NULL
            FROM {s}WorkInstructionsRelatedDocuments r JOIN #lv lv ON lv.ID = r.WorkInstructionsVersionID AND lv.rn = 1 JOIN fg.WorkInstructions w ON w.Id = r.WorkInstructionsID;
            """);

        await Exec("Work instruction signatures", $"""
            INSERT INTO fg.WorkInstructionSignatures (WorkInstructionId, Name, Position, Date)
            SELECT sg.WorkInstructionsID, LEFT(sg.Name, 400), NULL, sg.Date
            FROM {s}WorkInstructionsSignatures sg JOIN #lv lv ON lv.ID = sg.WorkInstructionsVersionsID AND lv.rn = 1 JOIN fg.WorkInstructions w ON w.Id = sg.WorkInstructionsID
            UNION ALL
            SELECT a.WorkInstructionsID, LEFT(a.Name, 400), LEFT(a.Position, 400), a.Date
            FROM {s}WorkInstructionsApprovals a JOIN #lv lv ON lv.ID = a.WorkInstructionsVersionsID AND lv.rn = 1 JOIN fg.WorkInstructions w ON w.Id = a.WorkInstructionsID;
            """);

        await Exec("Messages", Ident("Messages", $"""
            INSERT INTO fg.Messages (Id, GroupId, FromUserId, Subject, Body, MessageType, ReplyToId, SentAt)
            SELECT m.ID, m.GroupID, m.FromUserID, LEFT(ISNULL(m.Subject, ''), 400), ISNULL(m.Body, ''),
                   CASE WHEN m.MessageType LIKE '%support%' THEN 'Support' WHEN m.MessageType LIKE '%question%' THEN 'Question'
                        WHEN m.MessageType LIKE '%internal%' THEN 'Internal' ELSE 'General' END,
                   m.ReplyMessageID, m.MessageDateTime
            FROM {s}Messages m JOIN fg.Groups g ON g.Id = m.GroupID JOIN fg.Users u ON u.Id = m.FromUserID;
            """));

        await Exec("Message recipients", $"""
            INSERT INTO fg.MessageRecipients (MessageId, UserId, Viewed)
            SELECT r.MessageID, r.UserID, r.Viewed FROM {s}MessageRecipients r
            JOIN fg.Messages m ON m.Id = r.MessageID JOIN fg.Users u ON u.Id = r.UserID;
            """);
    }

    // ------------------------------------------------------------------ admin + report

    /// <summary>Keeps a known System Administrator login (Seed:AdminUsername / Seed:AdminPassword) after the import.</summary>
    private async Task EnsureAdminAsync()
    {
        var username = config["Seed:AdminUsername"] ?? "admin";
        if (await db.Users.AnyAsync(u => u.Username == username && !u.IsDeleted)) return;
        var groupId = await db.Groups.Where(g => g.Id == 3070).Select(g => (int?)g.Id).FirstOrDefaultAsync()
                      ?? await db.Groups.OrderBy(g => g.Id).Select(g => g.Id).FirstAsync();
        db.Users.Add(new User
        {
            GroupId = groupId,
            Username = username,
            Email = config["Seed:AdminEmail"] ?? "admin@finishgenius.local",
            FirstName = "System",
            LastName = "Administrator",
            PasswordHash = Passwords.Hash(config["Seed:AdminPassword"] ?? "Admin@12345"),
            AgreementAccepted = true,
            Roles = [new UserRole { Role = Roles.SystemAdmin }],
        });
        await db.SaveChangesAsync();
        Console.WriteLine($"  Created System Administrator '{username}' (legacy data has no such user).");
        log.LogInformation("Legacy import created admin user {User}", username);
    }

    private async Task ReportAsync()
    {
        Console.WriteLine("\nImported row counts:");
        foreach (var t in WipeOrder.Reverse())
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT_BIG(*) FROM fg.[{t}]";
            var n = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            Console.WriteLine($"  {t,-28} {n,12:N0}");
        }
    }
}
