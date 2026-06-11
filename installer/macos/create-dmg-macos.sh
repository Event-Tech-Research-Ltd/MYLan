#!/usr/bin/env bash
set -euo pipefail

# Create simple DMG files from already-created MYLan.app bundles.
# Run after:
#   ./publish-macos.sh
#   ./installer/macos/create-macos-app.sh

VERSION="2.1.0"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

create_dmg() {
  local rid="$1"
  local app="$ROOT_DIR/installer-output/macos/$rid/MYLan.app"
  local dmg="$ROOT_DIR/installer-output/macos/MYLan-${VERSION}-${rid}.dmg"

  if [[ ! -d "$app" ]]; then
    echo "Missing app bundle: $app"
    exit 1
  fi

  rm -f "$dmg"
  hdiutil create -volname "MYLan" -srcfolder "$app" -ov -format UDZO "$dmg"
  echo "Created: $dmg"
}

create_dmg "osx-x64"
create_dmg "osx-arm64"
