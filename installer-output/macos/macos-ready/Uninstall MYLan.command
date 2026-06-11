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