# Remove Claude Tray (keeps %APPDATA%\ClaudeTray settings).
Get-Process claude-tray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-Item (Join-Path $env:LOCALAPPDATA 'Programs\ClaudeTray') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Tray.lnk') -Force -ErrorAction SilentlyContinue
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeTray' -ErrorAction SilentlyContinue
Write-Host "Removed. Settings are still in $env:APPDATA\ClaudeTray."
