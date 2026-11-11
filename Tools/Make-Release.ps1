# Makes the files of a release into Builds/Release:
#   assets/manifest.json            the version; each part's file, size and SHA-256; where the game data and the HD art
#                                   come from; the server's map checksums (what the launcher compares and checks)
#   assets/UnityUO-<part>.zip       the parts, so an update downloads only what changed:
#                                     game, art, art-gumps   from the package (UOClient > Package Windows Player)
#                                     maps                   the shard's six map files, into UOData\
#                                     shard                  the shard's custom art and item tables, into Shard\Client\
#   assets/unityuo-launcher.ps1     installer/unityuo-launcher.ps1
#   assets/UnityUO-Setup.cmd        installer/UnityUO-Setup.cmd: the one file a player downloads
#   VERSION, NOTES.md               for the release's tag and text
# The maps and shard parts are taken from this PC's UO Smallz client (-ShardFrom), which is only read; the map files
# must match the server's checksums in installer/data-sources.json or nothing is made.
# The version is the number in the repo's VERSION file. Publishing is Tools/Publish-Release.ps1.
param(
    [string]$Package = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Package\Unity UO'),
    [string]$Out = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Release'),
    [string]$ShardFrom = (Join-Path $env:LOCALAPPDATA 'UOSmallz'),
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
$sources = Get-Content (Join-Path $root 'installer\data-sources.json') -Raw | ConvertFrom-Json

# ---- what goes in each part: (file on this PC, path in the install) ----
function Under([string]$dir, [string]$prefix) {
    if (-not (Test-Path -LiteralPath $dir)) { throw "$dir is missing." }
    Get-ChildItem -LiteralPath $dir -Recurse -File | ForEach-Object { ,@($_.FullName, ($prefix + $_.FullName.Substring($dir.Length + 1))) }
}
$packaged = @(Under $Package '')
$gumps = @($packaged | Where-Object { $_[1].StartsWith('Theme\gumps\') })
$art = @($packaged | Where-Object { ($_[1].StartsWith('Theme\') -or $_[1].StartsWith('Tools\')) -and -not $_[1].StartsWith('Theme\gumps\') })
$game = @($packaged | Where-Object { -not $_[1].StartsWith('Theme\') -and -not $_[1].StartsWith('Tools\') })

# The shard's map files: exactly the server's, or no release.
$dataDir = Get-ChildItem -LiteralPath (Join-Path $ShardFrom 'UOData') -Recurse -Filter 'tiledata.mul' |
    Where-Object { $_.DirectoryName -notmatch '\\_' } | Select-Object -First 1 | ForEach-Object { $_.DirectoryName }
if (-not $dataDir) { throw "No game data under $ShardFrom\UOData." }
$maps = @()
foreach ($m in $sources.maps.PSObject.Properties) {
    $file = Join-Path $dataDir $m.Name
    if (-not (Test-Path -LiteralPath $file)) { throw "$file is missing." }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLower() -ne $m.Value) { throw "$file is not the server's (its SHA-256 differs from installer\data-sources.json)." }
    $maps += ,@($file, "UOData\$($m.Name)")
}
$client = Join-Path $ShardFrom 'Client'
$shard = @(Under (Join-Path $client 'ExternalImages') 'Shard\Client\ExternalImages\') +
         @(Under (Join-Path $client 'TileData') 'Shard\Client\TileData\') +
         @(Under (Join-Path $client 'EquipConv') 'Shard\Client\EquipConv\')

$parts = @(
    @{ name = 'game';      title = 'the game';                         files = $game },
    @{ name = 'art-gumps'; title = 'the HD windows and paperdoll art'; files = $gumps },
    @{ name = 'art';       title = 'the HD effects art';               files = $art },
    @{ name = 'maps';      title = 'the shard''s map';                 files = $maps },
    @{ name = 'shard';     title = 'the shard''s custom items';        files = $shard }
)

if (Test-Path $Out) { Remove-Item -LiteralPath $Out -Recurse -Force }
$assets = Join-Path $Out 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$manifestParts = @()
foreach ($part in $parts) {
    $zipPath = Join-Path $assets "UnityUO-$($part.name).zip"
    $stream = [IO.File]::Create($zipPath)
    $zip = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in ($part.files | Sort-Object { $_[1] })) {
            # PNG and video are compressed already: stored as they are, which keeps the zip quick to make and open.
            $level = if ($f[1] -match '\.(png|mp4|webp|jpg)$') { [IO.Compression.CompressionLevel]::NoCompression } else { [IO.Compression.CompressionLevel]::Optimal }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f[0], $f[1].Replace('\', '/'), $level) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    $size = (Get-Item $zipPath).Length
    if ($size -ge 100MB) { throw "$zipPath is $([Math]::Round($size / 1MB)) MB: over GitHub's 100 MB limit for a pushed file. Split the part." }
    $manifestParts += [ordered]@{ name = $part.name; title = $part.title; file = "UnityUO-$($part.name).zip"; size = $size
                                  sha256 = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLower(); files = @($part.files).Count }
    "{0,-10} {1,5} files  {2,4} MB" -f $part.name, @($part.files).Count, [Math]::Round($size / 1MB)
}

Copy-Item (Join-Path $root 'installer\unityuo-launcher.ps1') $assets
Copy-Item (Join-Path $root 'installer\UnityUO-Setup.cmd') $assets
$launcher = Join-Path $assets 'unityuo-launcher.ps1'
$manifest = [ordered]@{
    version  = $version
    launcher = [ordered]@{ size = (Get-Item $launcher).Length; sha256 = (Get-FileHash $launcher -Algorithm SHA256).Hash.ToLower() }
    parts    = $manifestParts
    data     = [ordered]@{ uodata = $sources.uodata; hdart = $sources.hdart }
    maps     = $sources.maps
}
# No BOM: the launcher reads it as JSON text from the web.
[IO.File]::WriteAllText((Join-Path $assets 'manifest.json'), ($manifest | ConvertTo-Json -Depth 6))
[IO.File]::WriteAllText((Join-Path $Out 'VERSION'), "$version")
if (-not $Notes) { $Notes = "Unity UO v$version for the UO Smallz shard.`n`nTo install: download **UnityUO-Setup.cmd** below and double-click it. To update: just start the game; the shortcut checks for a new version first." }
[IO.File]::WriteAllText((Join-Path $Out 'NOTES.md'), $Notes)
"Release v$version in $Out"
