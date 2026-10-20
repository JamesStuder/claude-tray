#!/usr/bin/env bash
# Install Claude Tray for the current user (no root needed).
#   ./install.sh                 from a checkout (builds first) or from an unpacked dist/linux-* folder
#   ./install.sh --no-autostart  don't start it at login
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
autostart=1
for a in "$@"; do [ "$a" = --no-autostart ] && autostart=0; done

if [ -x "$here/claude-tray" ]; then
    src="$here"                                   # unpacked release folder
else
    root="$(cd "$here/../.." && pwd)"             # packaging/linux in a checkout
    arch=$(uname -m); rid=linux-x64; [ "$arch" = aarch64 ] && rid=linux-arm64
    "$root/build.sh" "$rid"
    src="$root/dist/$rid"
fi

lib="$HOME/.local/lib/claude-tray"
pkill -x claude-tray 2>/dev/null && sleep 1 || true
mkdir -p "$lib" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
install -m755 "$src/claude-tray" "$lib/claude-tray"
ln -sf "$lib/claude-tray" "$HOME/.local/bin/claude-tray"
install -m644 "$src/claude-tray.png" "$HOME/.local/share/icons/hicolor/256x256/apps/claude-tray.png"

desktop="[Desktop Entry]
Type=Application
Name=Claude Tray
Comment=Claude Code sessions and usage in the system tray
Exec=$lib/claude-tray
Icon=claude-tray
Terminal=false
Categories=Development;Utility;
StartupNotify=false"
printf '%s\n' "$desktop" > "$HOME/.local/share/applications/claude-tray.desktop"
if [ "$autostart" = 1 ]; then
    mkdir -p "$HOME/.config/autostart"
    printf '%s\nX-GNOME-Autostart-enabled=true\n' "$desktop" > "$HOME/.config/autostart/claude-tray.desktop"
fi
command -v update-desktop-database >/dev/null && update-desktop-database -q "$HOME/.local/share/applications" || true

setsid "$lib/claude-tray" >/dev/null 2>&1 < /dev/null &
echo "Installed to $lib and started. Settings: ~/.config/claude-tray/config.json"
