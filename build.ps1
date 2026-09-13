<#
.SYNOPSIS
  Builds Finish Genius (React frontend + ASP.NET Core API) into a single IIS-ready folder and zip.

.DESCRIPTION
  1. npm ci + vite build  -> backend/FinishGenius.Api/wwwroot
  2. dotnet publish (Release, framework-dependent, win-x64) -> publish/FinishGenius
  3. Generates database/FinishGenius_schema.sql (idempotent migration script)
  4. Zips the output -> publish/FinishGenius-<version>.zip

.PARAMETER IncludeLocalSettings
  Copy backend/FinishGenius.Api/appsettings.Local.json (connection string + JWT key) into the package.
  Default: $true. Use -IncludeLocalSettings:$false to ship without secrets and create the file on the server.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
param(
  [bool]$IncludeLocalSettings = $true,
  [switch]$SkipNpmInstall
)

# 'Continue' on purpose: Windows PowerShell 5.1 turns harmless stderr output of native tools (npm/vite warnings)
# into terminating errors under 'Stop'. Failures are detected through $LASTEXITCODE below instead.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$api = Join-Path $root 'backend\FinishGenius.Api'
$out = Join-Path $root 'publish\FinishGenius'
$version = Get-Date -Format 'yyyy.MM.dd-HHmm'

function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

Step 'Checking tools'
foreach ($tool in 'node', 'npm', 'dotnet') {
  if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { throw "$tool is not installed or not on PATH." }
}
Write-Host "node $(node -v) | dotnet $(dotnet --version)"

Step 'Building frontend (React + Vite)'
$web = Join-Path $root 'frontend'
Push-Location $web
try {
  # npm ci deletes node_modules first: skip it when the installed packages already match package-lock.json.
  $installed = Join-Path $web 'node_modules\.package-lock.json'
  $upToDate = (Test-Path $installed) -and ((Get-Item $installed).LastWriteTime -ge (Get-Item (Join-Path $web 'package-lock.json')).LastWriteTime)
  if ($SkipNpmInstall -or $upToDate) {
    Write-Host 'Frontend packages are up to date (npm ci skipped).'
  } else {
    # A running local UI (restart-local.bat's "FG UI" window) keeps native files in node_modules open; npm ci would
    # fail halfway through deleting the folder.
    $devServer = Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.CommandLine -like "*$web*" }
    if ($devServer) { throw 'The local UI ("FG UI" window, Vite dev server) is running and locks frontend packages. Close that window and run build.ps1 again.' }
    npm ci --no-audit --no-fund; if ($LASTEXITCODE) { throw 'npm ci failed' }
  }
  npm run build; if ($LASTEXITCODE) { throw 'Frontend build failed' }
} finally { Pop-Location }

Step 'Publishing API (dotnet publish -c Release)'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish (Join-Path $api 'FinishGenius.Api.csproj') -c Release -r win-x64 --self-contained false -o $out /p:EnvironmentName=Production
if ($LASTEXITCODE) { throw 'dotnet publish failed' }

$local = Join-Path $api 'appsettings.Local.json'
if ($IncludeLocalSettings -and (Test-Path $local)) {
  Copy-Item $local $out -Force
  Write-Warning 'appsettings.Local.json (database password + JWT key) was copied into the package. Keep the zip private.'
} else {
  Remove-Item (Join-Path $out 'appsettings.Local.json') -ErrorAction SilentlyContinue
  Write-Host 'No secrets included: create appsettings.Local.json on the server (see appsettings.Local.example.json).'
}
New-Item -ItemType Directory -Force (Join-Path $out 'logs') | Out-Null
New-Item -ItemType Directory -Force (Join-Path $out 'App_Data\uploads') | Out-Null

Step 'Generating idempotent database script'
$sql = Join-Path $root 'database\FinishGenius_schema.sql'
if (-not (Get-Command dotnet-ef -ErrorAction SilentlyContinue)) { dotnet tool install --global dotnet-ef | Out-Null }
# Release configuration: a locally running dev instance keeps the Debug output locked.
dotnet ef migrations script --idempotent --project $api --context AppDbContext --configuration Release --output $sql
if ($LASTEXITCODE) { Write-Warning 'Could not generate the SQL script (the app still migrates itself on startup).' }
else { Copy-Item $sql $out -Force }

Copy-Item (Join-Path $root 'deploy\Install-IIS.ps1') $out -Force
Copy-Item (Join-Path $root 'docs\DEPLOYMENT.md') $out -Force

Step 'Creating zip'
$zip = Join-Path $root "publish\FinishGenius-$version.zip"
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
Write-Host "`nBuild complete." -ForegroundColor Green
Write-Host "  Folder: $out"
Write-Host "  Zip:    $zip"
