/*
    dbo.FG_PageAccess — the Page Access switches (Administration → Page Access, owner account only).

    They live in the database on purpose: installing a new build replaces the site's files, and the database is
    never part of the package, so the switches survive every deployment. The application creates this table by
    itself the first time Page Access is read or saved, on the database named by "PageAccess:Database" in
    appsettings.Local.json (the Production one by default). This script is the same statement, for reference or to
    create the table ahead of time.

    One row (Id = 1) holding the same JSON the app used to keep in App_Data\page-access.json:
        { "Pages": { "<page key>": [roles allowed] }, "Tabs": { "<page.tab>": [roles allowed] }, ... }
    A page or tab that is not listed is on for every role that normally has it.

    Nothing existing is touched; this is an added table, like dbo.FG_LoginAudit.
*/

IF OBJECT_ID(N'dbo.FG_PageAccess', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FG_PageAccess
    (
        Id            INT            NOT NULL CONSTRAINT PK_FG_PageAccess PRIMARY KEY,  -- always 1: one row
        SettingsJson  NVARCHAR(MAX)  NOT NULL,
        UpdatedAtUtc  DATETIME2(0)   NOT NULL CONSTRAINT DF_FG_PageAccess_At DEFAULT (SYSUTCDATETIME()),
        UpdatedBy     NVARCHAR(256)  NULL
    );
END
GO

-- What is switched off right now:
-- SELECT UpdatedAtUtc, UpdatedBy, SettingsJson FROM dbo.FG_PageAccess WHERE Id = 1;

-- Back to "every page on for its usual roles" (the app writes the row again on the next save):
-- DELETE FROM dbo.FG_PageAccess WHERE Id = 1;
