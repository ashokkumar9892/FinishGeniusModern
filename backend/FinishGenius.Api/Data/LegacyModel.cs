using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FinishGenius.Api.Data;

/// <summary>
/// Maps the app's entities onto the OLD Finish Genius tables (schema dbo) for databases marked <c>"Legacy": true</c>, so the
/// same controllers read and write the data the old site uses. Reads go through one SQL query per table (renamed and
/// computed columns, NULL-safe flags); SaveChanges writes the real dbo table. Entities not mapped here still point at the
/// fg schema, which a legacy database does not have, so their screens report "not available yet".
/// Only <see cref="LegacyAppDbContext"/> uses this model and it is never migrated.
/// </summary>
public static partial class LegacyModel
{
    /// <summary>Text column that is NOT NULL in the legacy table: null is saved as "".</summary>
    public const string NotNullText = "Legacy:NotNullText";
    /// <summary>Value for a legacy-only NOT NULL column on insert: "@user" (signed-in user id), "@now" or a constant of the column's type.</summary>
    public const string InsertValue = "Legacy:InsertValue";
    /// <summary>Shadow property on legacy materials: the product name as stored, for the old site's sort order.</summary>
    public const string SortNameProperty = "SortName";
    /// <summary>Value saved instead of null for an optional app property whose legacy column is NOT NULL ("@user", "@now" or a constant).</summary>
    public const string NullDefault = "Legacy:NullDefault";
    /// <summary>Length of the legacy column: longer text is rejected with a readable message instead of a SQL error.</summary>
    public const string MaxLength = "Legacy:MaxLength";

    public static void Apply(ModelBuilder b)
    {
        Groups(b);
        Users(b);
        Departments(b);
        Vendors(b);
        Categories(b);
        Locations(b);
        IndustrySectors(b);
        Messages(b);
        History(b);
        Materials(b);
        ProcessSteps(b);
        ProcessSchedules(b);
        Devices(b);
        PurchaseOrders(b);
        MaterialsAndFormulas(b);
        MyWorkAndTelemetry(b);
        PhotoGallery(b);
        Processes(b);
        WorkInstructionTables(b);
    }

    // ------------------------------------------------------------------ groups, users

    private static void Groups(ModelBuilder b) => b.Entity<Group>(e =>
    {
        e.ToTable("Groups", "dbo");
        e.ToSqlQuery("""
            SELECT g.ID, ISNULL(NULLIF(LTRIM(RTRIM(g.Name)), ''), CONCAT('Group ', g.ID)) AS Name,
                   g.Address1, g.Address2, g.City, g.State, g.Zip, g.Country,
                   ISNULL(NULLIF(g.TimeZone, ''), 'Eastern Standard Time') AS TimeZone, g.ApiKey,
                   CAST(NULL AS nvarchar(400)) AS LogoFile, CAST(0 AS bit) AS ChecklistDeletionEnabled,
                   CAST(CASE WHEN g.deletionEnabled = 1 OR g.IsDeleted = 1 THEN 1 ELSE 0 END AS bit) AS deletionEnabled,
                   ISNULL(g.UpdatedDt, CAST('2000-01-01' AS datetime)) AS CreatedAt, g.UpdatedDt
            FROM dbo.Groups g
            """);
        e.Property(x => x.Id).HasColumnName("ID");
        // The old site's "delete group" sets deletionEnabled = 1 and every list hides those groups (spGetAllGroupByFilter):
        // 379 of 475 Prod groups. So that column is the group's deleted flag here, not "checklist deletion on submit".
        e.Property(x => x.IsDeleted).HasColumnName("deletionEnabled");
        ReadOnly(e.Property(x => x.ChecklistDeletionEnabled));
        e.Property(x => x.ChecklistDeletionEnabled).HasAnnotation(Unsupported,
            "\"Enable Checklist Deletion on Submit\" (on this database it is the old site's deleted flag and would hide the group)");
        e.Property(x => x.UpdatedAt).HasColumnName("UpdatedDt");
        ReadOnly(e.Property(x => x.LogoFile));   // legacy logos are keyed files (LogoKey), not paths
        ReadOnly(e.Property(x => x.CreatedAt));  // no such column
        Max(e.Property(x => x.TimeZone), 100);
        foreach (var p in new[] { nameof(Group.Address1), nameof(Group.Address2), nameof(Group.City), nameof(Group.State), nameof(Group.Zip), nameof(Group.Country) })
            e.Property(p).HasAnnotation(MaxLength, 50);
    });

    private static void Users(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("User", "dbo");
            e.ToSqlQuery("""
                SELECT u.ID, COALESCE(u.GroupID, u.DefaultGroupID, 0) AS GroupID, u.Email, u.Username, u.Password, u.FirstName, u.LastName,
                       u.PhoneNumber, u.Disabled, ISNULL(u.IsTermConditionAccepted, 0) AS IsTermConditionAccepted, u.TermsConditionUpdatedDate,
                       u.DefaultGroupID, CAST(0 AS bit) AS IsDeleted, u.CreateDate, CAST(NULL AS datetime) AS LastLoginAt, u.Discriminator
                FROM dbo.[User] u
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.PasswordHash).HasColumnName("Password");
            e.Property(x => x.AgreementAccepted).HasColumnName("IsTermConditionAccepted");
            e.Property(x => x.AgreementAcceptedAt).HasColumnName("TermsConditionUpdatedDate");
            e.Property(x => x.DefaultGroupId).HasColumnName("DefaultGroupID");
            e.Property(x => x.CreatedAt).HasColumnName("CreateDate");
            ReadOnly(e.Property(x => x.IsDeleted));    // the old site has no user delete, only Disabled
            ReadOnly(e.Property(x => x.LastLoginAt));  // not recorded by the old site
            Max(e.Property(x => x.PhoneNumber), 50);
            e.Property<string>("Discriminator").HasAnnotation(InsertValue, "User");
        });

        b.Entity<UserRole>(e =>
        {
            e.ToTable("UserRoles", "dbo");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.UserId).HasColumnName("UserID");
            // The old site's "Admin" role is the System Administrator.
            e.Property(x => x.Role).HasConversion(v => v == Roles.SystemAdmin ? "Admin" : v, v => v == "Admin" ? Roles.SystemAdmin : v);
            Max(e.Property(x => x.Role), 50);
        });

        b.Entity<UserGroup>(e =>
        {
            e.ToTable("User_Group_Junction", "dbo");
            e.Property(x => x.UserId).HasColumnName("UserID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
        });
    }

    private static void Departments(ModelBuilder b) => b.Entity<Department>(e =>
    {
        e.ToTable("Departments", "dbo");
        e.Property(x => x.Id).HasColumnName("ID");
        Max(e.Property(x => x.Name), 50);
    });

    private static void Vendors(ModelBuilder b) => b.Entity<Vendor>(e =>
    {
        e.ToTable("Vendors", "dbo");
        // Every text column is NOT NULL in the old table: blanks are stored as '' and read back as null.
        e.ToSqlQuery("""
            SELECT v.VendorID, v.GroupID, v.VendorName, NULLIF(v.Address, '') AS Address, NULLIF(v.City, '') AS City, NULLIF(v.State, '') AS State,
                   NULLIF(v.Zip, '') AS Zip, NULLIF(v.Country, '') AS Country, NULLIF(v.PaymentTerms, '') AS PaymentTerms,
                   NULLIF(v.AccountNumber, '') AS AccountNumber, NULLIF(v.ContactName, '') AS ContactName, NULLIF(v.OfficePhone, '') AS OfficePhone,
                   NULLIF(v.MobilePhone, '') AS MobilePhone, NULLIF(v.VendorEmail, '') AS VendorEmail, NULLIF(v.RequestorEmail, '') AS RequestorEmail,
                   ISNULL(v.IsDeleted, 0) AS IsDeleted, v.CreatedBy
            FROM dbo.Vendors v
            """);
        e.Property(x => x.Id).HasColumnName("VendorID");
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        (string name, int max)[] columns =
        [
            (nameof(Vendor.VendorName), 50), (nameof(Vendor.Address), 250), (nameof(Vendor.City), 250), (nameof(Vendor.State), 50),
            (nameof(Vendor.Zip), 50), (nameof(Vendor.Country), 50), (nameof(Vendor.PaymentTerms), 250), (nameof(Vendor.AccountNumber), 50),
            (nameof(Vendor.ContactName), 50), (nameof(Vendor.OfficePhone), 50), (nameof(Vendor.MobilePhone), 50),
            (nameof(Vendor.VendorEmail), 50), (nameof(Vendor.RequestorEmail), 50),
        ];
        foreach (var (name, max) in columns) e.Property(name).HasAnnotation(NotNullText, true).HasAnnotation(MaxLength, max);
        e.Property<int>("CreatedBy").HasAnnotation(InsertValue, "@user");
    });

    // ------------------------------------------------------------------ categories, locations, sectors

    private static void Categories(ModelBuilder b)
    {
        b.Entity<MaterialCategory>(e =>
        {
            e.ToTable("Categories", "dbo");
            // Every legacy category is a shared library category (GroupId null); new ones get the table default group.
            e.ToSqlQuery("""
                SELECT c.ID, CAST(NULL AS int) AS GroupID, LTRIM(RTRIM(c.Name)) AS Name,
                       CASE WHEN c.MaterialType BETWEEN 1 AND 7 THEN c.MaterialType ELSE 1 END AS MaterialType, c.Filter1, c.Filter2
                FROM dbo.Categories c
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            ReadOnly(e.Property(x => x.GroupId));
        });

        b.Entity<Characteristic>(e =>
        {
            e.ToTable("Characteristics", "dbo");
            // Same input-type rules as the legacy import (Data/LegacyImporter.cs).
            e.ToSqlQuery("""
                SELECT ch.ID, ch.CategoryID, LTRIM(RTRIM(ISNULL(ch.Name, ''))) AS Name,
                       NULLIF(LTRIM(RTRIM(REPLACE(REPLACE(ch.Unit, CHAR(9), ''), ':', ''))), '') AS Unit,
                       CASE WHEN ch.PDQuery IS NOT NULL OR ch.CalcVariable LIKE '%[_]ID' THEN 'Material'
                            WHEN ch.CalcVariable IN ('Material_Coverage', 'Material_Qty', 'Step_Labor', 'Step_Setup') OR ch.CalcVariable LIKE 'Misc%[_]Qty' THEN 'Number'
                            WHEN ch.PrintStyle = 3 OR ch.Name LIKE 'Notes%' THEN 'Notes'
                            WHEN TRY_CAST(ch.DefaultVar AS float) IS NOT NULL THEN 'Number'
                            ELSE 'Text' END AS InputType,
                       CASE WHEN LTRIM(RTRIM(ISNULL(ch.CalcVariable, ''))) IN ('', 'N_A', 'null') THEN NULL ELSE LTRIM(RTRIM(ch.CalcVariable)) END AS CalcVariable,
                       ch.DefaultVar, ISNULL(ch.Sequence, ch.ID) AS Sequence
                FROM dbo.Characteristics ch
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.CategoryId).HasColumnName("CategoryID");
            e.Property(x => x.DefaultValue).HasColumnName("DefaultVar");
            ReadOnly(e.Property(x => x.InputType)); // derived from the legacy columns on every read
        });
    }

    private static void Locations(ModelBuilder b) => b.Entity<MaterialLocation>(e =>
    {
        e.ToTable("MaterialLocations", "dbo");
        e.ToSqlQuery("""
            SELECT l.MaterialLocationID, ISNULL(l.GroupID, 0) AS GroupID, l.LocationName,
                   CASE WHEN l.MaterialType BETWEEN 1 AND 7 THEN l.MaterialType ELSE 1 END AS MaterialType, l.isDeleted
            FROM dbo.MaterialLocations l
            """);
        e.Property(x => x.Id).HasColumnName("MaterialLocationID");
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        e.Property(x => x.Name).HasColumnName("LocationName");
        e.Property(x => x.IsDeleted).HasColumnName("isDeleted");
        Max(e.Property(x => x.Name), 50);
    });

    private static void IndustrySectors(ModelBuilder b) => b.Entity<IndustrySector>(e =>
    {
        e.ToTable("ConfigurationName", "dbo");
        e.ToSqlQuery("SELECT c.ID, LTRIM(RTRIM(c.ConfigName)) AS ConfigName FROM dbo.ConfigurationName c");
        e.Property(x => x.Id).HasColumnName("ID");
        e.Property(x => x.Name).HasColumnName("ConfigName");
    });

    // ------------------------------------------------------------------ messages, history

    private static void Messages(ModelBuilder b)
    {
        b.Entity<Message>(e =>
        {
            e.ToTable("Messages", "dbo");
            // Legacy types: FGAPPChat (group chat), FGAPPSupport, FinishingQuestion; Internal DPM has its own tables.
            e.ToSqlQuery("""
                SELECT m.ID, m.GroupID, m.FromUserID, ISNULL(m.Subject, '') AS Subject, ISNULL(m.Body, '') AS Body,
                       CASE WHEN m.MessageType LIKE '%support%' THEN 'FGAPPSupport' WHEN m.MessageType LIKE '%question%' THEN 'FinishingQuestion'
                            WHEN m.MessageType LIKE '%internal%' THEN 'InternalDPM' ELSE 'FGAPPChat' END AS MessageType,
                       m.ReplyMessageID, m.MessageDateTime, m.[To]
                FROM dbo.Messages m
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.FromUserId).HasColumnName("FromUserID");
            e.Property(x => x.ReplyToId).HasColumnName("ReplyMessageID");
            e.Property(x => x.SentAt).HasColumnName("MessageDateTime");
            e.Property(x => x.MessageType).HasConversion(
                v => v == "Support" ? "FGAPPSupport" : v == "Question" ? "FinishingQuestion" : v == "Internal" ? "InternalDPM" : "FGAPPChat",
                v => v == "FGAPPSupport" ? "Support" : v == "FinishingQuestion" ? "Question" : v == "InternalDPM" ? "Internal" : "General");
            Max(e.Property(x => x.Subject), 510);
            e.Property<string>("To"); // comma-separated recipient ids, filled on insert (NOT NULL in the old table)
        });

        b.Entity<MessageRecipient>(e =>
        {
            e.ToTable("MessageRecipients", "dbo");
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.MessageId).HasColumnName("MessageID");
            e.Property(x => x.UserId).HasColumnName("UserID");
        });
    }

    private static void History(ModelBuilder b) => b.Entity<AuditLog>(e =>
    {
        // Old site: HistoryCategory 0 = formulas, 2 = process schedules, 1 = other screens (ScreenName), 3 = My Work runs.
        // The app's entity types map to the old screen names both ways; other types are stored as-is.
        e.ToTable("History", "dbo");
        e.ToSqlQuery("""
            SELECT h.Id, h.GroupId, h.UserId, u.Username AS UserName,
                   CASE h.HistoryCategory WHEN 0 THEN 'Formulas' WHEN 2 THEN 'Process Schedule' ELSE ISNULL(h.ScreenName, '') END AS ScreenName,
                   h.CategoryId, ISNULL(h.Field, '') AS Field, h.Description, h.OldValue, h.NewValue, h.CreatedDate, h.HistoryCategory
            FROM dbo.History h LEFT JOIN dbo.[User] u ON u.ID = h.UserId
            """);
        e.Property(x => x.Id).HasConversion<int>();
        e.Property(x => x.EntityType).HasColumnName("ScreenName").HasConversion(
            v => v == "Formula" ? "Formulas" : v == "Material" ? "Equipments and Materials" : v == "MaterialCategory" ? "Categories"
                : v == "ProcessSchedule" ? "Process Schedule" : v == "SubStep" ? "Sub Step" : v,
            v => v == "Formulas" ? "Formula" : v == "Equipments and Materials" ? "Material" : v == "Categories" ? "MaterialCategory"
                : v == "Process Schedule" ? "ProcessSchedule" : v == "Sub Step" ? "SubStep" : v);
        e.Property(x => x.EntityId).HasColumnName("CategoryId");
        e.Property(x => x.Action).HasColumnName("Field");
        e.Property(x => x.Details).HasColumnName("Description");
        e.Property(x => x.CreatedAt).HasColumnName("CreatedDate");
        ReadOnly(e.Property(x => x.UserName));
        Max(e.Property(x => x.EntityType), 200);
        e.Property<int>("HistoryCategory"); // set on insert: 0 formulas, 2 process schedules, 1 otherwise
    });

    // ------------------------------------------------------------------ materials, schedules

    private static void Materials(ModelBuilder b) => b.Entity<Material>(e =>
    {
        // Simple materials and formulations share dbo.Materials; formulations are the formula "mirror" materials (type 6).
        e.ToTable("Materials", "dbo");
        e.ToSqlQuery("""
            SELECT m.ID, ISNULL(m.GroupID, 1066) AS GroupID,
                   CASE WHEN m.Discriminator = 'Formulation' THEN 6 WHEN m.MaterialType BETWEEN 1 AND 7 THEN m.MaterialType ELSE 1 END AS MaterialType,
                   m.CategoryId, LTRIM(RTRIM(m.ManufacturersProductCode)) AS ManufacturersProductCode,
                   ISNULL(NULLIF(LTRIM(RTRIM(m.ManufacturersProductName)), ''), CONCAT('(unnamed #', m.ID, ')')) AS ManufacturersProductName,
                   ISNULL(m.GramsPerCubicCentiMetres, 0) AS GramsPerCubicCentiMetres, ISNULL(m.PricePerGallon, ISNULL(m.FormulaCost, 0)) AS PricePerGallon,
                   ISNULL(m.VOC, 0) AS VOC, ISNULL(m.HAP, 0) AS HAP, ISNULL(m.TAP, 0) AS TAP, ISNULL(m.MinQuantity, 0) AS MinQuantity,
                   CAST(NULL AS int) AS VendorId, m.Notes, m.ColorCode, ISNULL(m.IsDeleted, 0) AS IsDeleted,
                   ISNULL(m.MixedOn, CAST('2000-01-01' AS datetime)) AS CreatedAt, CAST(NULL AS datetime) AS UpdatedAt, m.Discriminator,
                   ISNULL(m.ManufacturersProductName, '') AS SortName
            FROM dbo.Materials m
            """);
        e.Property(x => x.Id).HasColumnName("ID");
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        e.Property(x => x.ProductCode).HasColumnName("ManufacturersProductCode");
        e.Property(x => x.ProductName).HasColumnName("ManufacturersProductName");
        e.Property(x => x.Price).HasColumnName("PricePerGallon");
        Float(e.Property(x => x.Density).HasColumnName("GramsPerCubicCentiMetres"));
        Float(e.Property(x => x.Voc).HasColumnName("VOC"));
        Float(e.Property(x => x.Hap).HasColumnName("HAP"));
        Float(e.Property(x => x.Tap).HasColumnName("TAP"));
        Float(e.Property(x => x.MinQuantity));
        ReadOnly(e.Property(x => x.VendorId));   // the old site does not link materials to vendors
        ReadOnly(e.Property(x => x.CreatedAt));
        ReadOnly(e.Property(x => x.UpdatedAt));
        // The name exactly as stored (leading spaces included): the old site's material tables sort on it.
        ReadOnly(e.Property<string>(SortNameProperty));
        e.Property<string>("Discriminator").HasAnnotation(InsertValue, "SimpleMaterial");
    });

    private static void ProcessSteps(ModelBuilder b) => b.Entity<ProcessStep>(e =>
    {
        // Step headers only; the step contents (FinishingStepsDetails / PullDowns) are mapped with the process screens.
        e.ToTable("FinishingSteps", "dbo");
        e.ToSqlQuery("""
            SELECT fs.ID, fs.GroupID, ISNULL(NULLIF(LTRIM(RTRIM(fs.Description)), ''), CONCAT('Step #', fs.ID)) AS Description,
                   ISNULL(fs.ConfigurationID, (SELECT TOP 1 c.ID FROM dbo.ConfigurationName c WHERE c.ConfigName = 'Wood')) AS ConfigurationID,
                   ISNULL(fs.IsDeleted, 0) AS IsDeleted, CAST('2000-01-01' AS datetime) AS CreatedAt, CAST(NULL AS datetime) AS UpdatedAt
            FROM dbo.FinishingSteps fs
            """);
        e.Property(x => x.Id).HasColumnName("ID");
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        e.Property(x => x.Name).HasColumnName("Description");
        e.Property(x => x.IndustrySectorId).HasColumnName("ConfigurationID");
        ReadOnly(e.Property(x => x.CreatedAt));
        ReadOnly(e.Property(x => x.UpdatedAt));
    });

    private static void ProcessSchedules(ModelBuilder b) => b.Entity<ProcessSchedule>(e =>
    {
        // Same column meaning as the legacy import: the pricing "price area" fields are the stored areas.
        e.ToTable("FinishingSchedules", "dbo");
        e.ToSqlQuery("""
            SELECT sc.ID, sc.GroupID, ISNULL(NULLIF(LTRIM(RTRIM(sc.Name)), ''), CONCAT('Schedule ', sc.ID)) AS Name,
                   ISNULL(LTRIM(RTRIM(sc.Number)), '') AS Number, sc.CustomerName, dp.DepartmentId, sc.Status,
                   CAST('2000-01-01' AS datetime) AS CreatedAt, CAST(NULL AS datetime) AS UpdatedAt,
                   ISNULL(sc.OneSidedArea, 0) AS OneSidedArea, ISNULL(sc.TwoSidedArea, 0) AS TwoSidedArea, ISNULL(sc.LaborRate, 0) AS LaborRate,
                   ISNULL(sc.MarkUp, 0) AS MarkUp, ISNULL(sc.PremiumMarkUp, 0) AS PremiumMarkUp, ISNULL(sc.OneSided, 0) AS OneSided,
                   ISNULL(sc.OneSidedArea, 0) AS OneSidedPriceArea, ISNULL(sc.TwoSided, 0) AS TwoSided, ISNULL(sc.TwoSidedArea, 0) AS TwoSidedPriceArea,
                   ISNULL(sc.Complexity, 0) AS Complexity, ISNULL(sc.HighComplexityArea, 0) AS HighComplexityArea, ISNULL(sc.TotalJobPrice, 0) AS TotalJobPrice,
                   sc.DeletionEnabled
            FROM dbo.FinishingSchedules sc
            LEFT JOIN (SELECT st.FinishingScheduleID, p.GroupID, MIN(p.DepartmentID) AS DepartmentId
                       FROM dbo.FinishingSchedulesSteps st JOIN dbo.MyWorkProcess p ON p.FinishingStepID = st.FinishingStepID
                       WHERE p.Status = 1 AND p.DepartmentID IS NOT NULL
                       GROUP BY st.FinishingScheduleID, p.GroupID) dp ON dp.FinishingScheduleID = sc.ID AND dp.GroupID = sc.GroupID
            """);
        e.Property(x => x.Id).HasColumnName("ID");
        e.Property(x => x.GroupId).HasColumnName("GroupID");
        e.Property(x => x.OneSidedComplexity).HasColumnName("OneSided");
        e.Property(x => x.TwoSidedComplexity).HasColumnName("TwoSided");
        e.Property(x => x.HighComplexity).HasColumnName("Complexity");
        ReadOnly(e.Property(x => x.OneSidedPriceArea));  // same stored column as OneSidedArea
        ReadOnly(e.Property(x => x.TwoSidedPriceArea));  // same stored column as TwoSidedArea
        // Departments belong to the schedule's My Work processes in the old site: read from them, assigned there.
        e.Property(x => x.DepartmentId).HasAnnotation(Unsupported, "Assigning a department to a schedule (departments belong to My Work processes there)");
        ReadOnly(e.Property(x => x.DepartmentId));
        // Status 1 = active, 0 = inactive (deleted in the old site) = archived here.
        e.Property(x => x.IsArchived).HasColumnName("Status").HasConversion(new ValueConverter<bool, int>(v => v ? 0 : 1, v => v == 0));
        ReadOnly(e.Property(x => x.CreatedAt));
        ReadOnly(e.Property(x => x.UpdatedAt));
        e.Property<bool>("DeletionEnabled").HasAnnotation(InsertValue, false);
    });

    // ------------------------------------------------------------------ devices, purchase orders

    private static void Devices(ModelBuilder b)
    {
        b.Entity<Device>(e =>
        {
            e.ToTable("Devices", "dbo");
            // Last seen = newest reading of the past day (DeviceMetrics is indexed by time, not by device), in UTC;
            // older readings show as "not seen recently".
            e.ToSqlQuery("""
                SELECT d.ID, d.GroupId, d.Name, d.Description,
                       CASE WHEN d.DeviceTypeID BETWEEN 1 AND 7 THEN d.DeviceTypeID ELSE 1 END AS DeviceTypeID,
                       d.NetworkBridgeDeviceId, d.IPAddress, d.ApiKey, d.IsArchived,
                       DATEADD(minute, DATEDIFF(minute, GETDATE(), GETUTCDATE()), ls.LastSeen) AS LastSeenAt, CAST('2000-01-01' AS datetime) AS CreatedAt
                FROM dbo.Devices d
                LEFT JOIN (SELECT m.DeviceID, MAX(m.[DateTime]) AS LastSeen FROM dbo.DeviceMetrics m
                           WHERE m.[DateTime] >= DATEADD(day, -1, GETDATE()) GROUP BY m.DeviceID) ls ON ls.DeviceID = d.ID
                """);
            e.Property(x => x.Id).HasColumnName("ID");
            e.Property(x => x.DeviceType).HasColumnName("DeviceTypeID");
            e.Property(x => x.NetworkBridgeId).HasColumnName("NetworkBridgeDeviceId");
            e.Property(x => x.IpAddress).HasColumnName("IPAddress");
            ReadOnly(e.Property(x => x.LastSeenAt)); // the old site keeps readings in DeviceMetrics only
            ReadOnly(e.Property(x => x.CreatedAt));
            Max(e.Property(x => x.IpAddress), 255);
        });

        b.Entity<DeviceCanister>(e =>
        {
            e.ToTable("DeviceCanisterConfiguration", "dbo");
            e.Property(x => x.Id).HasColumnName("ID");
        });
    }

    private static void PurchaseOrders(ModelBuilder b)
    {
        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("POHeaders", "dbo");
            e.Property(x => x.Id).HasColumnName("POHeaderID");
            e.Property(x => x.GroupId).HasColumnName("GroupID");
            e.Property(x => x.VendorId).HasColumnName("VendorID");
            e.Property(x => x.PoNumber).HasColumnName("PONum");
            e.Property(x => x.ShipName).HasColumnName("Name");
            e.Property(x => x.ShipAddress1).HasColumnName("Address1");
            e.Property(x => x.ShipAddress2).HasColumnName("Address2");
            e.Property(x => x.ShipCity).HasColumnName("City");
            e.Property(x => x.ShipState).HasColumnName("State");
            e.Property(x => x.ShipZip).HasColumnName("Zip");
            e.Property(x => x.ShipCountry).HasColumnName("Country");
            e.Property(x => x.CreatedAt).HasColumnName("DateCreated");
            e.Property(x => x.DeliveryDate).HasAnnotation(NullDefault, "@now");
            foreach (var p in new[] { nameof(PurchaseOrder.PoNumber), nameof(PurchaseOrder.ShipName), nameof(PurchaseOrder.ShipAddress1), nameof(PurchaseOrder.ShipAddress2),
                                      nameof(PurchaseOrder.ShipCity), nameof(PurchaseOrder.ShipState), nameof(PurchaseOrder.ShipZip), nameof(PurchaseOrder.ShipCountry) })
                e.Property(p).HasAnnotation(MaxLength, 50);
            e.Property<string>("OrderPDF").HasAnnotation(InsertValue, "");
        });

        b.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("PODetails", "dbo");
            // The old site prices order lines from the material, it stores no unit price.
            e.ToSqlQuery("""
                SELECT d.PODetailID, d.OrderID, d.MaterialID, d.Quantity, d.QuantityType,
                       CAST(ISNULL((SELECT m.PricePerGallon FROM dbo.Materials m WHERE m.ID = d.MaterialID), 0) AS decimal(18,4)) AS UnitPrice,
                       d.GroupID, d.CreatedBy, d.[Date]
                FROM dbo.PODetails d
                """);
            e.Property(x => x.Id).HasColumnName("PODetailID");
            e.Property(x => x.PurchaseOrderId).HasColumnName("OrderID");
            e.Property(x => x.MaterialId).HasColumnName("MaterialID");
            Float(e.Property(x => x.Quantity));
            ReadOnly(e.Property(x => x.UnitPrice));
            Max(e.Property(x => x.QuantityType), 50);
            e.Property<int>("GroupID"); // copied from the order on insert
            e.Property<int>("CreatedBy").HasAnnotation(InsertValue, "@user");
            e.Property<DateTime>("Date").HasAnnotation(InsertValue, "@now");
        });
    }

    // ------------------------------------------------------------------ saving

    /// <summary>
    /// Called by LegacyAppDbContext before every save: refuses changes to tables the old site owns, turns changes to
    /// entities without a table of their own (inventory ledger, document links, device preferences) into SQL on the old
    /// tables, fills the old tables' required columns and enforces their lengths. Returns the SQL to run around the save.
    /// </summary>
    public static List<SideWrite> BeforeSave(DbContext db, int currentUserId, string label)
    {
        var side = new List<SideWrite>();
        // Step values first: they take their old-table columns from their entry before entries are detached.
        // Work instructions before their trail: a new version row must exist before the trail is appended to it.
        var entries = db.ChangeTracker.Entries()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .OrderBy(x => x.Entity switch { ProcessStepValue => 0, WorkInstruction => 1, _ => 2 }).ToList();
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false; // entries are detached below; don't let fix-up re-add them mid-loop
        try
        {
            foreach (var entry in entries)
            {
                if (entry.Metadata.FindAnnotation(ReadOnlyEntity)?.Value is string what)
                    throw new ApiException(StatusCodes.Status409Conflict, $"{what} isn't available on the {label} database yet.");
                if (Virtual(db, entry, currentUserId, label, side)) continue;
                if (entry.State == EntityState.Deleted)
                {
                    Deleting(entry, side);
                    continue;
                }
                Related(entry, side);
                RelatedProcess(entry, side);
                Fill(db, entry, currentUserId, label);
                RelatedShopFloor(entry, currentUserId, side); // after Fill: uses the old-table columns Fill sets
            }
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
        return side;
    }

    private static void Fill(DbContext db, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, int currentUserId, string label)
    {
        foreach (var p in entry.Properties)
            if (p.Metadata.FindAnnotation(Unsupported)?.Value is string what && entry.State == EntityState.Modified
                && p.IsModified && !Equals(p.CurrentValue, p.OriginalValue))
                throw new ApiException(StatusCodes.Status409Conflict, $"{what} isn't available on the {label} database yet.");
        FillProcess(db, entry);
        FillShopFloor(db, entry, currentUserId);
        {
            if (entry.State == EntityState.Added)
            {
                foreach (var p in entry.Properties)
                    if (p.Metadata.FindAnnotation(InsertValue)?.Value is { } v)
                        p.CurrentValue = Resolve(v, currentUserId);
                switch (entry.Entity)
                {
                    case PurchaseOrderLine line:
                        entry.Property("GroupID").CurrentValue = OrderGroupId(db, line);
                        break;
                    case PhotoTag tag:
                        entry.Property("GroupID").CurrentValue = PhotoGroupId(db, tag);
                        break;
                    case FormulaDispenseSnapshot s:
                        entry.Property("ColourID").CurrentValue = db.ChangeTracker.Entries<FormulaIngredient>()
                            .Select(i => i.Entity).FirstOrDefault(i => i.Id == s.IngredientId)?.MaterialId ?? 0;
                        break;
                    case Message m:
                        entry.Property("To").CurrentValue = string.Join(",", m.Recipients.Select(r => r.UserId));
                        break;
                    case AuditLog a:
                        a.UserId ??= currentUserId;
                        entry.Property("HistoryCategory").CurrentValue =
                            a.EntityType == LinkEntityTypes.Formula ? 0 : a.EntityType == LinkEntityTypes.ProcessSchedule ? 2 : 1;
                        break;
                }
            }

            foreach (var p in entry.Properties)
                if (p.CurrentValue == null && p.Metadata.FindAnnotation(NullDefault)?.Value is { } fallback)
                    p.CurrentValue = Resolve(fallback, currentUserId);

            foreach (var p in entry.Properties.Where(p => p.Metadata.ClrType == typeof(string)))
            {
                if (p.CurrentValue == null && p.Metadata.FindAnnotation(NotNullText) != null) p.CurrentValue = "";
                if (p.Metadata.FindAnnotation(MaxLength)?.Value is int max && p.CurrentValue is string s && s.Length > max
                    && (entry.State == EntityState.Added || p.IsModified))
                    throw ApiException.Bad($"{Label(p.Metadata.Name)} must be {max} characters or fewer.");
            }
        }
    }

    private static object Resolve(object value, int currentUserId) =>
        value is "@user" ? currentUserId : value is "@now" ? DateTime.Now : value;

    /// <summary>Group of the order a new line belongs to (tracked order first, else the stored one).</summary>
    private static int OrderGroupId(DbContext db, PurchaseOrderLine line) =>
        db.ChangeTracker.Entries<PurchaseOrder>().Select(e => e.Entity).FirstOrDefault(o => o.Lines.Contains(line) || o.Id == line.PurchaseOrderId)?.GroupId
        ?? db.Set<PurchaseOrder>().Where(o => o.Id == line.PurchaseOrderId).Select(o => o.GroupId).First();

    // ------------------------------------------------------------------ helpers

    /// <summary>Read from the query, never written (no such column in the old table).</summary>
    private static void ReadOnly(PropertyBuilder p)
    {
        p.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        p.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
    }

    private static void Max(PropertyBuilder p, int max) => p.HasAnnotation(MaxLength, max);

    /// <summary>
    /// A decimal property stored in a legacy float column. The app-wide decimal(18,4) convention must be cleared, or the
    /// store type becomes float(18), which SQL Server treats as real (single precision).
    /// </summary>
    private static void Float(PropertyBuilder p)
    {
        p.HasConversion<double>().HasColumnType("float");
        p.Metadata.SetPrecision(null);
        p.Metadata.SetScale(null);
    }

    /// <summary>"VendorName" → "Vendor Name".</summary>
    private static string Label(string name) =>
        string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]) ? " " + c : c.ToString()));
}
