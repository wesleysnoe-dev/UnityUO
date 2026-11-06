# LiveGroundCompare water sun-ceiling receive evidence (isolated fixture, TEST only, not logged in)

- Captured: 2026-11-06 04:18:30 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; color space Gamma; batch mode True
- Fixture: `G:\UOClient\Assets\LiveGroundCompare\Evidence~\waterRecvChunk.json` on `G:\UOClient\Assets\LiveGroundCompare\Evidence~\realWaterChunk.json`: map 0 chunk (1456, 1456); 129 water tiles (2064 ceiling texels); map0 corner heights + map static columns via UOInstallFixture; ceilings LiveGroundLighting.ShadowCeiling + DynamicShadow.Stamp (shipped code)
- CPU (DotNet): water texels in shadow (visibility < 0.5): own ceiling 0, with the column 30; probe water point (1470.5, 1457.5) Z -5 -> pixel (605, 241)
- Ceiling bound as SpriteShadowCeiling binds it: 1024^2 RHalf, Repeat, bilinear, block at the chunk's wrapped slot, _LGSpriteShadowMap.w = 1/256; camera as the Phase C capture (orthographic along -_EyeDir, NOT the presenter's oblique matrix).

## Equivalence (feature off / missing must be the 83c0a85 water)
- 83c0a85 water (in-memory copy of Evidence~/LiveGroundCompareWater.83c0a85.shader.txt) vs shipped, _LGCeilOn 0 with the cast ceiling bound, day: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- 83c0a85 vs shipped, _LGCeilOn 1 with an EMPTY ceiling (no loaded chunk), day: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- 83c0a85 vs shipped, _LGCeilOn 0, night + player light: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Untimed (Unity's own _Time, no time patch): 83c0a85 text vs the shipped asset, _LGCeilOn 0: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel

## Receive
- Off vs on with the chunk's own terrain ceiling: 162/368640 px changed (12 by > 8/255), mean |delta| 0.001/255 per channel
- Own ceiling vs + the column in open water: 997/368640 px changed (933 by > 8/255), mean |delta| 0.056/255 per channel; 972 px darker
- Water alone (ground hidden), own ceiling vs + column: 851/368640 px changed (538 by > 8/255), mean |delta| 0.040/255 per channel; 687 px darker (the shadow is on the water itself)
- Probe (3x3 mean rgb), day: off 39.8 -> on 30.5: ratio 0.767 (ambient term to the 0.7 floor, sun glint removed)

## Light coherence (the sun shadow dims only the ambient; the player's light is not shadowed)
- Night, no player light, probe: off 16.7 -> on 13.3: ratio 0.800
- Night + player light at the probe: off 38.9 -> on 35.0: ratio 0.899 (closer to 1: the lamp's share is not dimmed)

## Motion with the receive live
- t0 = 10.000 s vs t1 = 10.500 s, _LGCeilOn 1: 47960/368640 px changed (29710 by > 8/255), mean |delta| 3.054/255 per channel; same t0 twice: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Probe at t1: off 63.3 -> on 45.1: ratio 0.712 (the shadow stays while the waves move)
- Unity's own _Time across separate frames with the receive live: `waterrecv_realtime.md` (CaptureWaterRealtimeBatch -lgWaterRecv).

## Stills
- `waterrecv_off.png`: day, _LGCeilOn 0 (cast ceiling bound but off): the water as before, t = 10.000 (sha256 87cdc6f20e7e1e71)
- `waterrecv_static_on.png`: day, _LGCeilOn 1, the chunk's own terrain ceiling (no casting statics in this chunk) (sha256 b5754ed5c5eedb21)
- `waterrecv_on.png`: day, _LGCeilOn 1, ceiling + a 16 Z column in open water at (1469, 1457, -5) (sha256 d01bfb29bcd59100)
- `waterrecv_diff_x4.png`: |cast on - static on| x4 (the column's shadow on the water) (sha256 ff6a30c71da38ec4)
- `waterrecv_night_off.png`: night ambient (0.32, 0.34, 0.48) + player light (0.90, 0.75, 0.50) r=4 tiles at the probe, _LGCeilOn 0 (sha256 2808a360ad742a81)
- `waterrecv_night_playerlight_on.png`: same, _LGCeilOn 1 with the cast ceiling (sha256 ec2fb445e9d52b0d)
- `waterrecv_t0_on.png`: receive on, _LGEvidenceTime = 10.000 s (sha256 d01bfb29bcd59100)
- `waterrecv_t1_on.png`: receive on, _LGEvidenceTime = 10.500 s (sha256 62df22aed8912edf)
- `waterrecv_t0_t1_diff_x8.png`: |t1 - t0| x8, receive on (sha256 a003312ab5b347cb)

Limits: one chunk; a fixture column stands in for a mobile/boat (in game: whatever SpriteShadowCeiling holds); the water is shaded at its own surface point, not refracted or depth-aware; still a CPU ceiling, not a Unity shadow map; no FPS figures.
