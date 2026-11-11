# Installs the packaged Unity UO client (Builds/Package/Unity UO, made by UOClient > Package Windows Player) on this PC:
# copies it to %LOCALAPPDATA%\UnityUO and puts a shortcut on the desktop. The exe carries the UO emblem, so the
# shortcut shows it. Settings, the saved login, hotkeys and logs live in the install folder's UserSettings and Logs and
# are kept across re-installs. The UO Smallz client's own folder (%LOCALAPPDATA%\UOSmallz) is only read by the game,
# never written here.
param(
    [string]$Package = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Package\Unity UO'),
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'UnityUO'),
    [string]$ShortcutName = 'Unity UO (installed)',
    # The shard's look: the natural ground and the grass on it. They are launch switches, not options, and the
    # ground renderer only runs against a server that is not this PC with -liveground-anyhost. '' for the classic land.
    [string]$Arguments = '-liveground -liveground-anyhost -groundbackend terrain -terrainstyle natural -grass',
    # Only remake the shortcut (the game may be running from the install).
    [switch]$ShortcutOnly
)
$ErrorActionPreference = 'Stop'

$exe = Join-Path $Package 'Unity UO.exe'
if (-not (Test-Path $exe)) { throw "No package at $Package (build it first: UOClient > Package Windows Player)." }
if (Test-Path (Join-Path $Package 'unity-uo-paths.json')) { throw "$Package still has unity-uo-paths.json: that is a developer build, not a package." }
if (Test-Path (Join-Path $Package 'UserSettings')) { throw "$Package has a UserSettings folder (a saved login may be in it): remove it before installing." }

if (-not $ShortcutOnly) {
# The game must not be running from the install folder while its files are replaced.
$running = Get-Process 'Unity UO' -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($InstallDir, [StringComparison]::OrdinalIgnoreCase) }
if ($running) { throw "Unity UO is running from $InstallDir. Close it and run this again." }

New-Item -ItemType Directory -Force $InstallDir | Out-Null
# /MIR makes the install match the package; the player's own folders are left alone.
robocopy $Package $InstallDir /MIR /XD UserSettings Logs Library /NFL /NDL /NJH /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copy failed (robocopy exit $LASTEXITCODE)." }
}

$installed = Join-Path $InstallDir 'Unity UO.exe'
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) "$ShortcutName.lnk"))
$link.TargetPath = $installed
$link.Arguments = $Arguments
$link.WorkingDirectory = $InstallDir
$link.IconLocation = "$installed,0"
$link.Description = 'UO Smallz (Unity client)'
$link.Save()

$files = (Get-ChildItem $InstallDir -Recurse -File | Measure-Object -Property Length -Sum)
"Installed to $InstallDir ($($files.Count) files, $([math]::Round($files.Sum / 1MB)) MB); shortcut: $ShortcutName on the desktop."
