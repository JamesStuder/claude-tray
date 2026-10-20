# Changelog

## 2.0.0 — 2026-10-20

Rewrite as a standalone cross-platform app (C#/.NET 10 + Avalonia) for Linux and Windows; replaces the
KDE Plasma widget (1.x).

- Settings window (tray menu → Settings…, or ⚙ in the panel): Claude Code login status, log in again / log out; theme and custom panel colours (background, text, buttons, button text); support (optional, pay what you want); about with the settings file and log.

- Tray icon with session-usage and needs-input badges; panel with background sessions (open,
  raise, remove) and usage (limits, tokens by day and model, pace).
- Model buttons: live switch through tmux (Linux), else the default model in Claude Code's
  settings.json (all platforms).
- Restart in tmux, resuming the same conversation.
- Plasma: panel placed next to the pointer by KWin; session windows raised by KWin.
- Windows: sessions in Windows Terminal, raised by window title.
- `claude-tray --text` terminal summary.
- Self-contained single-file builds and per-user installers for Linux and Windows.
