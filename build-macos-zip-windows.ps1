# Build macOS self-contained app bundles and package them into a Mac-ready zip.
# This can run on Windows. To create real DMG files, run CREATE-MACOS-DMGS.command on a Mac.

$ErrorActionPreference = "Stop"

$Version = "2.1.0"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$OutputRoot = Join-Path $Root "installer-output\macos"
$ZipPath = Join-Path $OutputRoot "MYLan-$Version-macos-universal-ready.zip"
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Write-UnixText {
  param(
    [Parameter(Mandatory=$true)][string]$Path,
    [Parameter(Mandatory=$true, ValueFromPipeline=$true)][string]$Text
  )

  process {
    [System.IO.File]::WriteAllText($Path, ($Text -replace "`r`n", "`n"), $Utf8NoBom)
  }
}

function New-MacAppBundle {
  param(
    [Parameter(Mandatory=$true)][string]$Rid
  )

  $publishDir = Join-Path $Root "release\$Rid"
  $appDir = Join-Path $OutputRoot "$Rid\MYLan.app"
  $macosDir = Join-Path $appDir "Contents\MacOS"
  $resourcesDir = Join-Path $appDir "Contents\Resources"
  $plistPath = Join-Path $appDir "Contents\Info.plist"

  $exe = Join-Path $publishDir "MYLan"
  if (-not (Test-Path $exe)) {
    throw "Missing macOS executable: $exe"
  }

  if (Test-Path $appDir) {
    Remove-Item -LiteralPath $appDir -Recurse -Force
  }

  New-Item -ItemType Directory -Path $macosDir, $resourcesDir | Out-Null
  Copy-Item -Path (Join-Path $publishDir "*") -Destination $macosDir -Recurse -Force

  @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>MYLan</string>
  <key>CFBundleDisplayName</key>
  <string>MYLan DHCP Field Server</string>
  <key>CFBundleIdentifier</key>
  <string>io.eventresearch.mylan</string>
  <key>CFBundleVersion</key>
  <string>$Version</string>
  <key>CFBundleShortVersionString</key>
  <string>$Version</string>
  <key>CFBundleExecutable</key>
  <string>MYLan</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>10.15</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@ | Write-UnixText -Path $plistPath
}

function Write-MacReleaseScripts {
  $releaseDir = Join-Path $OutputRoot "macos-ready"
  if (Test-Path $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
  }
  New-Item -ItemType Directory -Path $releaseDir | Out-Null

  Copy-Item -Path (Join-Path $OutputRoot "osx-arm64\MYLan.app") -Destination (Join-Path $releaseDir "MYLan-arm64.app") -Recurse -Force
  Copy-Item -Path (Join-Path $OutputRoot "osx-x64\MYLan.app") -Destination (Join-Path $releaseDir "MYLan-x64.app") -Recurse -Force

  @'
#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
ARCH="$(uname -m)"
if [[ "$ARCH" == "arm64" ]]; then
  APP_SOURCE="$DIR/MYLan-arm64.app"
else
  APP_SOURCE="$DIR/MYLan-x64.app"
fi

APP_DEST="/Applications/MYLan.app"
CLI_DEST="/usr/local/bin/mylan"
LAUNCHER_DEST="/Applications/Run MYLan.command"

if [[ ! -d "$APP_SOURCE" ]]; then
  echo "Could not find $APP_SOURCE"
  read -r -p "Press Return to close."
  exit 1
fi

echo "Installing MYLan for $ARCH..."
echo "macOS will ask for your administrator password."

sudo rm -rf "$APP_DEST"
sudo cp -R "$APP_SOURCE" "$APP_DEST"
sudo chown -R root:wheel "$APP_DEST"
sudo chmod -R go+rX "$APP_DEST"
sudo chmod +x "$APP_DEST/Contents/MacOS/MYLan"

sudo mkdir -p /usr/local/bin
TMP_RUNNER="$(mktemp)"
cat > "$TMP_RUNNER" <<'RUNNER'
#!/usr/bin/env bash
sudo /Applications/MYLan.app/Contents/MacOS/MYLan "$@"
RUNNER
sudo cp "$TMP_RUNNER" "$CLI_DEST"
sudo cp "$TMP_RUNNER" "$LAUNCHER_DEST"
rm -f "$TMP_RUNNER"
sudo chmod +x "$CLI_DEST" "$LAUNCHER_DEST"
sudo chown root:wheel "$CLI_DEST" "$LAUNCHER_DEST"

echo
echo "MYLan installed."
echo "Run it by double-clicking /Applications/Run MYLan.command"
echo "or from Terminal with: mylan"
echo
read -r -p "Press Return to close."
'@ | Write-UnixText -Path (Join-Path $releaseDir "Install MYLan.command")

  @'
#!/usr/bin/env bash
set -euo pipefail

APP="/Applications/MYLan.app"
if [[ ! -x "$APP/Contents/MacOS/MYLan" ]]; then
  echo "MYLan is not installed. Run Install MYLan.command first."
  read -r -p "Press Return to close."
  exit 1
fi

sudo "$APP/Contents/MacOS/MYLan" "$@"
'@ | Write-UnixText -Path (Join-Path $releaseDir "Run MYLan.command")

  @'
#!/usr/bin/env bash
set -euo pipefail

echo "Removing MYLan..."
echo "macOS will ask for your administrator password."

sudo rm -rf /Applications/MYLan.app
sudo rm -f "/Applications/Run MYLan.command"
sudo rm -f /usr/local/bin/mylan
sudo rm -rf "/Library/Application Support/MYLan" 2>/dev/null || true
sudo rm -rf /var/root/.local/share/MYLan 2>/dev/null || true

echo
echo "MYLan removed."
echo
read -r -p "Press Return to close."
'@ | Write-UnixText -Path (Join-Path $releaseDir "Uninstall MYLan.command")

  @"
MYLan DHCP Field Server - macOS

Install:
1. Unzip this package on your Mac.
2. Double-click "Install MYLan.command".
3. Enter your Mac administrator password.

Run:
Double-click "/Applications/Run MYLan.command".

This package includes:
- MYLan-arm64.app for Apple Silicon Macs.
- MYLan-x64.app for Intel Macs.

The installer automatically chooses the right app for your Mac.

Why a command launcher?
MYLan is a DHCP server. macOS requires administrator privileges for UDP port 67 and adapter changes.

Public release note:
For public distribution, sign and notarise the app/DMG with an Apple Developer ID.
"@ | Write-UnixText -Path (Join-Path $releaseDir "README-FIRST.txt")

  return $releaseDir
}

Write-Host "Publishing macOS Intel..."
dotnet publish (Join-Path $Root "MYLan\MYLan.csproj") -c Release -r osx-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $Root "release\osx-x64")

Write-Host "Publishing macOS Apple Silicon..."
dotnet publish (Join-Path $Root "MYLan\MYLan.csproj") -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $Root "release\osx-arm64")

New-MacAppBundle -Rid "osx-x64"
New-MacAppBundle -Rid "osx-arm64"
$releaseDir = Write-MacReleaseScripts

if (Test-Path $ZipPath) {
  Remove-Item -LiteralPath $ZipPath -Force
}

function ConvertTo-ZipExternalAttributes {
  param([Parameter(Mandatory=$true)][int]$UnixMode)
  $value = [uint32]($UnixMode * 65536)
  return [BitConverter]::ToInt32([BitConverter]::GetBytes($value), 0)
}

function Get-ZipUnixMode {
  param([Parameter(Mandatory=$true)][string]$Path)

  if (Test-Path -LiteralPath $Path -PathType Container) {
    return 16877 # 040755
  }

  $name = Split-Path -Leaf $Path
  if ($name.EndsWith(".command", [StringComparison]::OrdinalIgnoreCase) -or $name -eq "MYLan") {
    return 33261 # 0100755
  }

  return 33188 # 0100644
}

function New-ZipWithMacModes {
  param(
    [Parameter(Mandatory=$true)][string]$SourceDirectory,
    [Parameter(Mandatory=$true)][string]$DestinationZip
  )

  Add-Type -AssemblyName System.IO.Compression
  Add-Type -AssemblyName System.IO.Compression.FileSystem

  if (Test-Path -LiteralPath $DestinationZip) {
    Remove-Item -LiteralPath $DestinationZip -Force
  }

  $sourceParent = Split-Path -Parent $SourceDirectory
  $fs = [System.IO.File]::Open($DestinationZip, [System.IO.FileMode]::CreateNew)
  try {
    $archive = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
      $items = Get-ChildItem -LiteralPath $SourceDirectory -Recurse -Force | Sort-Object FullName
      foreach ($item in $items) {
        $relative = $item.FullName.Substring($sourceParent.Length + 1).Replace('\', '/')
        if ($item.PSIsContainer) {
          $entry = $archive.CreateEntry($relative.TrimEnd('/') + '/')
        }
        else {
          $entry = $archive.CreateEntry($relative, [System.IO.Compression.CompressionLevel]::Optimal)
          $entryStream = $entry.Open()
          try {
            $fileStream = [System.IO.File]::OpenRead($item.FullName)
            try { $fileStream.CopyTo($entryStream) }
            finally { $fileStream.Dispose() }
          }
          finally { $entryStream.Dispose() }
        }

        $entry.ExternalAttributes = ConvertTo-ZipExternalAttributes -UnixMode (Get-ZipUnixMode -Path $item.FullName)
      }
    }
    finally {
      $archive.Dispose()
    }
  }
  finally {
    $fs.Dispose()
  }
}

New-ZipWithMacModes -SourceDirectory $releaseDir -DestinationZip $ZipPath

Write-Host "Done. Mac-ready zip:"
Write-Host $ZipPath
