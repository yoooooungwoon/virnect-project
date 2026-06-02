@echo off
setlocal
cd /d "%~dp0"

set "MONI_PORT=47892"
set "ASPNETCORE_URLS=http://0.0.0.0:%MONI_PORT%"

echo.
echo moni-back monitoring server is starting...
echo Dashboard: http://127.0.0.1:%MONI_PORT%/
echo API:       http://127.0.0.1:%MONI_PORT%/api/status
echo.
echo Press Ctrl+C to stop the server.
echo.

dotnet run --project VirnectMonitor -c Release

echo.
echo Server stopped. Press any key to close this window.
pause >nul
