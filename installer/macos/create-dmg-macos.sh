#!/usr/bin/env bash
set -euo pipefail

# Create user-friendly DMG files from already-created MYLan.app bundles.
# Run after:
#   ./publish-macos.sh
#   ./installer/macos/create-macos-app.sh

VERSION="2.1.0"
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

write_install_script() {
  local staging="$1"
  cat > "$staging/Install MYLan.command" <<'SCRIPT'
#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
APP_SOURCE="$DIR/MYLan.app"
APP_DEST="/Applications/MYLan.app"
CLI_DEST="/usr/local/bin/mylan"
LAUNCHER_DEST="/Applications/Run MYLan.command"

if [[ ! -d "$APP_SOURCE" ]]; then
  echo "MYLan.app was not found next to this installer."
  read -r -p "Press Return to close."
  exit 1
fi

echo "Installing MYLan..."
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
SCRIPT
  chmod +x "$staging/Install MYLan.command"
}

write_run_script() {
  local staging="$1"
  cat > "$staging/Run MYLan.command" <<'SCRIPT'
#!/usr/bin/env bash
set -euo pipefail

APP="/Applications/MYLan.app"
if [[ ! -x "$APP/Contents/MacOS/MYLan" ]]; then
  DIR="$(cd "$(dirname "$0")" && pwd)"
  APP="$DIR/MYLan.app"
fi

if [[ ! -x "$APP/Contents/MacOS/MYLan" ]]; then
  echo "MYLan is not installed. Run Install MYLan.command first."
  read -r -p "Press Return to close."
  exit 1
fi

sudo "$APP/Contents/MacOS/MYLan" "$@"
SCRIPT
  chmod +x "$staging/Run MYLan.command"
}

write_uninstall_script() {
  local staging="$1"
  cat > "$staging/Uninstall MYLan.command" <<'SCRIPT'
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
SCRIPT
  chmod +x "$staging/Uninstall MYLan.command"
}

write_readme() {
  local staging="$1"
  cat > "$staging/README-FIRST.txt" <<'README'
MYLan DHCP Field Server

Install:
1. Double-click "Install MYLan.command".
2. Enter your Mac administrator password when prompted.

Run:
Double-click "/Applications/Run MYLan.command".

Why a command launcher?
MYLan is a DHCP server. macOS requires administrator privileges for DHCP port 67 and adapter changes.

Uninstall:
Double-click "Uninstall MYLan.command" from this DMG.

For public distribution, the app/DMG should be Developer ID signed and notarised.
README
}

create_dmg() {
  local rid="$1"
  local app="$ROOT_DIR/installer-output/macos/$rid/MYLan.app"
  local staging="$ROOT_DIR/installer-output/macos/dmg-$rid"
  local dmg="$ROOT_DIR/installer-output/macos/MYLan-${VERSION}-${rid}.dmg"

  if [[ ! -d "$app" ]]; then
    echo "Missing app bundle: $app"
    exit 1
  fi

  rm -rf "$staging"
  mkdir -p "$staging"
  cp -R "$app" "$staging/MYLan.app"
  ln -s /Applications "$staging/Applications"
  write_install_script "$staging"
  write_run_script "$staging"
  write_uninstall_script "$staging"
  write_readme "$staging"

  rm -f "$dmg"
  hdiutil create -volname "MYLan ${VERSION}" -srcfolder "$staging" -ov -format UDZO "$dmg"
  echo "Created: $dmg"
}

create_dmg "osx-x64"
create_dmg "osx-arm64"
