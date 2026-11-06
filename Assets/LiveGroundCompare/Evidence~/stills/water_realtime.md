# LiveGroundCompare water: Unity _Time across separate frames (isolated fixture, TEST only, not logged in)

- Captured: 2026-11-06 04:03:15 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; batch mode True; process 55108
- Shipped `LiveGroundCompare/Water` (no override, no in-memory copy) on map 0 chunk (1456, 1456); a probe material drawn by the same camera writes the _Time.y / _Time.x / unity_DeltaTime.x that frame's render got into a float target.
- Each frame is a separate Editor update tick (EditorApplication.update), >= 0.5 s wall apart; within a frame the water is rendered twice (must be 0 px apart).

| frame | Time.frameCount | wall since arm (s) | probe _Time.y | probe _Time.x | unity_DeltaTime.x | Time.realtimeSinceStartup | same-frame repeat | vs previous frame |
|---|---|---|---|---|---|---|---|---|
| 0 (`water_realtime_0.png`, sha256 5cc064003a370ee8) | 1 | 0.087 | 0.0000 | 0.0000 | 0.0001 | 0.150 | 0 px | - |
| 1 (`water_realtime_1.png`, sha256 bba42dfb7e11b13b) | 4 | 0.588 | 0.3032 | 0.0152 | 0.0946 | 0.642 | 0 px | 45806/368640 px changed (23318 by > 8/255), mean |delta| 2.198/255 per channel |
| 2 (`water_realtime_2.png`, sha256 c1badc58adcbe63a) | 5 | 1.090 | 0.4101 | 0.0205 | 0.1068 | 1.144 | 0 px | 41152/368640 px changed (12251 by > 8/255), mean |delta| 0.838/255 per channel |
| 3 (`water_realtime_3.png`, sha256 b4e22ef9252fa258) | 6 | 1.591 | 0.7434 | 0.0372 | 0.3333 | 1.644 | 0 px | 46326/368640 px changed (23599 by > 8/255), mean |delta| 2.306/255 per channel |
| 4 (`water_realtime_4.png`, sha256 687ca305503cef83) | 7 | 2.091 | 1.0767 | 0.0538 | 0.3333 | 2.145 | 0 px | 46171/368640 px changed (24213 by > 8/255), mean |delta| 2.406/255 per channel |

- Verdict: _Time.y advanced on every frame (0.000 -> 1.077 s) and the shipped water's pixels changed on all 4 frame steps.
- `water_realtime_first_last_diff_x8.png`: |last - first| x8.
- Limits: Editor batch frames (EditorApplication.update), not the player loop of a live session; one chunk; the probe proves the value Unity bound for _Time on each render, the pixel deltas that the shipped shader responds to it.
