@echo off
setlocal
cd /d "%~dp0"

echo Building MYLan Windows installer...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer-windows.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Make sure the .NET 8 SDK and Inno Setup 6 are installed.
  pause
  exit /b 1
)

echo.
echo Done.
echo Installer:
echo installer-output\windows\MYLan-Setup-2.1.0-win-x64.exe
pause
