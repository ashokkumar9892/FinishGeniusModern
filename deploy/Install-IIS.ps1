<#
.SYNOPSIS
  Installs / updates Finish Genius on a Windows Server with IIS. Run as Administrator ON THE SERVER.

.DESCRIPTION
  - Enables the IIS role features needed (if missing)
  - Checks the ASP.NET Core Hosting Bundle (.NET 10) is installed
  - Copies the package to the site folder (preserving App_Data uploads and appsettings.Local.json)
  - Creates/updates an application pool (No Managed Code) and the website + binding
  - Grants the app pool identity Modify rights on App_Data and logs

.EXAMPLE
  # From the extracted package folder:
  powershell -ExecutionPolicy Bypass -File .\Install-IIS.ps1 -SitePath C:\inetpub\FinishGenius -Port 80
  # With a host name:
  powershell -ExecutionPolicy Bypass -File .\Install-IIS.ps1 -HostName fg.mycompany.com
#>
param(
  [string]$SiteName = 'FinishGenius',
  [string]$SitePath = 'C:\inetpub\FinishGenius',
  [int]$Port = 80,
  [string]$HostName = '',
  [string]$PackagePath = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
function Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Run this script from an elevated (Administrator) PowerShell.'
}

Step 'Ensuring IIS features'
$isServer = (Get-CimInstance Win32_OperatingSystem).ProductType -ne 1
if ($isServer) {
  Install-WindowsFeature Web-Server, Web-Default-Doc, Web-Static-Content, Web-Http-Errors, Web-Http-Logging, Web-Stat-Compression, Web-Filtering, Web-Mgmt-Console, Web-WebSockets | Out-Null
} else {
  Enable-WindowsOptionalFeature -Online -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-StaticContent, IIS-DefaultDocument, IIS-HttpErrors, IIS-RequestFiltering, IIS-ManagementConsole, IIS-WebSockets | Out-Null
}
Import-Module WebAdministration

Step 'Checking ASP.NET Core Hosting Bundle'
$ancm = Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll"
$runtimes = & dotnet --list-runtimes 2>$null
if (-not $ancm -or -not ($runtimes -match 'Microsoft.AspNetCore.App 10\.')) {
  Write-Warning 'The .NET 10 Hosting Bundle is not installed. Download "ASP.NET Core Runtime 10.x - Windows Hosting Bundle" from https://dotnet.microsoft.com/download/dotnet/10.0, install it, run "iisreset", then re-run this script.'
  throw 'Hosting Bundle missing.'
}

Step "Copying files to $SitePath"
New-Item -ItemType Directory -Force $SitePath | Out-Null
if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) { Stop-Website -Name $SiteName -ErrorAction SilentlyContinue }
if (Test-Path "IIS:\AppPools\$SiteName") { Stop-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue; Start-Sleep 3 }
# Keep server-side data/config on upgrades
$keep = @('App_Data', 'logs', 'appsettings.Local.json', 'appsettings.Production.json')
Get-ChildItem $PackagePath -Force | Where-Object { $_.Name -notin @('Install-IIS.ps1') } | ForEach-Object {
  $target = Join-Path $SitePath $_.Name
  if ($_.Name -in $keep -and (Test-Path $target)) { Write-Host "  keeping existing $($_.Name)"; return }
  Copy-Item $_.FullName $target -Recurse -Force
}
New-Item -ItemType Directory -Force (Join-Path $SitePath 'App_Data\uploads'), (Join-Path $SitePath 'logs') | Out-Null

# The existing appsettings.Local.json is kept, but sections added in a newer package (e.g. "Owner") are copied into it.
# Settings already on the server are never changed; a backup is written first.
$packageLocal = Join-Path $PackagePath 'appsettings.Local.json'
$siteLocal = Join-Path $SitePath 'appsettings.Local.json'
if ((Test-Path $packageLocal) -and (Test-Path $siteLocal)) {
  $fromPackage = Get-Content $packageLocal -Raw | ConvertFrom-Json
  $onServer = Get-Content $siteLocal -Raw | ConvertFrom-Json
  $missing = @($fromPackage.PSObject.Properties | Where-Object { -not $onServer.PSObject.Properties[$_.Name] })
  if ($missing.Count -gt 0) {
    Copy-Item $siteLocal "$siteLocal.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')" -Force
    foreach ($section in $missing) { $onServer | Add-Member -NotePropertyName $section.Name -NotePropertyValue $section.Value }
    $onServer | ConvertTo-Json -Depth 20 | Set-Content $siteLocal -Encoding UTF8
    Write-Host "  added to appsettings.Local.json: $(($missing | ForEach-Object Name) -join ', ') (backup kept next to it)"
  }
}

if (-not (Test-Path (Join-Path $SitePath 'appsettings.Local.json'))) {
  Write-Warning "appsettings.Local.json not found in $SitePath. Copy appsettings.Local.example.json to appsettings.Local.json and set the connection string + JWT key before browsing the site."
}

Step 'Configuring application pool'
if (-not (Test-Path "IIS:\AppPools\$SiteName")) { New-WebAppPool -Name $SiteName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$SiteName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)

Step 'Configuring website'
if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
  New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $SiteName -Port $Port -HostHeader $HostName | Out-Null
} else {
  Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $SitePath
  Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
}
Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true

Step 'Granting folder permissions'
$identity = "IIS AppPool\$SiteName"
foreach ($dir in 'App_Data', 'logs') {
  icacls (Join-Path $SitePath $dir) /grant "${identity}:(OI)(CI)M" /T /Q | Out-Null
}
icacls $SitePath /grant "${identity}:(OI)(CI)RX" /Q | Out-Null

Step 'Opening Windows Firewall port'
if (-not (Get-NetFirewallRule -DisplayName "FinishGenius HTTP $Port" -ErrorAction SilentlyContinue)) {
  New-NetFirewallRule -DisplayName "FinishGenius HTTP $Port" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
}

Step 'Starting site'
Start-WebAppPool -Name $SiteName
Start-Website -Name $SiteName
Start-Sleep 5
try {
  $url = "http://localhost:$Port/api/health"
  $r = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 60 -Headers @{ Host = $(if ($HostName) { $HostName } else { 'localhost' }) }
  Write-Host "Health check OK: $($r.Content)" -ForegroundColor Green
} catch {
  Write-Warning "Health check failed: $($_.Exception.Message). Set stdoutLogEnabled=""true"" in web.config and check $SitePath\logs, or the Windows Event Log (Application)."
}
Write-Host "`nDone. Browse to http://$(if ($HostName) { $HostName } else { 'localhost' }):$Port" -ForegroundColor Green
