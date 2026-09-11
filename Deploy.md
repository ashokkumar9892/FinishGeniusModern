# Deploy Finish Genius on Windows (IIS)

Finish Genius runs as **one IIS website**: the ASP.NET Core API also serves the React app from `wwwroot`.

## 1. Prerequisites

**Build machine** (your dev PC)
- .NET 10 SDK
- Node.js (LTS) + npm

**Server** (Windows Server 2019/2022 or Windows 10/11)
- Administrator access (RDP)
- Network access to the SQL Server on port `1433`
- **ASP.NET Core Runtime 10.x – Windows Hosting Bundle**
  (https://dotnet.microsoft.com/download/dotnet/10.0 → *ASP.NET Core Runtime* → *Hosting Bundle*)

## 2. Build the package (on the dev machine)

```powershell
cd C:\AWFI\FinishGeniusModern
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output:
- `publish\FinishGenius\` – site files
- `publish\FinishGenius-<date>.zip` – the same, zipped (copy this to the server)

> The package includes `appsettings.Local.json` (DB password + JWT key) by default. Keep the zip private,
> or build without secrets using `.\build.ps1 -IncludeLocalSettings:$false` and create that file on the server.

## 3. Prepare the server (first time only)

Open **PowerShell as Administrator** on the server.

1. **Install IIS**: running `Install-IIS.ps1` (step 4) enables IIS automatically. Or install it yourself:
   - Windows Server: `Install-WindowsFeature Web-Server -IncludeManagementTools`
   - Windows 10/11: *Turn Windows features on or off* → **Internet Information Services**
2. **Install the .NET 10 Hosting Bundle**, then run:
   ```powershell
   iisreset
   ```
3. **Open the firewall** for port 80 (and 443 for HTTPS). On a cloud VM (e.g. Google Cloud), also allow
   `tcp:80` / `tcp:443` in the cloud firewall.

## 4. Install the site with the script (recommended)

Copy the zip to the server, extract it (e.g. to `C:\deploy\FinishGenius`), then run:

```powershell
cd C:\deploy\FinishGenius
powershell -ExecutionPolicy Bypass -File .\Install-IIS.ps1 -SiteName FinishGenius -SitePath C:\inetpub\FinishGenius -Port 80
# optional host name:  -HostName fg.yourcompany.com
```

The script:
- enables IIS features and checks for the Hosting Bundle
- copies the files to `C:\inetpub\FinishGenius` (keeps the existing `App_Data`, `logs` and `appsettings.Local.json`)
- creates the app pool **FinishGenius** (No Managed Code, AlwaysRunning) and the website
- grants the app pool **Modify** rights on `App_Data` and `logs`
- opens the Windows Firewall port and calls `/api/health`

## 5. Manual IIS setup (alternative to the script)

1. Copy the contents of `publish\FinishGenius\` to `C:\inetpub\FinishGenius`.
2. **IIS Manager → Application Pools → Add Application Pool**
   - Name: `FinishGenius`
   - .NET CLR version: **No Managed Code**
   - Pipeline mode: Integrated
   - Advanced Settings: *Start Mode* = `AlwaysRunning`, *Idle Time-out* = `0`
3. **IIS Manager → Sites → Add Website**
   - Site name: `FinishGenius`
   - Application pool: `FinishGenius`
   - Physical path: `C:\inetpub\FinishGenius`
   - Binding: `http`, port `80`, optional host name
4. **Folder permissions**: create the `App_Data\uploads` and `logs` folders if missing, then give the app pool
   Modify rights on them:
   ```powershell
   icacls C:\inetpub\FinishGenius\App_Data /grant "IIS AppPool\FinishGenius:(OI)(CI)M" /T
   icacls C:\inetpub\FinishGenius\logs     /grant "IIS AppPool\FinishGenius:(OI)(CI)M" /T
   ```
5. Make sure `appsettings.Local.json` exists in the site folder (see step 6), then start the site.

## 6. Configuration – `appsettings.Local.json`

Put this file in the site folder (`C:\inetpub\FinishGenius`). Start from `appsettings.Local.example.json`:

```json
{
  "Databases": {
    "Dev":  { "Label": "Development", "ConnectionString": "Server=SQL_SERVER;Database=DEV_DB;User Id=USER;Password=PASSWORD;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15", "AutoMigrate": true },
    "Prod": { "Label": "Production",  "ConnectionString": "Server=SQL_SERVER;Database=PROD_DB;User Id=USER;Password=PASSWORD;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15", "AutoMigrate": false, "Production": true }
  },
  "Database": { "Default": "Dev" },
  "Jwt":      { "Key": "LONG-RANDOM-SECRET-AT-LEAST-32-CHARS" },
  "Seed":     { "AdminPassword": "Admin@12345" }
}
```

| Setting | Meaning |
|---|---|
| `Databases:<key>` | Each database users can sign in to. `AutoMigrate: true` creates/updates the `fg` schema on startup. `Production: true` shows a "live data" warning in the UI. |
| `Database:Default` | Which database is used when none is chosen. |
| `Jwt:Key` | Secret that signs login tokens. Keep it the same across deploys, or every user is logged out. |
| `Seed:AdminPassword` | Initial `admin` password. Only used when the database has no users yet. |

A single `"ConnectionStrings": { "Default": "..." }` (older format) also still works.

After changing this file, **recycle the app pool** (IIS Manager → Application Pools → FinishGenius → Recycle).

## 7. Verify

- Health check: `http://<server>/api/health` → `{"status":"ok"}`
- Browse to `http://<server>/` and sign in with **admin / Admin@12345** (or your `Seed:AdminPassword`).
- **Change the admin password right away** (user menu → *Profile & password*).

## 8. Update to a new version

1. Run `build.ps1` again on the dev machine.
2. Copy the new zip to the server and extract it to a new folder.
3. Run `Install-IIS.ps1` from that folder. It stops the site, copies the files, keeps your data and config, and restarts.
4. For databases with `AutoMigrate: true`, schema changes are applied on startup. Otherwise run the included
   `FinishGenius_schema.sql` (safe to re-run) on that database first.

## 9. HTTPS (recommended)

1. Get a certificate (corporate cert, or free Let's Encrypt via https://www.win-acme.com).
2. IIS Manager → site **FinishGenius** → **Bindings** → Add → `https`, port `443`, select the certificate.
3. Open port 443 in the Windows and cloud firewalls.

## 10. Troubleshooting

| Problem | Fix |
|---|---|
| HTTP **500.19** | Hosting Bundle missing → install it, then `iisreset`. |
| HTTP **500.30 / 500.31** | The app failed to start (usually a bad connection string or JWT key). In `web.config` set `stdoutLogEnabled="true"`, recycle the app pool, then read `logs\stdout_*.log` and *Event Viewer → Windows Logs → Application*. |
| HTTP **502.5** | Wrong .NET version → install the **.NET 10** Hosting Bundle. |
| Uploads fail | The app pool needs Modify rights on `App_Data`. The max upload size is 250 MB (`web.config`). |
| Cannot reach SQL Server | On the server, run `Test-NetConnection <sql-server-ip> -Port 1433` and allow the web server's IP in the SQL firewall. |
| Everyone logged out after deploy | `Jwt:Key` changed. Keep it constant. |

For more detail, see [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md).
