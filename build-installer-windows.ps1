# Build Windows standalone executable and installer for MYLan.
# Requires:
#   - .NET 8 SDK
#   - Inno Setup 6 installed
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
  "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
  "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)

$iscc = $possibleIscc | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
  throw "Inno Setup Compiler was not found. Install Inno Setup 6, then run this script again."
}

Write-Host "Building installer with Inno Setup..."
& $iscc .\installer\windows\MYLan.iss

Write-Host "Done. Installer output is in: installer-output\windows"
