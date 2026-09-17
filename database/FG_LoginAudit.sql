/*
    dbo.FG_LoginAudit — the sign-in log shown on Administration → Login Activity (owner account only).

    The application creates this table by itself the first time someone signs in, on the database named by
    "LoginAudit:Database" in appsettings.Local.json (the Production one by default). This script is the same
    statement, for reference or to create the table ahead of time.

    It is the only table Finish Genius adds to the legacy Production database; nothing existing is touched.
    Every sign-in attempt is written here, whichever database the user opened — DatabaseKey says which one.
*/

IF OBJECT_ID(N'dbo.FG_LoginAudit', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_LoginAudit
    (
        Id              BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FG_LoginAudit PRIMARY KEY,
        SignedInAtUtc   DATETIME2(0)  NOT NULL CONSTRAINT DF_FG_LoginAudit_At DEFAULT (SYSUTCDATETIME()),
        UserName        NVARCHAR(256) NOT NULL,   -- as typed on the sign-in page
        UserId          INT           NULL,       -- the matching user, when there is one
        DatabaseKey     NVARCHAR(50)  NOT NULL,   -- "Dev" / "Prod"
        DatabaseLabel   NVARCHAR(100) NULL,
        Succeeded       BIT           NOT NULL,
        FailureReason   NVARCHAR(200) NULL,       -- Unknown username / Wrong password / Account disabled
        IsOwner         BIT           NOT NULL CONSTRAINT DF_FG_LoginAudit_Owner DEFAULT (0),
        IpAddress       NVARCHAR(64)  NULL,       -- X-Forwarded-For when behind IIS / a load balancer
        Location        NVARCHAR(200) NULL,       -- "City, Region, Country", or "Local network"
        City            NVARCHAR(100) NULL,
        Region          NVARCHAR(100) NULL,
        Country         NVARCHAR(100) NULL,
        TimeZone        NVARCHAR(64)  NULL,
        Isp             NVARCHAR(150) NULL,
        UserAgent       NVARCHAR(400) NULL,
        Host            NVARCHAR(200) NULL        -- the site address the browser used
    );
    CREATE INDEX IX_FG_LoginAudit_SignedInAtUtc ON dbo.FG_LoginAudit (SignedInAtUtc DESC);
    CREATE INDEX IX_FG_LoginAudit_UserName ON dbo.FG_LoginAudit (UserName, SignedInAtUtc DESC);
END
GO

-- Housekeeping: the log grows by one row per sign-in attempt. To keep a year:
-- DELETE FROM dbo.FG_LoginAudit WHERE SignedInAtUtc < DATEADD(year, -1, SYSUTCDATETIME());
