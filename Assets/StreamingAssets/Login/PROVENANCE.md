# Login background video assets — provenance

**Prepared (America/Los_Angeles):** 2026-11-05 ~20:01 PT  
**Scope:** media transforms only. No Unity/repo edits, no desktop work, no live/server changes.  
**Visual claims:** none — seam QA is for parent `videoReview` / watchVideo. This note does **not** claim the loop is seamless.

Brief referenced: `/workspace/login_background_asset_brief/README.md`

---

## Authorized source

| Field | Value |
|---|---|
| Parent chat attachment path | `/home/box/agent-data/agents/886a5c0c-bdc8-405c-9a8b-21d7585d4052/attachments/aa075e28bb6c3372d96049fbea7dd7b8b6186ed2bbd18accf8ffff03fbbe758f.mp4` |
| Resolves (symlink) to | `/cursor/stores/store-b845e3c2-2e05-4f20-81d1-c18424034c02/grok-bot/agents/886a5c0c-bdc8-405c-9a8b-21d7585d4052/attachments/01M6QEEFZ886XX7W6XK9R6K06B.mp4` |
| Access | Readable on this box via sanctioned agent attachment path (no auth bypass) |
| Size | 23113316 bytes |
| MD5 | `0273743a670c8fd5d5f5ba601c609846` |

### Measured source (ffprobe)

| Property | Value |
|---|---|
| Duration D | **15.041667 s** |
| Video | H.264 High, **yuv420p**, progressive, bt709 |
| Resolution | **1104×816** (under 1080p height → kept native; no upscale, no crop) |
| Frame rate | **24 fps** (361 frames) |
| Audio (source) | AAC LC stereo 48 kHz ~128 kbps, 15.000000 s — **removed in derivatives** |
| Extra | Attached MJPEG cover art stream present on source; not copied into muted/loop/QA |

Preserved byte copy: `source-original.mp4` (same MD5 as attachment target).

---

## Outputs (`/workspace/login-background-assets/`)

### 1. `login-bg-muted.mp4` — Unity import candidate (primary)

| Property | Value |
|---|---|
| Purpose | Source-audio-free H.264/yuv420p for login VideoPlayer(s); music stays separate |
| Method | `-map 0:v:0 -c:v copy -an` from authorized source (no re-encode, no scale/crop) |
| Duration | 15.041667 s |
| Size | 22711914 bytes |
| MD5 | `f21e35367be6c450df1643d26f5c1514` |
| Streams | video only (H.264, 1104×816, yuv420p, 24 fps) |

### 2. `login-bg-loop-0.5s-xfade.mp4` — baked 0.5 s end/start overlap derivative

| Property | Value |
|---|---|
| Purpose | Optional baked approximation of brief §2.4 dual-player blend; runtime dual-player still preferred per brief |
| L (overlap) | **0.5 s** |
| Construction | `[L .. D−L]` + `xfade(fade)` of `[D−L .. D]` over `[0 .. L]`; continue-from-L semantics so hard-loop of this file joins at content time L |
| Period / duration | **D − L = 14.541667 s** (349 frames @ 24 fps) |
| Encode | libx264 High, yuv420p, CRF 18, 24 fps, no audio, no crop |
| Size | 18705971 bytes |
| MD5 | `793b3d88781e6bef76ef30beb0e86ab4` |

**Not claimed seamless.** Ghosting vs jump tradeoff requires parent watched QA.

### 3. `login-bg-seam-qa.mp4` — short repeated-seam QA (for parent videoReview)

| Property | Value |
|---|---|
| Duration | 10.250000 s (246 frames) |
| Size | 11071326 bytes |
| MD5 | `46dd19af912495d47c5a6b4f31137e22` |
| Layout | (A) original hard cut seam window ×2 — muted last 1.0 s + first 1.0 s; (B) 0.25 s black; (C) loop xfade+join window ×3 — loop last 1.0 s (incl. 0.5 s xfade) + first 1.0 s |
| Audio | none |
| Use | Parent should run videoReview / watchVideo on this file; do not treat this note as visual verification |

### 4. `source-original.mp4`

Unmodified copy of authorized source (MD5 `0273743a670c8fd5d5f5ba601c609846`). Attachment left untouched.

---

## Independent menu music (not mixed into video)

| Property | Value |
|---|---|
| Path | `/workspace/uo-data/7.0.23.1/Music/Digital/LoginLoop.mp3` |
| Config | `Music/Digital/Config.txt` → `78 LoginLoop.mp3,loop` |
| Index | **78** |
| MD5 | `6aa2f398a001302aa47d43d457704df0` |
| Duration | 31.425300 s (stereo 44.1 kHz MP3 160 kbps) |

**Do not mux into video.** Play on a separate AudioSource; mute/disable all video audio; do not restart music on video blend/loop boundaries.

---

## Integration reminders (no code done here)

1. Prefer `login-bg-muted.mp4` + runtime dual VideoPlayer crossfade (L=0.5, test 0.25–0.75) per brief; or trial `login-bg-loop-0.5s-xfade.mp4` with simple looping after seam QA.
2. Contain + dark pillarbox; preserve full frame including bottom-right text; no watermark/text removal.
3. Stop/release video + LoginLoop on enter-game / login teardown.
4. Unity wiring owned by existing desktop Claude workstream — not this asset job.

---

## Processing logs

`ffmpeg-muted.log`, `ffmpeg-loop.log`, `ffmpeg-qa*.log` in this directory.
