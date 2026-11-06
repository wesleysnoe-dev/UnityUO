# LiveGroundCompare Phase C evidence (isolated fixture)

- Captured: 2026-11-06 03:10:35 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; color space Gamma; batch mode True
- Project: `C:\Users\MR028D~1.SMA\AppData\Local\Temp\UOClient_LGEvidence`
- Fixture: `G:\UOClient\Assets\LiveGroundCompare\Evidence~\realWaterChunk.json`: map 0 chunk (1456, 1456) 16x16: 129 water tiles (graphics 0xA8, 0xA9, 0xAA, 0xAB), 127 land, 0 static occluder columns; map0 land/statics + tiledata via UOInstallFixture; tile colour = WaterSurfaceBuilder.MeanColor(classic land art, texmap: false)
- Camera: orthographic along -_EyeDir (0.50, 0.71, -0.50) (the presenter's view kernel), size 10, 768x480; NOT the presenter's exact oblique screen matrix.

## Stills
- `water_t0.png`: water, _LGEvidenceTime = 10.000 s (day, sun mask on) (sha256 87cdc6f20e7e1e71)
- `water_t1.png`: water, _LGEvidenceTime = 10.500 s (day, sun mask on) (sha256 12610b1a271937fd)
- `realtime_a.png`: shipped water shader, wall t = 4.011 s (EditorApplication.timeSinceStartup) (sha256 5cc064003a370ee8)
- `realtime_b.png`: shipped water shader, wall t = 5.035 s (EditorApplication.timeSinceStartup) (sha256 5cc064003a370ee8)
- `light_day_nosun.png`: day, sun mask replaced by white (no terrain shadow), t = 10.000 (sha256 9bab668966c49fa7)
- `light_night_playerlight.png`: fixture night ambient (0.32, 0.34, 0.48) + player light (0.90, 0.75, 0.50) r=7 at tile (1464, 1464), t = 10.000 (sha256 85cb6bdafe9a1f7c)
- `sort_noproxy.png`: terrain pass + WorldSprite markers, no depth proxy (sha256 a5731c6ae31c6068)
- `sort_proxy.png`: terrain pass + DepthKey proxy (ColorMask 0) + WorldSprite markers (sha256 268fdeb2a56630ff)
- `water_t0_t1_diff_x8.png`: |t1 - t0| per channel x8

## Measured
- Water motion (controlled): t0 = 10.000 s, t1 = 10.500 s, delta = 0.500 s shader time: 48357/368640 px changed (29961 by > 8/255), mean |delta| 3.093/255 per channel
- Same t0 rendered twice: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel (0 changed = deterministic; the t0/t1 delta is the time uniform)
- Water coverage (t0 with vs without the water object): 51687 px
- Shipped shader, Unity _Time: wall 4.011 -> 5.035 s (delta 1.025 s); Time.time 0.000 -> 0.000; Time.realtimeSinceStartup 0.119 -> 1.144: 0/368640 px changed (0 by > 8/255), mean |delta| 0.000/255 per channel
- Sun mask on vs off (day): 692/368640 px changed (388 by > 8/255), mean |delta| 0.018/255 per channel
- Day vs fixture night + player light: 97834/368640 px changed (97400 by > 8/255), mean |delta| 8.297/255 per channel
- Sorting: red marker (0.8 below the ground on the highest land tile) 656 px without proxy -> 339 px with proxy; green marker (above ground) 416 -> 416 px

Limits: one chunk, offline fixture (not a live session); fixture camera and night/player-light values are illustrative; tile colour is the mean classic land art per tile (not the presenter's baked chunk art); no FPS measured.
