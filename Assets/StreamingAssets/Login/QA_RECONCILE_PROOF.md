# QA reconcile proof — login seam assets

**Time (America/Los_Angeles):** 2026-11-05 ~20:07 PT  
**Scope:** objective ffprobe / blackdetect / frame MAE only. **No seamless claim. No Unity/integration claim.**  
**Do not treat OCR of burned-in bottom-right source text as authoritative** (videoReview disagreed with earlier watchVideo); labels below are QA overlays only.

---

## Verdict on prior videoReview of `login-bg-seam-qa.mp4` (v1)

| Review claim | Objective finding |
|---|---|
| No black separator | **False.** `blackdetect` on v1: `black_start:4 black_end:4.25 black_duration:0.25`. Separator existed but was only **0.25 s** — easy to miss under sparse sampling. |
| No crossfade | **False for the loop derivative.** Mid-xfade frame is near the arithmetic blend of end/start; hard-cut baseline is much larger (see §Fade). v1 QA *structure* was five ~2 s windows, so most visible joins were **intentional hard repeats**, which can look like “all seams abrupt.” |
| Repeated 2 s hard loop ~5 times | **Structurally fair description of v1 QA packing**, not proof the loop asset lacked an xfade. v1 = (orig 2 s)×2 + 0.25 s black + (loop-window 2 s)×3. |
| Bottom-right text differs from earlier review | **Unresolved / non-authoritative.** No OCR used here; composition preserved (no crop). Treat text reads as reviewer sampling variance. |

**Loop final asset was not rebuilt** — metrics show the 0.5 s `xfade` is present. Defect was **QA packaging / separator length / labeling**, not absence of fade in `login-bg-loop-0.5s-xfade.mp4`.

---

## Source & finals (unchanged hashes where noted)

| File | Duration | Frames | MD5 |
|---|---|---|---|
| `source-original.mp4` | 15.041667 s | 361 | `0273743a670c8fd5d5f5ba601c609846` |
| `login-bg-muted.mp4` | 15.041667 s | 361 | `f21e35367be6c450df1643d26f5c1514` |
| `login-bg-loop-0.5s-xfade.mp4` | **14.541667 s = D−L** | 349 | `793b3d88781e6bef76ef30beb0e86ab4` |
| `login-bg-seam-qa-v1-superseded.mp4` | 10.250 s | 246 | `46dd19af912495d47c5a6b4f31137e22` |
| `login-bg-seam-qa-v2.mp4` | **44.083333 s** | 1058 | `d8ea2786d98580202519f8dee14697c1` |
| `login-bg-seam-qa-short.mp4` | **7.000 s** | 168 | `cc7f9fab97a612de1ff652b38fbab3b4` |

Construction of loop (unchanged): main = trim `[L .. D−L]` with L=0.5; then `xfade=fade:duration=0.5:offset=0` of trim`[D−L .. D]` over trim`[0 .. L]`; concat; no audio; 1104×816 yuv420p.

---

## §Fade — objective pixel proof (`login-bg-loop-0.5s-xfade.mp4`)

Frame index map @ 24 fps: main frames `n=0..336` (337 frames); xfade `n=337..348` (12 frames = 0.5 s).

| Comparison | MAE (0–255, RGB) |
|---|---|
| muted end-mid vs muted start-mid (**hard jump**) | **38.535** |
| loop `n342` (~50% xfade) vs arithmetic mean(end-mid, start-mid) | **7.022** |
| loop `n342` vs end-mid alone | 17.901 |
| loop `n342` vs start-mid alone | 23.520 |
| loop `n0` vs muted @ t=0.500 (content at L) | **1.833** |
| loop `n348` (xfade end) vs loop `n0` (**period join**) | **8.382** — imperfect; **not seamless** |

Consecutive-frame MAE:

| Transition | MAE |
|---|---|
| Normal motion muted `n359→n360` | 6.652 |
| Normal motion muted `n0→n1` | 4.297 |
| Xfade steps `n336→…→n348` | **5.97–8.18** each |
| Hard loop muted `n360→n0` | **38.046** |

Interpretation: xfade replaces a ~38 MAE discontinuity with a chain of ~6–8 MAE steps (motion-like). Period join still has MAE≈8.4 residual — do **not** call seamless.

v1 `blackdetect`: `black_start:4 black_end:4.25 duration:0.25`.  
v1 had **no** on-screen timestamps; easy to mis-describe under sampling.

---

## New QA files (labels only on QA; finals untouched)

### `login-bg-seam-qa-v2.mp4` (primary for next videoReview)

Timeline (America/Los_Angeles file clock irrelevant; media time below):

| Media t | Section |
|---|---|
| 0.000–3.000 | A1 ORIGINAL HARD SEAM (muted last 1.5 + first 1.5) |
| 3.000–6.000 | A2 same hard seam repeat |
| **6.000–7.000** | **1.0 s BLACK separator** (yellow centered label) |
| 7.000–10.000 | B1 LOOP XFADE+JOIN window (last 1.5 of loop + first 1.5); lime **XFADE ZONE** overlay at local 1.0–1.5 s |
| 10.000–13.000 | B2 same |
| **13.000–14.000** | **1.0 s BLACK** |
| 14.000–28.542 | C1 full loop period #1 (14.541667 s); XFADE ZONE overlay on final 0.5 s |
| **28.542–29.542** | **1.0 s BLACK** |
| 29.542–44.083 | C2 full loop period #2 |

`blackdetect d=0.5 pix_th=0.10` on v2 (measured):

```
black_start:6          black_end:7           duration:1
black_start:13         black_end:14          duration:1
black_start:28.541667  black_end:29.541667   duration:1
```

Matches expected separator starts exactly.

### `login-bg-seam-qa-short.mp4` (7.0 s seam-only)

`A (3s) + BLACK (1s) + B (3s)`.  
`blackdetect`: `black_start:3 black_end:4 duration:1`.

On-screen QA overlays: section title, running `t=%{pts:hms}`, and conditional **XFADE ZONE** — **not** present on `login-bg-muted.mp4` / `login-bg-loop-0.5s-xfade.mp4`.

---

## What to send to videoReview next

1. **`/workspace/login-background-assets/login-bg-seam-qa-short.mp4`** — quick confirm of hard vs xfade window + unmistakable 1 s black.  
2. **`/workspace/login-background-assets/login-bg-seam-qa-v2.mp4`** — full two-period boundary check with separators.  
3. Finals for integration later (unchanged): `login-bg-muted.mp4`, `login-bg-loop-0.5s-xfade.mp4`.

Independent music still: `/workspace/uo-data/7.0.23.1/Music/Digital/LoginLoop.mp3` (index 78) — not muxed.
