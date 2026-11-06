# LiveGroundCompare water: Unity _Time across separate frames (isolated fixture, TEST only, not logged in)

- Captured: 2026-11-06 04:18:44 -08:00; Unity 6000.6.8f1; Direct3D12 / NVIDIA GeForce RTX 5080; batch mode True; process 81480
- Shipped `LiveGroundCompare/Water` (no override, no in-memory copy) on map 0 chunk (1456, 1456); a probe material drawn by the same camera writes the _Time.y / _Time.x / unity_DeltaTime.x that frame's render got into a float target.
- Each frame is a separate Editor update tick (EditorApplication.update), >= 0.5 s wall apart; within a frame the water is rendered twice (must be 0 px apart).

| frame | Time.frameCount | wall since arm (s) | probe _Time.y | probe _Time.x | unity_DeltaTime.x | Time.realtimeSinceStartup | same-frame repeat | vs previous frame |
|---|---|---|---|---|---|---|---|---|
| 0 (`waterrecv_realtime_0.png`, sha256 4df70d7e3005fe13) | 1 | 0.095 | 0.0000 | 0.0000 | 0.0001 | 0.159 | 0 px | - |
| 1 (`waterrecv_realtime_1.png`, sha256 7501f5ccb4446fd1) | 2 | 0.596 | 0.0200 | 0.0010 | 0.0200 | 0.651 | 0 px | 30161/368640 px changed (1538 by > 8/255), mean |delta| 0.183/255 per channel |
| 2 (`waterrecv_realtime_2.png`, sha256 c0951ca01db26b9b) | 5 | 1.096 | 0.6245 | 0.0312 | 0.1003 | 1.153 | 0 px | 48914/368640 px changed (32356 by > 8/255), mean |delta| 3.309/255 per channel |
| 3 (`waterrecv_realtime_3.png`, sha256 dcfb8324eacf92fe) | 6 | 1.599 | 0.8551 | 0.0428 | 0.2305 | 1.655 | 0 px | 44442/368640 px changed (19958 by > 8/255), mean |delta| 1.758/255 per channel |
| 4 (`waterrecv_realtime_4.png`, sha256 08c58107d620ee93) | 7 | 2.101 | 1.1884 | 0.0594 | 0.3333 | 2.156 | 0 px | 45689/368640 px changed (23289 by > 8/255), mean |delta| 2.216/255 per channel |

- Verdict: _Time.y advanced on every frame (0.000 -> 1.188 s) and the shipped water's pixels changed on all 4 frame steps.
- `waterrecv_realtime_first_last_diff_x8.png`: |last - first| x8.
- Sun-ceiling receive live during every frame: _LGCeilOn 1, cast ceiling of `waterRecvChunk.json` (column 16 Z at (1469, 1457, -5)).
- Limits: Editor batch frames (EditorApplication.update), not the player loop of a live session; one chunk; the probe proves the value Unity bound for _Time on each render, the pixel deltas that the shipped shader responds to it.
