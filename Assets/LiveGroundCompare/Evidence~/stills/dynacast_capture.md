# LiveGroundCompare dynamic cast + cross-chunk margin evidence (isolated fixture, TEST only, not logged in)

- Captured: 2026-11-06 04:02:34 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; color space Gamma; batch mode True
- Fixture: `G:\UOClient\Assets\LiveGroundCompare\Evidence~\dynCastChunk.json` on `G:\UOClient\Assets\LiveGroundCompare\Evidence~\shadowRecvChunk.json`: map 0 chunk (1424, 1712); map0 corner heights + map static columns via UOInstallFixture; ceilings from ShadowCeilingCore (shipped code); map0 land/statics + tiledata + anim body 400 via UOInstallFixture; statics = IsoCompositor.BuildDrawList (production placements, scale 1)
- Caster A (1435, 1726, 20) -> B (1435, 1727, 20); receiver (1436, 1726, 20); mobile column 16 Z (TazUO DEFAULT_CHARACTER_HEIGHT); texels raised by A 45, by B 21; margin texels raised 271 by 3 real neighbours
- Ground drawn in terrain space through LiveGroundLighting.TerrainToUnityRows (the presenter's camera rows), so TerrainLit samples the world ceiling at its own tile point; sprites as the receive capture (one world pixel per output pixel). Bodies are a real body-400 stand frame.

## Ground path
- Terrain-space ground vs the receive capture's screen-plane ground (ceiling off): 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Terrain reading the chunk's own (static) world ceiling vs its mask only: 374/1048576 px changed (5 by > 8/255), mean |delta| 0.001/255 per channel

## Dynamic cast (mobile column, labelled approximation, not a shadow map)
- Static vs caster at A: 2331/1048576 px changed (949 by > 8/255), mean |delta| 0.026/255 per channel; 2331 px darker
- Receiver at A: foot px (-6380, 69506) off (24,24,24) on (17,17,17) (body art (24,24,24)): ratio 0.708; head px (-6381, 69452) off (181,181,181) on (181,181,181) (body art (181,181,181)): ratio 1.000
- Static vs caster at B: 916/1048576 px changed (214 by > 8/255), mean |delta| 0.004/255 per channel; receiver foot px (-6380, 69506) off (24,24,24) on (24,24,24) (body art (24,24,24)): ratio 1.000; head px (-6381, 69452) off (181,181,181) on (181,181,181) (body art (181,181,181)): ratio 1.000
- Removed (swept) vs static, receiver only: 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel (0 = no stale shadow)

## Light coherence
- Night + player light at the receiver, static vs caster at A: 2215/1048576 px changed (490 by > 8/255), mean |delta| 0.010/255 per channel; receiver foot px (-6380, 69506) off (29,26,23) on (27,23,20) (body art (24,24,24)): ratio 0.931; head px (-6381, 69452) off (120,113,121) on (120,113,121) (body art (181,181,181)): ratio 1.000
  (day foot ratio 0.708 vs night-under-the-lamp 0.931: the sun shadow dims only the ambient, the player's light is not shadowed by it)

## Cross-chunk margin
- Chunk alone vs with its loaded neighbours: 1649/1048576 px changed (1197 by > 8/255), mean |delta| 0.016/255 per channel; 1649 px darker

## Keyword off
- Keyword off, _LGCeilOn 0: margin+cast ceiling vs static ceiling bound: 0/1048576 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel (0 = the production path ignores the ceiling)

## Upload (SpriteShadowCeiling, 1024^2 RHalf)
- Full SetPixelData + Apply (2 MB): 0.599 ms CPU per upload, 0 B allocated over 60
- 4 changed chunks via 64^2 staging + Graphics.CopyTexture: 0.033 ms CPU per frame, 0 B allocated over 60
- (after 5 warmup calls; CPU submit time on the main thread, not GPU time)

## Stills
- `dynacast_static.png`: caster body at A (1435, 1726, 20) + receiver at (1436, 1726, 20); ceiling WITHOUT the caster (static only) (sha256 8595f4de3b0d39ab)
- `dynacast_castA.png`: same, ceiling with the mobile column at A (dynamic moving approximation, 16 Z) (sha256 ef1046b95e469fd7)
- `dynacast_castA_diff_x4.png`: |castA - static| x4 (sha256 037479c5a2d3cc95)
- `dynacast_castA_zoom.png`: x4 crop around A: left static ceiling, right with the caster column (sha256 ded7b8eb7ed03377)
- `dynacast_castB.png`: caster moved to B (1435, 1727, 20); ceiling recomposed (A's column gone) (sha256 26f058264cc6d7fb)
- `dynacast_removed.png`: caster gone (not drawn for a frame: swept); receiver only (sha256 df6ad8f5f9a5849e)
- `dynacast_night_castA.png`: night ambient (0.32, 0.34, 0.48) + player light r=140 px at the receiver, caster at A (sha256 60cfbea97d5c2d10)
- `margin_off.png`: chunk alone (neighbours not loaded: their rays lit, the old seam) (sha256 c926869a6a071027)
- `margin_on.png`: same chunk with its 3 real neighbours loaded: their shadow continues across the west/north seams (sha256 8cce656bb948afe8)
- `margin_diff_x4.png`: |margin on - off| x4 (sha256 5248c0dc831bdcb5)
- `margin_castA.png`: neighbours loaded + caster at A + receiver (sha256 836b99197fe12708)

Limits: one chunk drawn (the neighbours' geometry is not drawn, only their CPU heights/columns cast into it); a mobile is a 16 Z column on its foot tile (tile-snapped, no silhouette, no self-shadow on its own tile); items cast by tiledata height like map statics; still a CPU ceiling, not a Unity shadow map; no FPS figures (CPU per-frame cost is measured in DotNet: see dynacast.md / LiveGroundDynCastFixtureTests).
