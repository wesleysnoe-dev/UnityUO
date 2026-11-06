# Login background assets (Unity StreamingAssets)

- `login-bg-loop-0.5s-xfade.mp4` — chosen Unity playback asset. 1104×816, 24fps, H.264 yuv420p, NOAUDIO, duration 14.541667s. Baked 0.5s crossfade derivative; QA: softened loop, minor residual join/ghosting, NOT perfect seamless. Composition + bottom-right "THIS IS A VIDEO, NOT PLAYABLE" preserved. Do not crop or strip text. Use ordinary VideoPlayer loop — do NOT add another runtime crossfade on top.
- Music is NOT here: play UO data `Music/Digital/LoginLoop.mp3` (Config.txt index **78**, loop) independently; never restart music when the video loops.
- Provenance: see PROVENANCE.md / QA_RECONCILE_PROOF.md (copied from source-side prep). Originals remain under box `/workspace/login-background-assets/` and `C:\Users\Mr.Smallz\UOClient-login-assets\`.
