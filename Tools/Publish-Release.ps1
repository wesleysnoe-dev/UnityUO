# Publishes Builds/Release (Tools/Make-Release.ps1) as a GitHub release: pushes its files and the release workflow as
# one commit on the release-staging branch, which .github/workflows/release.yml turns into release v<VERSION> and then
# deletes. The push uses this PC's git sign-in for GitHub, as any push does. Nothing in the project's own history changes.
param(
    [string]$Release = (Join-Path (Split-Path -Parent $PSScriptRoot) 'Builds\Release'),
    [string]$Remote = 'https://github.com/wesleysnoe-dev/UnityUO.git'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($f in 'VERSION', 'NOTES.md', 'assets\manifest.json', 'assets\unityuo-launcher.ps1', 'assets\UnityUO-Setup.cmd') {
    if (-not (Test-Path (Join-Path $Release $f))) { throw "$Release has no $f. Run Tools\Make-Release.ps1 first." }
}
$stage = Join-Path (Split-Path -Parent $Release) 'ReleaseStaging'
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage '.github\workflows') | Out-Null
Copy-Item (Join-Path $Release '*') $stage -Recurse
Copy-Item (Join-Path $root '.github\workflows\release.yml') (Join-Path $stage '.github\workflows')

$version = (Get-Content (Join-Path $Release 'VERSION') -Raw).Trim()
Push-Location $stage
try {
    git init -q -b release-staging
    git config core.autocrlf false      # the files go up byte for byte: their checksums are in the manifest
    git add -A
    git -c user.name="$(git -C $root config user.name)" -c user.email="$(git -C $root config user.email)" commit -q -m "release v$version"
    git push --force $Remote release-staging
    if ($LASTEXITCODE -ne 0) { throw "The push failed (git exit $LASTEXITCODE)." }
} finally { Pop-Location }
"Pushed release v$version to release-staging. The workflow publishes it in a minute or two: https://github.com/wesleysnoe-dev/UnityUO/actions"
