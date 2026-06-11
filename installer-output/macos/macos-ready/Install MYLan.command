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