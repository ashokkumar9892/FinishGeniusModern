# Importing data from the legacy Finish Genius database

The new app can copy all business data from the legacy database (`FGAPP`) into its own `fg` schema.
The import runs **inside SQL Server** (cross-database `INSERT … SELECT`), so it is fast and nothing passes through
your PC.

## Requirements

- The legacy database and the target database (the one in `ConnectionStrings:Default`) are on the **same SQL Server**.
- The SQL login in the connection string can read the legacy database and owns (or can write to) the target database.
- Stop the website/API while importing (or accept that users see errors for a few minutes).

## Run it

From a build (dev machine) — uses the connection string in `backend/FinishGenius.Api/appsettings.Local.json`:

```powershell
cd backend\FinishGenius.Api
dotnet run -- import-legacy --source FGAPP --yes
```

On the server, from the deployed site folder (e.g. `C:\inetpub\FinishGenius`):

```powershell
dotnet FinishGenius.Api.dll import-legacy --source FGAPP --yes
```

Without `--yes` it only prints what it would do.

**Current setup:** the app runs in `FGAPP_21_May_2024` on `34.74.178.204`, which also contains the legacy tables
(`dbo`). The source is then the same database — the new app only ever writes to the `fg` schema:

```powershell
dotnet run -- import-legacy --source FGAPP_21_May_2024 --yes
```

> **Warning:** every run first **deletes all rows in the `fg` schema** of the target database, then re-imports.
> Other schemas (e.g. `dbo` tables that belong to other applications) are never touched.
> Anything created in the new app since the last import is lost — import once, then work in the new app.

## What is imported

| Legacy | New app | Notes |
|---|---|---|
| Groups, User, UserRoles, User_Group_Junction | Groups, Users, roles, extra groups | Same ids. Legacy role `Admin` → System Administrator. Users without a group get their default group (or AWFI). |
| Passwords (bcrypt) | kept | Users sign in with their existing password; it is upgraded to the new hash on first login. |
| Departments, Vendors | same | |
| Categories, Characteristics | Material Categories (shared) | Legacy categories are global, so they are imported as **shared** categories visible to every group (editable by System Administrators). |
| Materials | Equipment & Materials | Formulations are also imported as materials (type Formula) so process steps can reference them. Materials without a group go to "_Master". |
| Materials (Formulation) + FormulationMaterials | Formulas + ingredients | Status 2 = Complete. |
| MaterialBatches, MaterialQuantityChanges | Inventory transactions | Opening balance per batch + dispense history; on-hand equals the legacy batch quantities. |
| MaterialLocations, POHeaders/PODetails | Locations, Purchase orders | |
| ConfigurationName, SubStep, PullDownDefinition | Industry sectors, Sub steps, Pull downs | Pull-down SQL queries are converted to Material Type / Filter1 / Filter2. |
| FinishingSteps + FinishingStepsPullDowns + FinishingStepsDetails | Process steps + entries + values | Detail ids are kept. Material picks are re-pointed to the same-named material of the step's own group. |
| FinishingSchedules + Steps + StepsValues | Process schedules + steps + schedule edits | Only schedule values that differ from the step value become schedule-level edits. Saved pricing / quantity inputs are kept. |
| MyWorkDefect / MyWorkAdder | Defect / Adder types | |
| MyWorkExecution + executed processes/lines/defects/adders | My Work executions, checklist lines, checks, defects, adders | Incomplete legacy runs stay "In progress". |
| Devices, DeviceCanisterConfiguration | Devices, canisters | |
| DeviceMetrics | Device readings | Last 30 days of recorded data only (the full history is 8.6 M rows). |
| WorkInstructions + Versions + Steps + Equipment/Materials/Related docs/Signatures | Work instructions | Latest version's content; every version is in the Edit Trail. |
| Documents + MaterialDocs/FormulaDocs/ProcessStepDocs/ProcessScheduleDocs | Documents + links | Metadata only — see "Files" below. |
| PhotoGallery + PhotoTags | Photo Gallery | Metadata only — see "Files" below. |
| Messages + MessageRecipients | DPM Center messages | |

Not imported: legacy audit/purge tables, printer/scale preferences, work-instruction step attachments (stored as
numeric keys without file names), group logos (upload them again in Groups → Edit).

After the import a System Administrator `admin` (password from `Seed:AdminPassword`, default `Admin@12345`) is created
if the legacy data has no user named `admin`.

## Calculations with legacy data

Material Quantities and Pricing understand the legacy calculation tags:

- `Material_ID` + `Material_Coverage` (sq ft/gal): base gallons = sq ft ÷ (coverage × gun transfer efficiency)
- `Gun_TE` (%): transfer efficiency of the step's spray gun
- `MiscN_ID` + `MiscN_Qty`: additive in fl oz/gal of base (gallons = base × oz ÷ 128), or consumable in sq ft per piece
  (pieces = sq ft ÷ value), or sq ft per quart
- `Step_Labor` / `Step_Setup` (min / sq ft): production time

## Files (documents and photos)

Uploaded files are not stored in the database. The import points them to:

```
<Storage:Root>\documents\<groupId>\legacy\<file name>
<Storage:Root>\photos\<groupId>\legacy\<file name>
```

`Storage:Root` defaults to `<site>\App_Data\uploads`. To make the old files available, copy them from the legacy web
server into those folders (one folder per group id). Until then, previews/downloads of legacy documents show
"File not found"; everything else works.
