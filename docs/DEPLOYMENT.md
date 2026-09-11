# Deploying Finish Genius to Windows Server / IIS (e.g. Google Cloud Windows VM)

The application is **one IIS website**: the ASP.NET Core API serves the React app from `wwwroot` and all `/api/*` calls.

## 0. What you need

- A Windows Server 2019/2022 VM (Google Compute Engine "Windows Server 2022 Datacenter" image works) with RDP access.
- Network path from the VM to SQL Server `34.74.178.204:1433`
  (if SQL Server is another GCP VM, allow the web VM's internal/external IP in its firewall rule).
- The build package produced by `build.ps1` (`publish\FinishGenius-<date>.zip`).

## 1. Build the package (on your dev machine)

```powershell
cd C:\AWFI\FinishGeniusModern
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output:
- `publish\FinishGenius\` — the site files (`FinishGenius.Api.dll`, `web.config`, `wwwroot\`, `appsettings*.json`,
  `FinishGenius_schema.sql`, `Install-IIS.ps1`, this guide)
- `publish\FinishGenius-<date>.zip` — the same, zipped

By default the package **includes `appsettings.Local.json`** (connection string + JWT key) so it runs immediately.
Use `.\build.ps1 -IncludeLocalSettings:$false` to ship without secrets and create that file on the server instead.
Keep the zip private.

## 2. Prepare the server (once)

1. **Google Cloud firewall**: VPC network → Firewall → allow `tcp:80` (and `tcp:443` if you add HTTPS) to the VM
   (network tag e.g. `http-server`). Tick "Allow HTTP traffic" on the VM.
2. RDP to the VM and open **PowerShell as Administrator**.
3. Install the **ASP.NET Core Runtime 10.x – Windows Hosting Bundle**:
   https://dotnet.microsoft.com/download/dotnet/10.0 → *ASP.NET Core Runtime* → *Hosting Bundle*. Then run `iisreset`.
   (If IIS is not installed yet, run step 3 first — the script installs IIS — then install the bundle, `iisreset`, and
   run the script again.)

## 3. Install / update the site

Copy the zip to the server, extract it (e.g. `C:\deploy\FinishGenius`), then:

```powershell
cd C:\deploy\FinishGenius
powershell -ExecutionPolicy Bypass -File .\Install-IIS.ps1 -SiteName FinishGenius -SitePath C:\inetpub\FinishGenius -Port 80
# optional host name binding:  -HostName fg.yourcompany.com
```

The script:
- enables IIS features, verifies the Hosting Bundle,
- copies files to `C:\inetpub\FinishGenius` (on upgrades it **keeps** `App_Data` uploads, `logs` and
  `appsettings.Local.json` already on the server),
- creates the app pool `FinishGenius` (*No Managed Code*, AlwaysRunning) and the website,
- grants the app-pool identity *Modify* on `App_Data` and `logs`,
- opens the Windows Firewall port and calls `/api/health`.

Browse to `http://<VM external IP>/` and sign in (`admin` / the `Seed:AdminPassword`, default `Admin@12345`).
**Change the admin password immediately** (user menu → Profile & password).

### Manual IIS setup (alternative to the script)

1. Copy `publish\FinishGenius\*` to `C:\inetpub\FinishGenius`.
2. IIS Manager → Application Pools → Add: name `FinishGenius`, .NET CLR version **No Managed Code**.
3. Sites → Add Website: name `FinishGenius`, physical path `C:\inetpub\FinishGenius`, app pool `FinishGenius`, port 80.
4. Folder permissions: give `IIS AppPool\FinishGenius` **Modify** on `C:\inetpub\FinishGenius\App_Data` and `\logs`.
5. Make sure `appsettings.Local.json` exists in the site folder with the connection string and a JWT key.

## 4. Configuration reference (`appsettings.Local.json` in the site folder)

```json
{
  "Databases": {
    "Dev":  { "Label": "Development", "AutoMigrate": true,
              "ConnectionString": "Server=34.74.178.204;Database=FGAPP_21_May_2024;User Id=FGAPP;Password=***;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15" },
    "Prod": { "Label": "Production", "AutoMigrate": false, "Production": true, "Legacy": true,
              "ConnectionString": "Server=35.196.141.157;Database=FGAPP02232023_FULL_03012023_030118;User Id=FGAPP;Password=***;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15" }
  },
  "Jwt": { "Key": "<long random secret, 32+ chars — keep the same value across servers/restarts>" },
  "Seed": { "AdminPassword": "<initial admin password, used only when no users exist>" },
  "Database": { "Default": "Dev", "AutoMigrate": true, "SeedDemoData": false },
  "Storage": { "Root": "" }
}
```

| Setting | Meaning |
|---|---|
| `Databases:<Key>` | Databases offered on the sign-in page (only shown when there are 2+). `Label` is what users see, `Production: true` adds the live-data warning and an amber header badge. A lone `ConnectionStrings:Default` still works (one "Dev" database). |
| `Databases:<Key>:AutoMigrate` | Create/update that database's `fg` schema on startup (default = `Database:AutoMigrate`; always off for `Legacy` databases). |
| `Databases:<Key>:Legacy` | `true` = use the old Finish Genius database as-is: the app reads and writes the old `dbo` tables directly (shared with the old site, which keeps working; passwords are stored as bcrypt so both sites accept them). It never creates `fg` tables and is never migrated. Screens not connected to the old tables yet show a message. Mapping: `Data/LegacyModel.cs`. Prod is configured this way. |
| `Database:Default` | Database used when none is chosen (and by the command-line tools without `--db`). |
| `Database:AutoMigrate` | `true` = the app creates/updates the `fg` schema on startup. Set `false` if a DBA runs `FinishGenius_schema.sql` (idempotent) instead. |
| `Database:SeedDemoData` | Default `false`. `true` creates an "AWFI Demo Group" with sample data (for a brand-new, empty database). |
| `Storage:Root` | Upload folder. Empty = `<site>\App_Data\uploads`. Can be a larger disk, e.g. `D:\FinishGeniusData`. Back it up. |
| `Jwt:ExpiryHours` / `RememberMeDays` | Session lifetime. |

A different database: change `Database=` in the connection string — the schema is created automatically (the SQL login
needs `db_owner`, or `db_ddladmin` + read/write for migrations).

### Production (legacy database) — what differs

Production is the old site's database used as-is (`"Legacy": true`, mapping in `Data/LegacyModel*.cs`):

- Connected so far: sign-in, groups, users, profile, DPM Center, material categories, departments, vendors, locations,
  Equipment & Materials (list, inventory, reorder / purchase orders, order history), environmental report, bulk
  import, formulas (list and editor), and the documents library.
- Stock uses the old batches: `MaterialBatches.BatchQty` is the on-hand quantity and `MaterialQuantityChanges` the
  history, written exactly like the old site (add to a batch = new quantity + change row; new batch = quantity only).
- A formula is its own material row (`dbo.Materials`, Discriminator `Formulation`); no separate "mirror" row is made.
- Ingredients keep the order they were added in (the old tables have no ordering column).
- Sending jobs to dispense machines, scales and label printers, and changing purge settings, are refused with a
  message: those devices are connected to the old site.
- Old document files live on the old server; copy `AppData/Documents/{group}/` to
  `<Storage:Root>/documents/{group}/legacy/` to preview them here. Files uploaded here are stored by this app, so the
  old site cannot open them.
- Old data contains a few corrupt quantities (up to 10^28 gallons); values that do not fit are shown as 0.

### Pointing at a different database / importing legacy data

- To use another database, change `ConnectionStrings:Default` in `appsettings.Local.json` and recycle the app pool —
  the `fg` schema is created automatically on startup.
- To (re)load the real data from the legacy `FGAPP` database on the same SQL Server, run from the site folder:
  `dotnet FinishGenius.Api.dll import-legacy --source FGAPP --yes` (stop the site first; this replaces all `fg` data).
  See [LEGACY_IMPORT.md](LEGACY_IMPORT.md), including where to copy legacy document/photo files.
- Both commands accept `--db <Key>` to work on another configured database. They write to it, so don't run them
  against a `Legacy` database (Production) unless you have decided to give it its own `fg` tables.

## 5. HTTPS (recommended)

- Obtain a certificate (e.g. win-acme / Let's Encrypt: https://www.win-acme.com, or your corporate cert), bind it to
  the site on port 443 in IIS (Site → Bindings → https), open `tcp:443` in the GCP firewall.
- Optionally add the IIS URL Rewrite HTTP→HTTPS redirect rule.

## 6. Updating to a new version

1. Run `build.ps1` on the dev machine, copy the new zip to the server, extract to a new folder.
2. Run `Install-IIS.ps1` again from that folder (it stops the site, copies files, keeps data/config, restarts).
3. Database changes are applied automatically on startup (or run the new `FinishGenius_schema.sql` first if
   AutoMigrate is off).

## 7. Troubleshooting

| Symptom | Fix |
|---|---|
| **HTTP 500.19** | Hosting Bundle missing → install it, `iisreset`. |
| **HTTP 500.30 / 500.31** (app failed to start) | Usually a bad connection string or JWT key. In `web.config` set `stdoutLogEnabled="true"`, recycle the app pool, read `logs\stdout_*.log`; also check *Event Viewer → Windows Logs → Application*. |
| **HTTP 502.5** | Wrong runtime — install the .NET **10** Hosting Bundle. |
| Uploads fail | App-pool identity needs Modify on `App_Data` (or your `Storage:Root`). Large files: limit is 250 MB (`web.config` `maxAllowedContentLength`). |
| Deep links (e.g. `/materials`) return 404 | Make sure `wwwroot\index.html` exists in the site folder (package built with `build.ps1`). |
| Can't reach SQL Server | From the VM: `Test-NetConnection 34.74.178.204 -Port 1433`; open the SQL Server firewall for the VM IP. |
| Everyone logged out after deploy | `Jwt:Key` changed — keep it constant. |

Health endpoint: `GET /api/health` → `{"status":"ok"}`.

## 8. Devices (optional)

Shop-floor devices / network bridges post readings to `POST /api/devices/ingest` with header `X-Api-Key: <device API key>`
(shown in Dashboard → Devices → Edit). Allow that traffic to the server if devices are on another network.
