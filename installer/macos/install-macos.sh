#!/usr/bin/env bash
set -euo pipefail

# Simple MYLan macOS installer.
# Usage:
#   sudo ./install-macos.sh /path/to/MYLan.app

APP_SOURCE="${1:-}"
APP_DEST="/Applications/MYLan.app"
CLI_DEST="/usr/local/bin/mylan"

if [[ -z "$APP_SOURCE" || ! -d "$APP_SOURCE" ]]; then
  echo "Usage: sudo ./install-macos.sh /path/to/MYLan.app"
  exit 1
fi

if [[ $EUID -ne 0 ]]; then
  echo "Please run with sudo because MYLan needs installation into /Applications and DHCP requires elevated privileges."
  exit 1
fi

rm -rf "$APP_DEST"
cp -R "$APP_SOURCE" "$APP_DEST"
chown -R root:wheel "$APP_DEST"
chmod -R go+rX "$APP_DEST"
chmod +x "$APP_DEST/Contents/MacOS/MYLan"

mkdir -p /usr/local/bin
cat > "$CLI_DEST" <<'RUNNER'
#!/usr/bin/env bash
sudo /Applications/MYLan.app/Contents/MacOS/MYLan "$@"
RUNNER
chmod +x "$CLI_DEST"

echo "MYLan installed to $APP_DEST"
echo "Run from Terminal with: mylan"
echo "Or run directly with: sudo /Applications/MYLan.app/Contents/MacOS/MYLan"
