<#
.SYNOPSIS
  Starts the Finish Genius API (http://localhost:5080) and the Vite dev server (http://localhost:5173) in two windows.
#>
$root = $PSScriptRoot
$api = Join-Path $root 'backend\FinishGenius.Api'
$web = Join-Path $root 'frontend'

if (-not (Test-Path (Join-Path $api 'appsettings.Local.json'))) {
  Write-Warning 'backend\FinishGenius.Api\appsettings.Local.json is missing. Copy appsettings.Local.example.json and fill in the connection string + JWT key.'
  exit 1
}
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
  Write-Host 'Installing frontend packages (first run)...'
  Push-Location $web; npm install; Pop-Location
}

Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$api'; dotnet run --launch-profile http"
Start-Process powershell -ArgumentList '-NoExit', '-Command', "Set-Location '$web'; npm run dev"

Write-Host 'API:  http://localhost:5080   (health: /api/health)'
Write-Host 'UI:   http://localhost:5173   (sign in: admin / Admin@12345)'
Start-Sleep 6
Start-Process 'http://localhost:5173'
