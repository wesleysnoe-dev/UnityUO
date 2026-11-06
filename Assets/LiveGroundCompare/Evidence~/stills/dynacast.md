# LiveGroundCompare dynamic cast / margins / water _Time: measured (TEST only, isolated fixtures, not logged in)

Written 2026-11-06. Labelled **dynamic moving approximation** on the CPU sun-shadow ceiling: not Unity shadow maps. No FPS claims.

## CPU per frame (DotNet `LiveGroundDynCastFixtureTests.Perf_ThreeByThreeTownBlock_WalkingMobilesAndItems_AfterWarmup`)

block 3x3 chunks around town chunk (1424,1712) map 0; 1258 items (893 casting) + 40 walking mobiles; casters 1298 (933 casting); margins warmup 9 frames; measured 600 frames after 120 warmup: Frame() mean 0.232 ms, p50 0.234, p95 0.391, max 0.656; chunks redone/frame mean 3.36 max 8; texels stamped/frame mean 2274; allocated 0 bytes over 600 frames; one margin recompute 0.794 ms (main thread, budget 1/frame). CPU only (no texture upload; see dynacast.md for the Unity upload). .NET 10.0.14, 16 logical CPUs.

An earlier run of the same test (budget 2/frame then): Frame() mean 0.217 ms, p95 0.353, one margin 0.607 ms, 0 bytes.

- Covers what the main thread does in `SpriteShadowCeiling.Apply` besides the upload: every caster's `Report`
  (one dictionary lookup when it stays on its tile) and `ShadowCeilingCore.Frame` (sweep, margins, recompose).
- The half-float conversion of changed blocks (4096 `Mathf.FloatToHalf` per chunk) and the upload are Unity-side.
  The upload is timed below.

## Upload (Unity batch, `dynacast_capture.md`)

- Full 1024^2 RHalf `SetPixelData` + `Apply`: 0.60 ms CPU (1.06 / 1.18 ms in two earlier runs), 0 B allocated.
- 4 changed chunks via 64^2 staging + `Graphics.CopyTexture`: 0.033 ms CPU (0.061 / 0.080 ms earlier), 0 B.
- CPU submit time on the main thread only (GPU time not measured). RTX 5080, D3D12, Unity 6000.6.8f1.

## Correctness (DotNet, Unity-free, shipped code)

- `Stamp_IsExactlyAStaticColumnInCeilingAt`: a stamped mobile/item column is bit-equal to `ShadowCeiling` with a
  static column there (flat and uneven ground, 4 placements).
- `Core_MoveAndRemove_InvalidateExactly_NoStaleShadow` (mobile and item layers): a move leaves exactly the new column.
  An undrawn caster is swept, and the chunk returns bit-equal to its static ceiling. Same tile costs 0 recomposes.
- `Core_ACasterNearTheEdgeShadesTheEastNeighbourToo_AndOnlyTheChunksItTouches`: only footprint chunks are redone.
- `Margin_WestAndNorthNeighboursCastAcrossTheSeam_AsOneWorldWould`: margin base = world-wide brute-force ray over
  the loaded chunks, 0 mismatching texels.
- `Margin_MissingNeighbourIsLit_ThenFillsIn_ThenGoesAwayWithIt_NoStaleSeam`; `Margin_WorkIsBudgetedPerFrame_AndCounted`.
- `Light_SunShadowDimsOnlyTheAmbient_NotThePlayersLight`.

## GPU stills (`dynacast_capture.md`, `water_realtime.md`)

- Receiver foot ratio 0.708 with the caster west of it, 1.000 after it moved, 0 px stale after removal. At night
  under the player light: 0.931.
- Margins: 1,649 px darker along the west/north seams with the 3 real neighbours loaded.
- Keyword off: 0 px with any ceiling bound. Receive capture re-run: keyword off vs 4aa51a6 still 0 / 1,048,576 px.
- Water: the shipped shader on 5 separate Editor frames, in 2 separate batch processes. The probe `_Time.y` rose
  every frame (to 1.267 s and to 1.077 s), pixels changed on every step, and same-frame repeats were 0 px.

## Limits

- Mobiles are tile-snapped 16 Z columns (no silhouette, no own-tile self-shadow; the shadow steps a tile at a time).
  Items cast by their tiledata height. The multi placement preview casts while it is shown.
- Margins come from loaded neighbours only (W/N/NW). They are budgeted at 1 chunk per frame, and an edge reads lit
  until its margin is done. Rays beyond one neighbour chunk stop.
- Shore/height/depth/sorting: no new defect was found, so nothing was changed there (the Phase C sort and water stills
  were re-run and are unchanged apart from timestamps).
- Fixture evidence only (one chunk drawn; Editor frames). No live session, no FPS.