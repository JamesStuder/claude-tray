#!/usr/bin/env bash
# Remove Claude Tray (keeps ~/.config/claude-tray and the cache).
set -euo pipefail
pkill -x claude-tray 2>/dev/null || true
rm -rf "$HOME/.local/lib/claude-tray"
rm -f "$HOME/.local/bin/claude-tray" "$HOME/.local/share/applications/claude-tray.desktop" \
      "$HOME/.config/autostart/claude-tray.desktop" "$HOME/.local/share/icons/hicolor/256x256/apps/claude-tray.png"
echo "Removed. Settings are still in ~/.config/claude-tray."
