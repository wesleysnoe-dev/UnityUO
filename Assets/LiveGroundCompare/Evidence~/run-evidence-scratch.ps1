# LiveGroundCompare (TEST only) Phase C evidence, for when the main project is open in the Editor (project lock).
# Copies ONLY the LiveGroundCompare shaders + Editor capture and UOClient/WorldSprite into a throwaway scratch project,
# runs LiveGroundEvidenceCapture.CaptureBatch there in -batchmode (GPU, no -nographics), and writes the stills back to
# Assets/LiveGroundCompare/Evidence~/stills. Never touches the main project's Library, scenes, settings or Builds.
#
#   powershell -ExecutionPolicy Bypass -File Assets\LiveGroundCompare\Evidence~\run-evidence-scratch.ps1
#
# Regenerate the fixture first if the builders changed:
#   $env:UOCLIENT_WRITE_LIVEGROUND_EVIDENCE='1'; dotnet test DotNet/UOClient.Tests --filter "FullyQualifiedName~LiveGroundEvidenceFixtureTests"
#
# -ShadowRecv runs the sprite shadow receive capture instead (CaptureShadowRecvBatch on shadowRecvChunk.json, from
# LiveGroundSpriteShadowFixtureTests; it reads the PNGs and WorldSprite.4aa51a6.shader.txt next to the JSON).
# -DynCast: mobile/item dynamic cast + cross-chunk margin stills (CaptureDynCastBatch on dynCastChunk.json from
# LiveGroundDynCastFixtureTests, over shadowRecvChunk.json). -WaterRealtime: the shipped water shader on separate Editor
# frames with a _Time probe (CaptureWaterRealtimeBatch; the Editor stays up for those frames, then exits itself).
#
# -WaterRecv: water sun-ceiling receive stills (CaptureWaterRecvBatch on waterRecvChunk.json from
# LiveGroundWaterRecvFixtureTests, over realWaterChunk.json; equivalence against LiveGroundCompareWater.83c0a85.shader.txt).
# -WaterRealtime -WaterRecv: the multi-frame _Time run with that cast ceiling live (waterrecv_realtime_*).
param(
    [string]$Scratch = (Join-Path $env:TEMP 'UOClient_LGEvidence'),
    [string]$Unity = 'C:\Program Files\Unity\Hub\Editor\6000.6.8f1\Editor\Unity.exe',
    [switch]$ShadowRecv,
    [switch]$DynCast,
    [switch]$WaterRealtime,
    [switch]$WaterRecv
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$lg = Join-Path $repo 'Assets\LiveGroundCompare'

foreach ($d in 'Assets\LiveGroundCompare', 'Assets\WorldSprite\Resources', 'Packages') {
    New-Item -ItemType Directory -Force (Join-Path $Scratch $d) | Out-Null
}
Remove-Item -Recurse -Force (Join-Path $Scratch 'Assets\LiveGroundCompare\*') -ErrorAction SilentlyContinue
Copy-Item -Recurse (Join-Path $lg 'Shaders') (Join-Path $Scratch 'Assets\LiveGroundCompare\Shaders')
Copy-Item -Recurse (Join-Path $lg 'Editor') (Join-Path $Scratch 'Assets\LiveGroundCompare\Editor')
Copy-Item (Join-Path $repo 'Assets\UOClient.Unity\Runtime\Resources\WorldSprite.shader*') (Join-Path $Scratch 'Assets\WorldSprite\Resources')
Set-Content -Encoding ascii (Join-Path $Scratch 'Packages\manifest.json') '{ "dependencies": { "com.unity.modules.imageconversion": "1.0.0" } }'

$log = Join-Path $Scratch 'capture.log'
$method, $fixtureArg, $fixture = if ($ShadowRecv) { 'CaptureShadowRecvBatch', '-lgShadowFixture', 'Evidence~\shadowRecvChunk.json' }
                                 elseif ($DynCast) { 'CaptureDynCastBatch', '-lgDynFixture', 'Evidence~\dynCastChunk.json' }
                                 elseif ($WaterRealtime) { 'CaptureWaterRealtimeBatch', '-lgFixture', 'Evidence~\realWaterChunk.json' }
                                 elseif ($WaterRecv) { 'CaptureWaterRecvBatch', '-lgWaterRecv', 'Evidence~\waterRecvChunk.json' }
                                 else { 'CaptureBatch', '-lgFixture', 'Evidence~\realWaterChunk.json' }
$extra = if ($WaterRealtime -and $WaterRecv) { @('-lgWaterRecv', "`"$(Join-Path $lg 'Evidence~\waterRecvChunk.json')`"") } else { @() }
$p = Start-Process -FilePath $Unity -Wait -PassThru -ArgumentList (@(
    '-batchmode', '-projectPath', "`"$Scratch`"", '-lgGamma',
    '-executeMethod', "UOClient.LiveGroundCompare.Editor.LiveGroundEvidenceCapture.$method",
    $fixtureArg, "`"$(Join-Path $lg $fixture)`"",
    '-lgOut', "`"$(Join-Path $lg 'Evidence~\stills')`"",
    '-logFile', "`"$log`"") + $extra)
Select-String -Path $log -Pattern 'LGEVIDENCE|LGSHADOWRECV|LGDYNCAST|LGWATERTIME|LGWATERRECV|evidence FAILED|error CS' | ForEach-Object { $_.Line }
Write-Output "unity exit $($p.ExitCode); log $log"
exit $p.ExitCode
