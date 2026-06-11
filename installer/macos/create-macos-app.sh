#!/usr/bin/env bash
set -euo pipefail

# Create .app bundles for MYLan macOS builds.
# Expected publish folders:
#   release/osx-x64/MYLan
#   release/osx-arm64/MYLan

APP_NAME="MYLan"
APP_DISPLAY_NAME="MYLan DHCP Field Server"
VERSION="2.1.0"
BUNDLE_ID="io.eventresearch.mylan"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

create_app() {
  local rid="$1"
  local src="$ROOT_DIR/release/$rid"
  local out="$ROOT_DIR/installer-output/macos/$rid/${APP_NAME}.app"

  if [[ ! -x "$src/MYLan" ]]; then
    echo "Missing executable: $src/MYLan"
    echo "Run ./publish-macos.sh first."
    exit 1
  fi

  rm -rf "$out"
  mkdir -p "$out/Contents/MacOS" "$out/Contents/Resources"
  cp -R "$src/"* "$out/Contents/MacOS/"
  chmod +x "$out/Contents/MacOS/MYLan"

  cat > "$out/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>
  <string>${APP_NAME}</string>
  <key>CFBundleDisplayName</key>
  <string>${APP_DISPLAY_NAME}</string>
  <key>CFBundleIdentifier</key>
  <string>${BUNDLE_ID}</string>
  <key>CFBundleVersion</key>
  <string>${VERSION}</string>
  <key>CFBundleShortVersionString</key>
  <string>${VERSION}</string>
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
PLIST

  echo "Created: $out"
}

create_app "osx-x64"
create_app "osx-arm64"
