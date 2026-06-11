#!/usr/bin/env bash
set -euo pipefail

APP="/Applications/MYLan.app"
if [[ ! -x "$APP/Contents/MacOS/MYLan" ]]; then
  echo "MYLan is not installed. Run Install MYLan.command first."
  read -r -p "Press Return to close."
  exit 1
fi

sudo "$APP/Contents/MacOS/MYLan" "$@"