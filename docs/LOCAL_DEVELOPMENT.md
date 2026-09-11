# Running Finish Genius locally

## Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 10.0.x | `dotnet --list-sdks` |
| Node.js | 20+ (tested with 24) | `node -v` |
| Git | any | `git --version` |
| Network access to the SQL Server | port 1433 to `34.74.178.204` | `Test-NetConnection 34.74.178.204 -Port 1433` |

No ODBC driver is needed — .NET uses Microsoft.Data.SqlClient directly.

## 1. Configure secrets (once)

`backend/FinishGenius.Api/appsettings.Local.json` is **git-ignored** and holds the connection strings and JWT key:

```json
{
  "Databases": {
    "Dev":  { "Label": "Development", "AutoMigrate": true,
              "ConnectionString": "Server=34.74.178.204;Database=FGAPP_21_May_2024;User Id=FGAPP;Password=********;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15" },
    "Prod": { "Label": "Production", "AutoMigrate": false, "Production": true, "Legacy": true,
              "ConnectionString": "Server=35.196.141.157;Database=FGAPP02232023_FULL_03012023_030118;User Id=FGAPP;Password=********;Encrypt=False;TrustServerCertificate=True;Connect Timeout=15" }
  },
  "Database": { "Default": "Dev" },
  "Jwt": { "Key": "a-long-random-secret-of-at-least-32-characters" },
  "Seed": { "AdminPassword": "Admin@12345" }
}
```

When more than one database is configured the sign-in page shows a **Database** switch (Development / Production).
The session stays on the database it signed in to; the header shows a badge (amber for `"Production": true`), and
signing out is how you switch. `"Legacy": true` (Production) uses the old database as-is: the app reads and writes
the old site's `dbo` tables directly (`Data/LegacyModel.cs`), with the same passwords as the old site. A single `ConnectionStrings:Default` (older config) still works as one "Dev" database.

Copy `appsettings.Local.example.json` to create it. (The ODBC string
`DRIVER={ODBC Driver 17 for SQL Server};SERVER=…;DATABASE=…;UID=…;PWD=…;Encrypt=no` maps to the
`Server=…;Database=…;User Id=…;Password=…;Encrypt=False` form above.)

## 2. Install frontend packages (once)

```powershell
cd frontend
npm install
```

## 3. Run

Easiest — opens two windows (API + UI):

```powershell
powershell -ExecutionPolicy Bypass -File .\run-local.ps1
```

Or manually in two terminals:

```powershell
# Terminal 1 – API on http://localhost:5080
cd backend\FinishGenius.Api
dotnet run --launch-profile http

# Terminal 2 – UI on http://localhost:5180 (proxies /api to 5080, hot reload)
cd frontend
npm run dev
```

Open http://localhost:5180 and sign in with **admin / Admin@12345**.

On first start the API automatically:
1. applies EF Core migrations (creates schema `fg` and its tables in the configured database),
2. seeds industry sectors, the Wood sub-step configuration, the System Administrator, and an **AWFI Demo Group**
   (sample materials, categories with calculation characteristics, 2 process steps, schedule "Bamboo", departments,
   defect/adder types) so every screen has data.

Set `"Database": { "AutoMigrate": false }` to disable automatic migration, and `"SeedDemoData": false` to skip the demo
group.

## Useful commands

```powershell
# type-check the frontend
cd frontend; npx tsc -b

# build the API
dotnet build backend\FinishGenius.sln

# add a database migration after changing Domain/* entities
dotnet ef migrations add <Name> --project backend\FinishGenius.Api --context AppDbContext -o Data/Migrations

# run the production build locally (API serves the SPA on http://localhost:5080)
cd frontend; npm run build; cd ..\backend\FinishGenius.Api; dotnet run --launch-profile http
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| `ConnectionStrings:Default is not configured` | Create `appsettings.Local.json` (step 1). |
| `Jwt:Key must be at least 32 characters` | Set a longer `Jwt:Key`. |
| SQL timeout / login failed | Check VPN/firewall to the SQL Server and the credentials. |
| UI shows network errors | Make sure the API is running on port 5080. |
| Port already in use | Stop the other process or change `applicationUrl` in `Properties/launchSettings.json` and the proxy in `frontend/vite.config.ts`. |
