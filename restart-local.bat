@echo off
rem Restarts Finish Genius locally: stops the API (port 5080) and the UI (port 5180), then starts both again.
rem Double-click it, or run restart-local.bat from a command prompt.
setlocal
set "ROOT=%~dp0"
set "API=%ROOT%backend\FinishGenius.Api"
set "WEB=%ROOT%frontend"

if not exist "%API%\appsettings.Local.json" (
  echo appsettings.Local.json is missing in backend\FinishGenius.Api.
  echo Copy appsettings.Local.example.json to appsettings.Local.json and fill in the connection strings + JWT key.
  pause
  exit /b 1
)

echo Stopping Finish Genius...
taskkill /FI "WINDOWTITLE eq FG API*" /T /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq FG UI*" /T /F >nul 2>&1
taskkill /IM FinishGenius.Api.exe /T /F >nul 2>&1
for %%P in (5080 5180) do (
  for /f "tokens=5" %%I in ('netstat -ano ^| findstr /R /C:":%%P .*LISTENING"') do taskkill /PID %%I /T /F >nul 2>&1
)
ping -n 3 127.0.0.1 >nul

if not exist "%WEB%\node_modules" (
  echo Installing frontend packages - first run...
  pushd "%WEB%"
  call npm install
  popd
)

echo Starting API on http://localhost:5080 ...
start "FG API" /D "%API%" cmd /k dotnet run --launch-profile http
echo Starting UI on http://localhost:5180 ...
start "FG UI" /D "%WEB%" cmd /k npm run dev

echo Waiting for the API to come up...
set /a TRIES=0
:wait
set /a TRIES+=1
curl -s -o nul http://localhost:5080/api/health && goto ready
if %TRIES% geq 90 goto slow
ping -n 3 127.0.0.1 >nul
goto wait

:slow
echo The API has not answered after 3 minutes - check the "FG API" window for errors.
goto open

:ready
echo API is up.

:open
start "" http://localhost:5180
echo.
echo   UI:   http://localhost:5180   (sign in: admin / Admin@12345, choose Development or Production)
echo   API:  http://localhost:5080/api/health
echo   Run restart-local.bat again to restart; close the "FG API" and "FG UI" windows to stop.
endlocal
