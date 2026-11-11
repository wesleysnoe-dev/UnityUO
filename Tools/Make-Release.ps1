# Makes the files of a release from the package (Builds/Package/Unity UO, made by UOClient > Package Windows Player)
# into Builds/Release:
#   assets/manifest.json            the version and each part's file, size and SHA-256 (what the launcher compares)
#   assets/UnityUO-<part>.zip       the game in parts, so an update downloads only what changed
#   assets/unityuo-launcher.ps1     installer/unityuo-launcher.ps1
#   assets/UnityUO-Setup.cmd        installer/UnityUO-Setup.cmd: the one file a player downloads
#   VERSION, NOTES.md               for the release's tag and text
# The version is the number in the repo's VERSION file. Publishing is Tools/Publish-Release.ps1.
param(
    [string]$Package = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Package\Unity UO'),
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Release'),
    [string]$Notes = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path (Join-Path $Package 'Unity UO.exe'))) { throw "No package at $Package." }
if (Test-Path (Join-Path $Package 'unity-uo-paths.json')) { throw "$Package is a developer build (it has unity-uo-paths.json)." }
if (Test-Path (Join-Path $Package 'UserSettings')) { throw "$Package has a UserSettings folder (a saved login may be in it): remove it first." }
$version = [int]((Get-Content (Join-Path $root 'VERSION') -Raw).Trim())

# The parts, by the top of each file's path. Each zip stays under GitHub's 100 MB limit for a pushed file.
$parts = @(
    @{ name = 'art-gumps'; title = 'the HD windows and paperdoll art'; match = { param($p) $p.StartsWith('Theme\gumps\') } },
    @{ name = 'art';       title = 'the HD effects art';               match = { param($p) $p.StartsWith('Theme\') -or $p.StartsWith('Tools\') } },
    @{ name = 'game';      title = 'the game';                         match = { param($p) $true } }
)

if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
$assets = Join-Path $Out 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$all = Get-ChildItem $Package -Recurse -File | ForEach-Object { $_.FullName.Substring($Package.Length + 1) } | Sort-Object
$taken = @{}
$manifestParts = @()
foreach ($part in $parts) {
    $files = @($all | Where-Object { -not $taken.ContainsKey($_) -and (& $part.match $_) })
    foreach ($f in $files) { $taken[$f] = $true }
    $zipPath = Join-Path $assets "UnityUO-$($part.name).zip"
    $stream = [IO.File]::Create($zipPath)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in $files) {
            # PNG and video are compressed already: stored as they are, which keeps the zip quick to make and open.
            $level = if ($f -match '\.(png|mp4|webp|jpg)$') { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $Package $f), $f.Replace('\', '/'), $level) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    $size = (Get-Item $zipPath).Length
    if ($size -ge 100MB) { throw "$zipPath is $([Math]::Round($size / 1MB)) MB: over GitHub's 100 MB limit. Split the part." }
    $manifestParts += [ordered]@{ name = $part.name; title = $part.title; file = "UnityUO-$($part.name).zip"; size = $size
                                  sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower(); files = $files.Count }
    "{0,-10} {1,5} files  {2,4} MB" -f $part.name, $files.Count, [Math]::Round($size / 1MB)
}

Copy-Item (Join-Path $root 'installer\unityuo-launcher.ps1') $assets
Copy-Item (Join-Path $root 'installer\UnityUO-Setup.cmd') $assets
$launcher = Join-Path $assets 'unityuo-launcher.ps1'
$manifest = [ordered]@{
    version  = $version
    launcher = [ordered]@{ size = (Get-Item $launcher).Length; sha256 = (Get-FileHash $launcher -Algorithm SHA256).Hash.ToLower() }
    parts    = $manifestParts
}
# No BOM: the launcher reads it as JSON text from the web.
[IO.File]::WriteAllText((Join-Path $assets 'manifest.json'), ($manifest | ConvertTo-Json -Depth 5))
[IO.File]::WriteAllText((Join-Path $Out 'VERSION'), "$version")
if (-not $Notes) { $Notes = "Unity UO v$version for the UO Smallz shard.`n`nTo install: download **UnityUO-Setup.cmd** below and double-click it. To update: just start the game; the shortcut checks for a new version first." }
[IO.File]::WriteAllText((Join-Path $Out 'NOTES.md'), $Notes)
"Release v$version in $Out"
