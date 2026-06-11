#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

echo "Building MYLan macOS DMGs..."
echo

chmod +x publish-macos.sh installer/macos/*.sh

./publish-macos.sh
./installer/macos/create-macos-app.sh
./installer/macos/create-dmg-macos.sh

echo
echo "Done."
echo "DMGs:"
echo "installer-output/macos/MYLan-2.1.0-osx-x64.dmg"
echo "installer-output/macos/MYLan-2.1.0-osx-arm64.dmg"
echo
read -r -p "Press Return to close."
