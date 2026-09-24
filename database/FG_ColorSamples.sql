/*
    dbo.FG_ColorSamples — the colour-matching sample library (Formulation → Color Matching).

    One row per measured physical sample: the wood, the formula and strength, how it was applied and topcoated, and
    the L*a*b* it measured. Everything the feature does — matching a target colour, predicting where a formula lands,
    the stain preview — is worked out from these rows, so the table IS the training data.

    On the app's own database (schema fg) this table comes from an EF migration (fg.ColorSamples). On the old site's
    Production database the app creates this one by itself the first time the screen is used, like dbo.FG_LoginAudit
    and dbo.FG_PageAccess. This script is the same statement, to create it ahead of time or to review it.

    Nothing existing is touched.
*/

IF OBJECT_ID(N'dbo.FG_ColorSamples', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_ColorSamples
    (
        Id             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_ColorSamples PRIMARY KEY,
        GroupId        INT             NOT NULL,
        Name           NVARCHAR(200)   NOT NULL,
        WoodSpecies    NVARCHAR(100)   NOT NULL,   -- Maple, Oak, Cherry…
        SandingGrit    INT             NULL,
        WoodL          FLOAT           NULL,       -- the unfinished board, when it was measured
        WoodA          FLOAT           NULL,
        WoodB          FLOAT           NULL,
        GrainDirection NVARCHAR(100)   NULL,       -- flat sawn, rift, quartered…
        Porosity       NVARCHAR(100)   NULL,
        GrowthRings    NVARCHAR(100)   NULL,
        ExistingFinish NVARCHAR(200)   NULL,
        MoisturePercent FLOAT          NULL,
        FormulaId      INT             NULL,       -- dbo.Materials (Discriminator 'Formulation'), when it is one of ours
        FormulaName    NVARCHAR(400)   NOT NULL,   -- kept as text too, so the sample still says what was on it
        Concentration  FLOAT           NULL,       -- percent
        Method         INT             NULL,       -- 1 Spray, 2 Wipe, 3 Brush, 4 Dip, 5 Roll, 9 Other
        Coats          INT             NULL,
        WetFilmMils    FLOAT           NULL,
        FlashMinutes   INT             NULL,
        SprayGun       NVARCHAR(200)   NULL,
        SprayPressurePsi FLOAT         NULL,
        DryingConditions NVARCHAR(400) NULL,
        Sealer         NVARCHAR(200)   NULL,
        Topcoat        NVARCHAR(200)   NULL,
        Sheen          FLOAT           NULL,       -- percent
        FinalL         FLOAT           NOT NULL,   -- the measured finished colour
        FinalA         FLOAT           NOT NULL,
        FinalB         FLOAT           NOT NULL,
        Source         INT             NOT NULL,   -- 1 Spectrophotometer, 2 Photo, 3 Typed in
        MeasuredAt     DATETIME2(0)    NULL,
        PhotoFile      NVARCHAR(400)   NULL,
        Notes          NVARCHAR(4000)  NULL,
        ColorantsJson  NVARCHAR(MAX)   NULL,       -- the recipe as it was when the sample was made

        CreatedBy      INT             NULL,
        CreatedAt      DATETIME2(0)    NOT NULL,
        UpdatedAt      DATETIME2(0)    NULL,
        IsDeleted      BIT             NOT NULL
    );
    CREATE INDEX IX_FG_ColorSamples_Group ON dbo.FG_ColorSamples (GroupId, WoodSpecies);
    CREATE INDEX IX_FG_ColorSamples_Formula ON dbo.FG_ColorSamples (FormulaId);
END
GO

-- What has been recorded, per formula and wood (the honest measure of how far the dataset has come):
-- SELECT FormulaName, WoodSpecies, COUNT(*) AS Samples, COUNT(DISTINCT Concentration) AS Strengths,
--        SUM(CASE WHEN Source = 1 THEN 1 ELSE 0 END) AS OnDevice
-- FROM dbo.FG_ColorSamples WHERE IsDeleted = 0
-- GROUP BY FormulaName, WoodSpecies ORDER BY Samples DESC;
