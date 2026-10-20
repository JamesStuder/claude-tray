# Install Claude Tray for the current user (no admin needed).
#   .\install.ps1                 from an unpacked dist\win-x64 folder, or from a checkout (builds first)
#   .\install.ps1 -NoAutostart    don't start it at sign-in
param([switch]$NoAutostart)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path (Join-Path $here 'claude-tray.exe')) {
    $src = $here
} else {
    $root = Resolve-Path (Join-Path $here '..\..')
    $rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
    dotnet publish "$root\src\ClaudeTray" -c Release -r $rid --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$root\dist\$rid" --nologo -v quiet
    if ($LASTEXITCODE) { throw 'build failed' }
    $src = "$root\dist\$rid"
}

$dest = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeTray'
Get-Process claude-tray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $src 'claude-tray.exe') $dest -Force
$exe = Join-Path $dest 'claude-tray.exe'

$shell = New-Object -ComObject WScript.Shell
$lnk = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'Claude Tray.lnk'))
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Description = 'Claude Code sessions and usage in the system tray'
$lnk.Save()

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if ($NoAutostart) { Remove-ItemProperty $run -Name 'ClaudeTray' -ErrorAction SilentlyContinue }
else { Set-ItemProperty $run -Name 'ClaudeTray' -Value "`"$exe`"" }

Start-Process $exe
Write-Host "Installed to $dest and started. Settings: $env:APPDATA\ClaudeTray\config.json"
