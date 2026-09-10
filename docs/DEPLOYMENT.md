# Deploying Finish Genius to Windows Server / IIS (e.g. Google Cloud Windows VM)

The application is **one IIS website**: the ASP.NET Core API serves the React app from `wwwroot` and all `/api/*` calls.

## 0. What you need

- A Windows Server 2019/2022 VM (Google Compute Engine "Windows Server 2022 Datacenter" image works) with RDP access.
- Network path from the VM to SQL Server `35.196.141.157:1433`
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
  "ConnectionStrings": { "Default": "Server=35.196.141.157;Database=FGApp-AshokTest;User Id=FGAPP;Password=***;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15" },
  "Jwt": { "Key": "<long random secret, 32+ chars — keep the same value across servers/restarts>" },
  "Seed": { "AdminPassword": "<initial admin password, used only when no users exist>" },
  "Database": { "AutoMigrate": true, "SeedDemoData": true },
  "Storage": { "Root": "" }
}
```

| Setting | Meaning |
|---|---|
| `Database:AutoMigrate` | `true` = the app creates/updates the `fg` schema on startup. Set `false` if a DBA runs `FinishGenius_schema.sql` (idempotent) instead. |
| `Database:SeedDemoData` | Default `false`. `true` creates an "AWFI Demo Group" with sample data (for a brand-new, empty database). |
| `Storage:Root` | Upload folder. Empty = `<site>\App_Data\uploads`. Can be a larger disk, e.g. `D:\FinishGeniusData`. Back it up. |
| `Jwt:ExpiryHours` / `RememberMeDays` | Session lifetime. |

A different database: change `Database=` in the connection string — the schema is created automatically (the SQL login
needs `db_owner`, or `db_ddladmin` + read/write for migrations).

### Pointing at a different database / importing legacy data

- To use another database, change `ConnectionStrings:Default` in `appsettings.Local.json` and recycle the app pool —
  the `fg` schema is created automatically on startup.
- To (re)load the real data from the legacy `FGAPP` database on the same SQL Server, run from the site folder:
  `dotnet FinishGenius.Api.dll import-legacy --source FGAPP --yes` (stop the site first; this replaces all `fg` data).
  See [LEGACY_IMPORT.md](LEGACY_IMPORT.md), including where to copy legacy document/photo files.

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
| Can't reach SQL Server | From the VM: `Test-NetConnection 35.196.141.157 -Port 1433`; open the SQL Server firewall for the VM IP. |
| Everyone logged out after deploy | `Jwt:Key` changed — keep it constant. |

Health endpoint: `GET /api/health` → `{"status":"ok"}`.

## 8. Devices (optional)

Shop-floor devices / network bridges post readings to `POST /api/devices/ingest` with header `X-Api-Key: <device API key>`
(shown in Dashboard → Devices → Edit). Allow that traffic to the server if devices are on another network.
