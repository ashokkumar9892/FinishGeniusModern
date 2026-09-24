using FinishGenius.Api.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FinishGenius.Api.Data;

/// <summary>
/// Tables Finish Genius adds to the old site's database for features it has no equivalent of. The Production database
/// is never migrated by EF (see Program.cs), so these are created on first use, the way dbo.FG_LoginAudit and
/// dbo.FG_PageAccess are. On the app's own database (schema fg) the same entities come from an EF migration instead,
/// and nothing here runs.
/// </summary>
public static class LegacyTables
{
    public const string ColorSamples = "FG_ColorSamples";

    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static readonly HashSet<string> Ready = [];

    private static readonly string CreateColorSamples = $"""
        IF OBJECT_ID(N'dbo.{ColorSamples}', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.{ColorSamples}
            (
                Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_{ColorSamples} PRIMARY KEY,
                GroupId        INT             NOT NULL,
                Name           NVARCHAR(200)   NOT NULL,
                WoodSpecies    NVARCHAR(100)   NOT NULL,
                SandingGrit    INT             NULL,
                WoodL          FLOAT           NULL,
                WoodA          FLOAT           NULL,
                WoodB          FLOAT           NULL,
                GrainDirection NVARCHAR(100)   NULL,
                Porosity       NVARCHAR(100)   NULL,
                GrowthRings    NVARCHAR(100)   NULL,
                ExistingFinish NVARCHAR(200)   NULL,
                MoisturePercent FLOAT          NULL,
                FormulaId      INT             NULL,
                FormulaName    NVARCHAR(400)   NOT NULL,
                Concentration  FLOAT           NULL,
                Method         INT             NULL,
                Coats          INT             NULL,
                WetFilmMils    FLOAT           NULL,
                FlashMinutes   INT             NULL,
                SprayGun       NVARCHAR(200)   NULL,
                SprayPressurePsi FLOAT         NULL,
                DryingConditions NVARCHAR(400) NULL,
                Sealer         NVARCHAR(200)   NULL,
                Topcoat        NVARCHAR(200)   NULL,
                Sheen          FLOAT           NULL,
                FinalL         FLOAT           NOT NULL,
                FinalA         FLOAT           NOT NULL,
                FinalB         FLOAT           NOT NULL,
                Source         INT             NOT NULL,
                MeasuredAt     DATETIME2(0)    NULL,
                PhotoFile      NVARCHAR(400)   NULL,
                Notes          NVARCHAR(4000)  NULL,
                ColorantsJson  NVARCHAR(MAX)   NULL,
                CreatedBy      INT             NULL,
                CreatedAt      DATETIME2(0)    NOT NULL,
                UpdatedAt      DATETIME2(0)    NULL,
                IsDeleted      BIT             NOT NULL
            );
            CREATE INDEX IX_{ColorSamples}_Group ON dbo.{ColorSamples} (GroupId, WoodSpecies);
            CREATE INDEX IX_{ColorSamples}_Formula ON dbo.{ColorSamples} (FormulaId);
        END
        """;

    /// <summary>
    /// Columns added after the table first shipped. A site that already has the table gets them here, so an upgrade
    /// needs nothing run by hand.
    /// </summary>
    private static readonly (string Name, string Type)[] LaterColumns =
    [
        ("GrainDirection", "NVARCHAR(100) NULL"), ("Porosity", "NVARCHAR(100) NULL"), ("GrowthRings", "NVARCHAR(100) NULL"),
        ("ExistingFinish", "NVARCHAR(200) NULL"), ("MoisturePercent", "FLOAT NULL"), ("SprayGun", "NVARCHAR(200) NULL"),
        ("SprayPressurePsi", "FLOAT NULL"), ("DryingConditions", "NVARCHAR(400) NULL"), ("ColorantsJson", "NVARCHAR(MAX) NULL"),
    ];

    private static readonly string AddColorSampleColumns = string.Join(Environment.NewLine, LaterColumns.Select(c =>
        $"IF COL_LENGTH(N'dbo.{ColorSamples}', N'{c.Name}') IS NULL ALTER TABLE dbo.{ColorSamples} ADD {c.Name} {c.Type};"));

    /// <summary>Creates the colour-sample table if this database is the old site's and does not have it yet.</summary>
    public static async Task EnsureColorSamplesAsync(AppDbContext db, CancellationToken token = default)
    {
        if (db is not LegacyAppDbContext) return; // the app's own database gets it from a migration
        var key = db.Database.GetConnectionString() ?? "";
        if (Ready.Contains(key)) return;
        await Lock.WaitAsync(token);
        try
        {
            if (Ready.Contains(key)) return;
            await db.Database.ExecuteSqlRawAsync(CreateColorSamples, token);
            await db.Database.ExecuteSqlRawAsync(AddColorSampleColumns, token);
            Ready.Add(key);
        }
        catch (SqlException e)
        {
            throw new ApiException(StatusCodes.Status503ServiceUnavailable,
                $"The colour samples table (dbo.{ColorSamples}) could not be created on this database: {e.Message} " +
                $"Run database/{ColorSamples}.sql once, or give the login permission to create tables.");
        }
        finally
        {
            Lock.Release();
        }
    }
}
