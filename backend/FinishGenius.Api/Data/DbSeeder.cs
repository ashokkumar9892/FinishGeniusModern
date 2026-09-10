using FinishGenius.Api.Domain;
using FinishGenius.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

/// <summary>
/// Idempotent startup seed: reference data (industry sectors, Wood sub steps from the legacy app),
/// the first System Administrator, and an optional demo group so every screen has something to show.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration config, ILogger log)
    {
        if (!await db.IndustrySectors.AnyAsync())
        {
            db.IndustrySectors.AddRange(new[] { "Aerospace", "Automotive", "Construction", "Food", "Heavy Manufacturing", "Marine", "Wood" }
                .Select(n => new IndustrySector { Name = n }));
            await db.SaveChangesAsync();
        }

        var wood = await db.IndustrySectors.FirstAsync(s => s.Name == "Wood");
        if (!await db.SubSteps.AnyAsync(s => s.IndustrySectorId == wood.Id))
        {
            // Mirrors the legacy FGAPP "Wood" configuration (sequence, pass throughs, pull-down source).
            (int seq, string name, string shortName, int passes, string? instruction, MaterialType? type, string? filter2, string header)[] rows =
            [
                (1, "WIDE BELT SANDING and SURFACE PREPARATION", "Wide Belt Sand", 1, "Select below the Substrate Surface Preparation (if any) for this step", MaterialType.Equipment, "WIDE BELT SAND", "Wide Belt Sanding Equipment Category"),
                (2, "PROFILE SANDING SURFACE PREPARATION", "Profile Sand", 1, null, MaterialType.Equipment, "PROFILE SAND", "Profile Sanding Equipment Category"),
                (3, "BRUSH SANDING SURFACE PREPARATION", "Brush Sand", 1, null, MaterialType.Equipment, "BRUSH SAND", "Brush Sanding Equipment Category"),
                (4, "DRUM SANDING SURFACE PREPARATION", "Drum Sand", 1, null, MaterialType.Equipment, "DRUM SAND", "Drum Sanding Equipment Category"),
                (5, "MACHINE ORBITAL SANDING SURFACE PREPARATION", "Machine Orbital Sand", 1, null, MaterialType.Equipment, "MACHINE ORBITAL", "Stroke Sanding Equipment Category"),
                (6, "HAND/ORBITAL SANDING SURFACE PREPARATION AND DISTRESSING PROCESSES", "Surface Preparation", 1, null, MaterialType.Equipment, "SURFACE PREPARATION", "Hand Sanding/Distressing Equipment/Product Category"),
                (7, "AUTOMATED SPRAY APPLICATION", "Spray Auto", 1, "Select below the Application Method (if any) for this step", MaterialType.Equipment, "SPRAY AUTO", "Automation Equipment Category"),
                (8, "HAND/AUTOMATED MATERIAL HANDLING", "Part Handling", 1, "Select below the Material Handling (if any) for this step", MaterialType.Equipment, "PART HANDLING", "Part Handling Method/Equipment Category"),
                (9, "AMBIENT/CONTROLLED SPRAY ENVIRONMENT", "Environment", 1, "Select below the Spray Environment (if any) for this step", MaterialType.Equipment, "ENVIRONMENT", "Spray Environment Control Equipment Categories"),
                (10, "COATING MATERIALS", "Materials", 1, "Select Coating Material Category in the drop down. To calculate quantities enter the coverage and mix ratios.", MaterialType.Product, "MATERIALS", "Select Base Materials/Formula Category"),
                (11, "SPRAY GUNS/APPLICATORS", "Guns", 1, "Select the Gun Inputs Below For This Step", MaterialType.Equipment, "GUNS", "Spray Gun/ Applicator Category"),
                (12, "COATING MATERIAL DELIVERY SYSTEMS", "Pumps", 1, "Select below the Delivery/Application System Setup (if any) for this step", MaterialType.Equipment, "PUMPS", "Material Delivery Systems Category"),
                (13, "CURING SYSTEMS", "Curing", 3, "Select below the Curing for this step", MaterialType.Equipment, "FORCE CURING", "Force Curing Systems Categories"),
                (14, "PROCESS STEP SUNDRY MATERIALS", "Sundries", 1, "Abrasive for use with Surface Prep step", MaterialType.Sundry, "SUNDRIES", "Sundry Category"),
                (15, "POLISHING/RUBBING", "Polishing", 1, null, MaterialType.Equipment, "POLISHING", "Polishing/Rubbing Equipment Category"),
                (16, "QC", "QC", 1, null, MaterialType.Equipment, "QC", "QC Category"),
                (17, "EQUIPMENT MAINTENANCE PARTS LIST", "Maintain", 1, null, MaterialType.Equipment, "MAINTAIN", "Equipment Category"),
                (18, "PROCESS COSTING", "Costing", 1, "Enter values on a per sq ft basis", MaterialType.Equipment, "COSTING", "Select Default"),
                (19, "MISCELLANEOUS", "MISC", 1, null, null, "MISC", "Miscellaneous"),
            ];
            foreach (var r in rows)
            {
                db.SubSteps.Add(new SubStep
                {
                    IndustrySectorId = wood.Id, UserRole = "User", Sequence = r.seq, Name = r.name, ShortName = r.shortName,
                    PassThroughs = r.passes, Instruction = r.instruction,
                    PullDowns = [new SubStepPullDown { Sequence = 1, Header = r.header, ChoiceName = r.shortName, MaterialType = r.type, CategoryFilter2 = r.filter2 }],
                });
            }
            await db.SaveChangesAsync();
            log.LogInformation("Seeded Wood sub steps.");
        }

        if (!await db.Groups.AnyAsync())
        {
            db.Groups.Add(new Group { Name = "AWFI", City = "Charlotte", State = "NC", Country = "USA", Address1 = "8334 Pineville-Matthews Road", Address2 = "Ste 103-159", Zip = "28226" });
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            var group = await db.Groups.OrderBy(g => g.Id).FirstAsync();
            var password = config["Seed:AdminPassword"] ?? "Admin@12345";
            db.Users.Add(new User
            {
                GroupId = group.Id,
                Username = config["Seed:AdminUsername"] ?? "admin",
                Email = config["Seed:AdminEmail"] ?? "admin@finishgenius.local",
                FirstName = "System",
                LastName = "Administrator",
                PasswordHash = Passwords.Hash(password),
                AgreementAccepted = true,
                Roles = [new UserRole { Role = Roles.SystemAdmin }],
            });
            await db.SaveChangesAsync();
            log.LogWarning("Created initial System Administrator '{User}'. Change the password after first login.", config["Seed:AdminUsername"] ?? "admin");
        }

        if (config.GetValue("Database:SeedDemoData", true) && !await db.Groups.AnyAsync(g => g.Name == DemoGroupName))
            await SeedDemoAsync(db, wood.Id, log);
    }

    public const string DemoGroupName = "AWFI Demo Group";

    private static async Task SeedDemoAsync(AppDbContext db, int woodId, ILogger log)
    {
        var admin = await db.Users.OrderBy(u => u.Id).FirstAsync();
        var g = new Group { Name = DemoGroupName, City = "Charlotte", State = "NC", Country = "USA" };
        db.Groups.Add(g);
        await db.SaveChangesAsync();
        db.UserGroups.Add(new UserGroup { UserId = admin.Id, GroupId = g.Id });

        MaterialCategory Cat(string name, MaterialType type, string? filter2, params Characteristic[] chars)
        {
            var c = new MaterialCategory { GroupId = g.Id, Name = name, MaterialType = type, Filter1 = "NA", Filter2 = filter2, Characteristics = chars.ToList() };
            db.MaterialCategories.Add(c);
            return c;
        }
        Characteristic Ch(int seq, string name, string input, string? unit = null, string? calc = null, string? def = null) =>
            new() { Sequence = seq, Name = name, InputType = input, Unit = unit, CalcVariable = calc, DefaultValue = def };

        var topcoat = Cat("Clear 2K Urethane Topcoat", MaterialType.Product, "MATERIALS",
            Ch(1, "Coating Material", CharacteristicInputTypes.Material, null, CalcVariables.MaterialId),
            Ch(2, "Coating Mix %", CharacteristicInputTypes.Number, "%", CalcVariables.MixPercent, "100"),
            Ch(3, "Catalyst", CharacteristicInputTypes.Material, null, CalcVariables.MaterialId),
            Ch(4, "Catalyst Mix %", CharacteristicInputTypes.Number, "%", CalcVariables.MixPercent),
            Ch(5, "Reducer", CharacteristicInputTypes.Material, null, CalcVariables.MaterialId),
            Ch(6, "Reducer Mix %", CharacteristicInputTypes.Number, "%", CalcVariables.MixPercent),
            Ch(7, "Coverage", CharacteristicInputTypes.Number, "SqFt / Gal", CalcVariables.Coverage),
            Ch(8, "Wet Mils", CharacteristicInputTypes.Number, "Wet Mils"),
            Ch(9, "Notes", CharacteristicInputTypes.Notes));
        var gun = Cat("Manual HVLP Gun", MaterialType.Equipment, "GUNS",
            Ch(1, "Manual HVLP Gun Name/Number/Manufacture", CharacteristicInputTypes.Material, null, CalcVariables.MaterialId),
            Ch(2, "Flow Rate", CharacteristicInputTypes.Number, "Oz. / Min"),
            Ch(3, "Fan Width", CharacteristicInputTypes.Number, "Inches"),
            Ch(4, "Gun To Part Distance", CharacteristicInputTypes.Number, "Inches"),
            Ch(5, "Atomizing Air Pressure", CharacteristicInputTypes.Number, "PSI"));
        var flash = Cat("Ambient Flash Off", MaterialType.Equipment, "FORCE CURING",
            Ch(1, "Flash Time", CharacteristicInputTypes.Number, "Mins"),
            Ch(2, "Temperature", CharacteristicInputTypes.Number, "ºF"),
            Ch(3, "Relative Humidity", CharacteristicInputTypes.Number, "% RH"),
            Ch(4, "Line Throughput", CharacteristicInputTypes.Number, "SqFt / Hr", CalcVariables.ProductionRate));
        var wideBelt = Cat("Wide Belt Sander", MaterialType.Equipment, "WIDE BELT SAND",
            Ch(1, "Sander", CharacteristicInputTypes.Material, null, CalcVariables.MaterialId),
            Ch(2, "Belt Grit", CharacteristicInputTypes.Text),
            Ch(3, "Feed Speed", CharacteristicInputTypes.Number, "Ft/Min"),
            Ch(4, "Oscillation Set-Up", CharacteristicInputTypes.YesNo));
        var costing = Cat("Process Costing", MaterialType.Equipment, "COSTING",
            Ch(1, "Overhead", CharacteristicInputTypes.Number, "$ / SqFt", CalcVariables.CostPerSqFt),
            Ch(2, "Sundry Allowance", CharacteristicInputTypes.Number, "$ / SqFt", CalcVariables.CostPerSqFt));
        var catalyst = Cat("Catalyst /Hardener", MaterialType.Base, "Additive");
        var reducer = Cat("Reducer", MaterialType.Base, "Additive");
        var pigments = Cat("Ilva PZ5 Series Pigments", MaterialType.Pigment, "NA");
        var dyes = Cat("Ilva PF5", MaterialType.Dye, "NA");
        var abrasives = Cat("Sheet Abrasives", MaterialType.Sundry, "SUNDRIES");
        await db.SaveChangesAsync();

        Material M(MaterialType t, MaterialCategory c, string name, string code, decimal density, decimal price, decimal voc = 0, decimal hap = 0, decimal tap = 0, decimal min = 0)
        {
            var m = new Material { GroupId = g.Id, MaterialType = t, CategoryId = c.Id, ProductName = name, ProductCode = code, Density = density, Price = price, Voc = voc, Hap = hap, Tap = tap, MinQuantity = min };
            db.Materials.Add(m);
            return m;
        }
        var kemvar = M(MaterialType.Product, topcoat, "SW Kemvar 9320S CV (Innovat)", "V84F90029", 8.1m, 48.50m, 4.2m, 0.3m, 0.1m, 5);
        var cat = M(MaterialType.Base, catalyst, "SW Standard Catalyst", "V66V20005", 7.9m, 62.00m, 5.1m, 0.2m, 0, 1);
        var red = M(MaterialType.Base, reducer, "Sherwin Williams N-Butyl Acetate \"Reducer\"", "R6K18", 7.3m, 21.75m, 7.3m, 0, 0, 2);
        M(MaterialType.Base, reducer, "Thinner Blend - TZ35", "6660074", 7.09m, 18.00m, 7.0939m);
        M(MaterialType.Pigment, pigments, "ILVA Aquatec Paste Violet", "PZ547", 9.35m, 350.00m, 0.03m);
        M(MaterialType.Dye, dyes, "Ilva Cherry - PF 5T02", "6630234", 7.76m, 103.23m, 0.8286m);
        M(MaterialType.Sundry, abrasives, "Mirka Abranet 180 Grit 3x4\" Abrasive", "9A-129-180", 0, 44.45m);
        var hvlp = M(MaterialType.Equipment, gun, "Kremlin Xcite HVLP Gun", "XCITE-120", 0, 675.00m);
        var sander = M(MaterialType.Equipment, wideBelt, "Hermies Wide Belt 43\" x 75\"", "151393", 0, 18500.00m);
        await db.SaveChangesAsync();

        db.InventoryTransactions.AddRange(
            new InventoryTransaction { GroupId = g.Id, MaterialId = kemvar.Id, Quantity = 20, Reason = "Opening balance", CreatedBy = admin.Id },
            new InventoryTransaction { GroupId = g.Id, MaterialId = cat.Id, Quantity = 4, Reason = "Opening balance", CreatedBy = admin.Id },
            new InventoryTransaction { GroupId = g.Id, MaterialId = red.Id, Quantity = 1, Reason = "Opening balance", CreatedBy = admin.Id });

        db.Vendors.Add(new Vendor { GroupId = g.Id, VendorName = "Sherwin Williams", PaymentTerms = "Net 30", AccountNumber = "AWFI-001", ContactName = "Greg Farris", OfficePhone = "(904) 679-9070", VendorEmail = "orders@example.com", City = "Cleveland", State = "OH", Zip = "44115", Address = "101 W Prospect Ave" });

        var subs = await db.SubSteps.Where(s => s.IndustrySectorId == woodId).Include(s => s.PullDowns).ToListAsync();
        SubStep Sub(string shortName) => subs.First(s => s.ShortName == shortName);
        ProcessStepEntry Entry(SubStep s, MaterialCategory c, int pass, params (int seq, string? value, int? materialId)[] values) => new()
        {
            SubStepId = s.Id, Pass = pass, PullDownId = s.PullDowns.FirstOrDefault()?.Id, CategoryId = c.Id,
            Values = values.Select(v => new ProcessStepValue
            {
                CharacteristicId = c.Characteristics.First(ch => ch.Sequence == v.seq).Id, Value = v.value, MaterialId = v.materialId,
            }).ToList(),
        };

        var sanding = new ProcessStep
        {
            GroupId = g.Id, Name = "Wide Belt Wood Sanding", IndustrySectorId = woodId,
            Entries = [Entry(Sub("Wide Belt Sand"), wideBelt, 1, (1, null, sander.Id), (2, "120 grit", null), (3, "45", null), (4, "Yes", null))],
        };
        var spray = new ProcessStep
        {
            GroupId = g.Id, Name = "Topcoat Spray - Kemvar CV", IndustrySectorId = woodId,
            Entries =
            [
                Entry(Sub("Guns"), gun, 1, (1, null, hvlp.Id), (2, "10", null), (3, "8", null), (4, "8", null), (5, "10", null)),
                Entry(Sub("Materials"), topcoat, 1, (1, null, kemvar.Id), (2, "100", null), (3, null, cat.Id), (4, "11", null), (5, null, red.Id), (6, "9.4", null), (7, "937.5", null), (8, "4", null)),
                Entry(Sub("Curing"), flash, 1, (1, "20", null), (2, "72", null), (3, "50", null), (4, "120", null)),
                Entry(Sub("Costing"), costing, 1, (1, "0.05", null), (2, "0.02", null)),
            ],
        };
        db.ProcessSteps.AddRange(sanding, spray);

        var line = new Department { GroupId = g.Id, Name = "Line Operations" };
        db.Departments.AddRange(line, new Department { GroupId = g.Id, Name = "Paint Finishes" }, new Department { GroupId = g.Id, Name = "Customer Support" });
        await db.SaveChangesAsync();

        db.ProcessSchedules.Add(new ProcessSchedule
        {
            GroupId = g.Id, Name = "Bamboo", Number = "WST-2020-42", CustomerName = "Demo Customer", DepartmentId = line.Id,
            Steps = [new ProcessScheduleStep { ProcessStepId = sanding.Id, Ordering = 1 }, new ProcessScheduleStep { ProcessStepId = spray.Id, Ordering = 2 }],
        });
        db.DefectTypes.AddRange(
            new DefectType { GroupId = g.Id, Name = "Orange Peel", ChartColor = "#f97316" },
            new DefectType { GroupId = g.Id, Name = "Runs / Sags", ChartColor = "#ef4444" },
            new DefectType { GroupId = g.Id, Name = "Dust Nibs", ChartColor = "#eab308" });
        db.AdderTypes.AddRange(new AdderType { GroupId = g.Id, Name = "Extra Sanding" }, new AdderType { GroupId = g.Id, Name = "Touch Up" });
        await db.SaveChangesAsync();
        log.LogInformation("Seeded demo group '{Group}'.", DemoGroupName);
    }
}
