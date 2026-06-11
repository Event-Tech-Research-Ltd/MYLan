# Build Windows standalone executable and installer for MYLan.
# Requires:
#   - .NET 8 SDK
#   - Inno Setup 6 or 7 installed
# Run from the repository root in PowerShell.

$ErrorActionPreference = "Stop"

Write-Host "Publishing MYLan for Windows x64..."
dotnet publish .\MYLan\MYLan.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\win-x64

$possibleIscc = @(
  "$env:ProgramFiles(x86)\Inno Setup 7\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $possibleIscc | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw "Inno Setup Compiler was not found. Install Inno Setup 6 or 7, then run this script again."
}

Write-Host "Building installer with Inno Setup..."
& $iscc .\installer\windows\MYLan.iss
if ($LASTEXITCODE -ne 0) {
  throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$expectedInstaller = ".\installer-output\windows\MYLan-Setup-2.1.0-win-x64.exe"
if (-not (Test-Path $expectedInstaller)) {
  throw "Inno Setup finished but the expected installer was not created: $expectedInstaller"
}

Write-Host "Done. Installer output is in: installer-output\windows"
