# Finish Genius (Modern)

Web platform for AWFI industrial-coatings finishing: equipment & materials inventory, formulas, process steps and
schedules, material quantity and pricing estimates, shop-floor "My Work" execution, dashboards/devices and controlled
work instructions.

| Layer | Technology |
|---|---|
| Frontend | React 19, TypeScript, Vite, Tailwind CSS, TanStack Query, Recharts, lucide icons |
| Backend | ASP.NET Core 10 Web API, EF Core 10, JWT auth, ClosedXML (Excel) |
| Database | SQL Server (all tables in schema **`fg`** so they coexist with other tables in the same database) |
| Hosting | IIS on Windows Server (single site: the API also serves the built SPA) |

## Quick start (local)

```powershell
# 1. secrets (once): connection string + JWT key
copy backend\FinishGenius.Api\appsettings.Local.example.json backend\FinishGenius.Api\appsettings.Local.json
#    ...edit it (the dev machine already has one)

# 2. run API (http://localhost:5080) and UI (http://localhost:5173)
powershell -ExecutionPolicy Bypass -File .\run-local.ps1
```

Sign in with **admin / Admin@12345** (created on first start — change it under *Profile & password*).

Full guide: [docs/LOCAL_DEVELOPMENT.md](docs/LOCAL_DEVELOPMENT.md)

## Real data from the legacy app

```powershell
cd backend\FinishGenius.Api
dotnet run -- import-legacy --source FGAPP_21_May_2024 --yes
```

Copies groups, users (existing passwords keep working), materials, formulas, process steps/schedules, My Work history,
devices, work instructions, documents/photos metadata and messages from the legacy `FGAPP` database into the `fg`
schema of the configured database. Details: [docs/LEGACY_IMPORT.md](docs/LEGACY_IMPORT.md)

## Build for production

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Produces `publish\FinishGenius\` and `publish\FinishGenius-<date>.zip` (IIS-ready). Deployment to a Windows/IIS server
(e.g. a Google Cloud Windows VM): [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)

## Repository layout

```
backend/FinishGenius.Api   API, EF model + migrations, seeding, services
frontend/                  React SPA (builds into backend/FinishGenius.Api/wwwroot)
database/                  generated idempotent SQL schema script
deploy/                    IIS install script
docs/                      requirements (from the AWFI test documents), conventions, guides
build.ps1 / run-local.ps1  build & run helpers
```

## Modules

Groups · Users & roles · DPM Center (messages) · Photo Gallery · Equipment & Materials (inventory, vendors, locations,
reorder/purchase orders, environmental report, Excel bulk upload) · Material Categories & characteristics · Formulas ·
Documents library · Sub Step Setup · Process Steps (step builder) · Process Schedules (clone / copy master / bulk copy) ·
Material Quantities · Pricing · My Work (shop-floor checklists, defects, adders) · Dashboard (KPIs, departments, devices,
telemetry) · Work Instructions (versioned, media steps, print slides).

Role access follows the matrix in [docs/requirements/01-groups-users-materials.md](docs/requirements/01-groups-users-materials.md).
