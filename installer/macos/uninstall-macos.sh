#!/usr/bin/env bash
set -euo pipefail

# Simple MYLan macOS uninstaller.
# Usage:
#   sudo ./uninstall-macos.sh

if [[ $EUID -ne 0 ]]; then
  echo "Please run with sudo."
  exit 1
fi

rm -rf /Applications/MYLan.app
rm -f /Applications/Run\ MYLan.command
rm -f /usr/local/bin/mylan
rm -rf /Library/Application\ Support/MYLan
rm -rf /var/root/.local/share/MYLan 2>/dev/null || true
rm -rf "$HOME/Library/Application Support/MYLan" 2>/dev/null || true

if [[ -n "${SUDO_USER:-}" && "$SUDO_USER" != "root" ]]; then
  USER_HOME="$(dscl . -read "/Users/$SUDO_USER" NFSHomeDirectory 2>/dev/null | awk '{print $2}')"
  if [[ -n "$USER_HOME" ]]; then
    rm -rf "$USER_HOME/.local/share/MYLan" 2>/dev/null || true
    rm -rf "$USER_HOME/Library/Application Support/MYLan" 2>/dev/null || true
  fi
fi

echo "MYLan has been removed."
