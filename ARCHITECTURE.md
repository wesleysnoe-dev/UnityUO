# Unity UO Client — Architecture Plan

An overhauled Ultima Online client built in Unity. It keeps UO's isometric look and gameplay but uses updated, painted-style graphics that are generated locally with ComfyUI. The client stays playable on low-end machines. It targets ModernUO and reads the player's own installed UO data files.

**Visual direction:** "modern classic." The grid, projection and gameplay stay exactly UO; the art, lighting and UI are new (see the mockup: painted textures, warm light, parchment UI, grid backpack, tabbed journal, round minimap).

---

## 1. Goals and constraints

| Goal | Notes |
|---|---|
| Protocol-compatible with **ModernUO** (primary) | Pin one client version (7.0.x). The packet table is versionable. ModernUO accepts unencrypted clients. Custom packets for new features are possible because we control the server. |
| Reads the player's own UO install | Supports both MUL and UOP. **Never ship EA's data files**: the user points the client at their install. The new art ships as our own packs. |
| Isometric look with updated graphics | 2× HD art (88 px tiles) via an override system. The original art is always the fallback. |
| Low-end friendly | Graphics tiers: Classic / Enhanced / Full. One batched draw path for all tiers. Compressed texture packs. |
| Runs outside Unity for tests | All game logic lives in a pure C# core with no `UnityEngine` references. |
| Moddable | Gumps, hotkeys and macros are data-driven. Leave plugin hooks open for later. |

**Reference material:** ClassicUO (BSD-2) is the most complete open-source spec of real client behavior: packet handling, draw order, walking, gumps. Read it, port from it, and credit it where code is reused. ModernUO is the local test server.

**Engine:** Unity 6 LTS, URP (2D Renderer), .NET Standard 2.1 API level, IL2CPP for release builds.

---

## 2. Layered architecture

```
┌──────────────────────────────────────────────────────────┐
│  Unity Presentation  (UOClient.Unity  – MonoBehaviours)  │
│  WorldRenderer · GumpLayer · Input · Audio · Camera      │
└──────────────▲───────────────────────────┬───────────────┘
               │ reads state / events      │ commands (walk, use, say)
┌──────────────┴───────────────────────────▼───────────────┐
│  Game Core  (UOClient.Core – pure C#, noEngineReferences) │
│  World model · Entities · Movement · Targeting · Journal │
│  Packet handlers  ←→  Outgoing packet builders           │
└──────────────▲───────────────────────────┬───────────────┘
               │ decoded packets           │ raw bytes
┌──────────────┴──────────────┐  ┌─────────▼───────────────┐
│ Network (UOClient.Net)      │  │ Assets (UOClient.IO)    │
│ Socket · Huffman · Crypto   │  │ MUL/UOP readers         │
│ Packet framing · Len table  │  │ Decoders (art, gump,    │
│ Background I/O thread       │  │ anim, map, hues, fonts) │
└─────────────────────────────┘  └─────────────────────────┘
```

Each box is an assembly definition (`.asmdef`). Only `UOClient.Unity` references `UnityEngine`. The other three build as plain .NET libraries and get xUnit/NUnit tests that run in CI without the editor.

### Project layout

```
UOClient/
├─ Assets/
│  ├─ UOClient.Unity/        # MonoBehaviours, renderers, shaders, scenes
│  │  ├─ Rendering/          # WorldRenderer, Atlas, HueShader, LandMesh
│  │  ├─ Gumps/              # Gump controls, text input, drag/drop
│  │  ├─ Input/              # New Input System actions, mouse picking
│  │  └─ Audio/
│  ├─ UOClient.Core/         # asmdef: noEngineReferences = true
│  ├─ UOClient.Net/          # asmdef: noEngineReferences = true
│  └─ UOClient.IO/           # asmdef: noEngineReferences = true
├─ Tests/                    # dotnet test project linking the Core/Net/IO sources
└─ Tools/                    # Editor windows: art browser, map viewer, packet log viewer
```

---

## 3. Asset layer (UOClient.IO)

### File access
- `IFileContainer` abstracts the two formats:
  - `MulFile` + `IdxFile`: 12-byte index entries (offset, length, extra).
  - `UopFile`: `MYP\0` header, block tables, filename hashes (Jenkins `hashlittle2`) mapped to entries. Some entries are zlib-compressed.
- Open files with `MemoryMappedFile`. Decode lazily per ID and cache with LRU eviction.
- `verdata.mul` patches are supported only for very old installs. Low priority.

### Decoders (return raw pixel buffers, not Unity textures)
| Asset | Source | Format notes |
|---|---|---|
| Land art | `art.mul` / `artLegacyMUL.uop` IDs 0–0x3FFF | 44×44 raw diamond, ARGB1555 |
| Static art | same, IDs 0x4000+ | Run-length encoded rows |
| Land textures | `texmaps.mul` | 64×64 / 128×128, used for stretched land |
| Gumps | `gumpart*.uop` / `gumpart.mul` | RLE (value, run) pairs per row |
| Map | `map0LegacyMUL.uop` … | 196-byte blocks: 4-byte header + 64 cells × (u16 tile, s8 z) |
| Statics | `statics0.mul` + `staidx0.mul` | 7 bytes: u16 id, u8 x, u8 y, s8 z, u16 hue |
| Tiledata | `tiledata.mul` | Land/static flags (Impassable, Surface, Wet, Background, Foliage, etc.), height, name. Layout changed in the High Seas era: 64-bit flags. |
| Hues | `hues.mul` | Groups of 8 hues; each hue has 32 ARGB1555 colors plus a range |
| Animations | `anim*.mul`, `AnimationFrame*.uop` | Plus `body.def`, `bodyconv.def`, `mobtypes.txt`, `Equipconv.def` |
| Multis | `multi.mul` / `MultiCollection.uop` | House/boat component lists |
| Fonts | `fonts.mul`, `unifont*.mul` | ASCII and Unicode bitmap fonts |
| Text | `cliloc.enu` | Localized strings with `~1_ARG~` substitution |
| Sound | `sound.mul` / `soundLegacyMUL.uop` | 16-bit PCM 22 kHz |
| Light | `light.mul` | Light shapes for light sources |

**Validation:** build an editor tool that renders any art/gump/anim ID next to its ClassicUO output (or a reference PNG). Decoder bugs are much cheaper to catch here than in the world view.

---

## 4. Network layer (UOClient.Net)

### Connection lifecycle
```
Login server                                   Game server
────────────                                   ───────────
→ 0xEF seed + client version
→ 0x80 account login
← 0xA8 server list
→ 0xA0 select server
← 0x8C relay (IP, port, auth key) ──────────▶  → 4-byte key, 0x91 game login
                                               ← (Huffman on from here on)
                                               ← 0xB9 features, 0xA9 char list
                                               → 0x5D / 0x00 select / create char
                                               ← 0x1B login confirm
                                               ← world packets…
                                               ← 0x55 login complete
```

### Design
- **I/O thread:** a dedicated socket reader thread. It decompresses Huffman, frames packets with the length table (fixed length, or variable with a u16 length at bytes 1–2), and pushes `PacketBuffer`s into a lock-free `ConcurrentQueue`.
- **Main thread:** `NetClient.Pump()` drains the queue once per frame and dispatches to `PacketHandlers[id]`. The world model is only ever changed on the main thread.
- **Send path:** `PacketWriter` (pooled, big-endian), written straight to the socket.
- **Encryption:** start with none. ServUO accepts unencrypted clients. Implement login/game encryption later, only if needed for a particular shard.
- **Version table:** `ClientVersion` selects packet lengths and feature flags (for example 0xF3 vs 0x1A world item, 0x0B vs 0xBF.0x2F, 0xB0 vs 0xDD gumps).
- **Packet logger:** a ring buffer of in/out packets with hex dumps, viewable in the editor. Essential for debugging.

### Core packet set for first playable
`0x1B 0x20 0x55 0x78 0x77 0x1D 0x1A/0xF3 0x2E 0x24 0x3C 0x25 0x22 0x21 0x02 0x11 0x1C 0xAE 0xC1 0x6C 0x73 0xBF 0xB0/0xDD 0x88 0x98 0xD6 0xBC 0x4E 0x4F 0x65 0x54 0x6D`

---

## 5. Game core (UOClient.Core)

### World model
- `World` holds the `Map` (land and statics, loaded in 8×8 blocks around the player), `EntityStore` (`Dictionary<Serial, Entity>`) and `Player`.
- Entities: `Item`, `Mobile`, `Multi`. Each has serial, graphic, hue, position (x, y, z), direction and flags. Mobiles add equipment by layer, stats and notoriety.
- Containers: an item's parent is either the world or a container/mobile serial.
- **Events:** `EntityAdded/Updated/Removed`, `SpeechReceived`, `GumpOpened` and so on. The presentation layer subscribes to these instead of polling.

### Movement (the subtle part)
- Client-side prediction. Send `0x02` with a sequence number and direction, and move immediately if the local walkability check passes.
- Walkability needs tiledata flags, land z (average of corners), statics/item surfaces and step height (≤ 2 up). Port ClassicUO's `Pathfinder.CanWalk` closely.
- On `0x22` ACK, pop the step. On `0x21` deny, snap back to the server's position.
- Throttle: walk 400 ms / run 200 ms per tile (mounted runs are faster).
- Click-to-move pathfinding uses A* on the same walkability rules.

### Other systems
Targeting (0x6C), context menus (0xBF.0x14), drag/drop (0x07/0x08), journal, skills, spellbook, party, buff bar, macros and hotkeys (`Input → Command` mapping saved as JSON).

---

## 6. Presentation (UOClient.Unity)

### World rendering — the biggest technical decision
UO is isometric 2.5D. Thousands of sprites have to sort correctly, and Unity's default SpriteRenderer sorting is neither fast nor flexible enough for this.

**Recommendation: a custom batched renderer.**
- **Texture atlases:** decoded art is packed at runtime into large `Texture2D` pages (e.g. 4096²) with a shelf packer, and evicted per page.
- **Draw list per frame:** gather visible land, statics, items and mobiles. Each gets a sort key `(x + y) · bigN + z-priority + type order + sequence`, using ClassicUO's priority rules for tall statics, mobiles on stairs, etc. Sort, then build a single dynamic `Mesh` (or a few, one per atlas page) and issue them with `Graphics.RenderMesh`.
- **Projection:** screen x = (x − y) · 22, screen y = (x + y) · 22 − z · 4, using an orthographic camera at 1 unit = 1 px.
- **Stretched land:** where neighboring z values differ, draw land as a 4-vertex quad using the texmap texture, with per-vertex normals for UO's classic slope shading.
- **Hues:** upload `hues.mul` as a 32×3000 lookup texture. One shader handles normal, full, partial (gray-only) and translucent hues, selected by per-vertex data.
- **Roof/occlusion:** hide statics above the player's z when the player is under a roof, as the classic client does (the "circle of transparency" is optional).
- **Mouse picking:** walk the sorted draw list back-to-front and do per-pixel alpha tests against the CPU-side pixel buffer.

Alternative considered: SpriteRenderer with a custom `sortingOrder`. It is fine for a prototype but falls over at 5–10k sprites and gives poor control of draw order.

### Animations
- `AnimationManager` resolves body → file/group through `bodyconv.def` and `mobtypes`. It decodes frames on demand into the atlas.
- Equipment layers draw in the fixed layer order that depends on direction. Mounts draw under the rider.

### Gumps / UI
- Gumps are absolutely positioned bitmaps plus text, so treat them as a 2D scene, not a layout problem.
- **Recommendation:** uGUI Canvas in Screen Space Overlay, with a small `GumpControl` hierarchy (GumpPic, Button, TextEntry, HtmlGump, Checkbox, Radio, ScrollBar, ResizePic). This gives input focus, IME and text entry for free.
- Server gumps (0xB0/0xDD) are parsed into a layout command list and then instantiated into controls. The reply goes out as 0xB1.
- Text uses the UO bitmap fonts rendered into atlases. A TextMeshPro fallback is an option for the HD UI mode.

### Audio
`AudioClip.Create` from `sound.mul` PCM. Music is MP3/MIDI from the install's `Music/` folder.

### Modern UI (per the mockup)
- **Status bars, tabbed journal (All/Guild/Party/Private/System/Emote), overhead names, round minimap:** standard gump controls with a new skin.
- **Grid backpack:** a client-side view only. The server still stores x/y positions in the container, and the client keeps its own grid layout per container.
- **Paperdoll portrait:** rendered live from the in-world character at high resolution, so it always reflects the gear actually equipped. There is no per-item paperdoll art.
- Classic UO skins stay available as an option.

---

## 7. Art pipeline (ComfyUI, local, RTX 5080)

### Decision (2026-11-05): bake per placement for towns, per-graphic sprites elsewhere
The first real ComfyUI test showed that one HD sprite per graphic can't reproduce AI-painted art. UO builds roofs, walls and ground from a few overlapping pieces reused hundreds of times, and the original art only works because it was hand-made to repeat. Each copy that ComfyUI paints is different, so any single copy shows seams and repetition when reused. Measured on a Britain block: mean error 16 per channel for per-graphic sprites, 0.0 for baked placements.

- **Baked placements:** map statics and land are fixed data, so every placement (tile, z, graphic, ordinal) keeps its own painted pixels. These are cut from the restyled composite by the ownership map and stored in atlas pages with an offset from the tile anchor. They are still separate sprites, so depth sorting with players and items is unchanged. Hidden pixels are filled in priority order:
  1. Its own pixels from the roofless pass.
  2. The per-graphic slice.
  3. Color-matched original art.
- **Cost:** about 9–12 KB per tile as BC7 at 2×, or roughly 200–270 MB for a Britain-sized town. So baking is for towns, dungeons and landmarks.
- **Per-graphic HD sprites** remain the fallback for wilderness, player houses, dropped items and anything placed at runtime. Tileable generation (self-tiling sheets with repeating noise) is the open research item.

### Art override system (client side)
- `ArtResolver` takes a map placement and returns, in order: its baked placement sprite if one exists (drawn at the tile anchor plus the stored offset), else the per-graphic HD pack entry, else the original decoded art, upscaled.
- HD packs are our own format: a header, an ID → atlas rect table and BC7-compressed atlas pages. They load directly to the GPU with no PNG decoding at runtime.
- Packs are layered: base HD pack → optional packs → user mods.

### Environment (land, statics)
1. **Export:** an editor tool exports originals at 2× along with ControlNet inputs (depth from tiledata height and z, lineart, alpha mask).
2. **Composite restyle:** the client renders a whole building or block from its original pieces into one image and records, per pixel, which piece that pixel came from (an ownership map). That composite goes through img2img (SDXL-family model + ControlNet depth/lineart + IP-Adapter style reference + the project style LoRA), so the result is coherent rather than patchwork.
3. **Slice back:** the ownership map cuts the result back into per-piece sprites. The original alpha is re-applied.
4. **Fix-up:** color-match within each art set. Land tiles get their diamond edges blended so they tile seamlessly in every combination.
5. **Review:** an in-client art browser for approving or regenerating each piece, then the approved pieces are packed.
- **Rules:** strict UO projection (no perspective). One consistent light direction (top-left, as in UO). Only ambient occlusion is baked in; day/night and point lights stay dynamic.
- **Throughput:** hundreds to low thousands of composites, and a few seconds per SDXL image on the 5080. Flux-class (fp8/GGUF) is reserved for hero assets and UI.
- The client drives ComfyUI through its local HTTP API (queue a workflow JSON, poll, then download the result).

### Characters & creatures (no Blender)
1. **Stopgap from day one:** the original animations, upscaled, plus a low-denoise (≈0.25–0.35) ComfyUI pass with a fixed seed, ControlNet and the style LoRA.
2. **Target:** **Unity as the sprite studio.**
   - Characters: a modular Asset Store fantasy character pack (shared humanoid rig, separate armor/clothing pieces) plus Mixamo animations.
   - An editor tool places an orthographic camera at UO's exact angle and captures all directions and frames, with the body and each equipment piece rendered as separate layers. The layers then get a light ComfyUI stylization pass.
3. **Creatures:** local image-to-3D models in ComfyUI. Humanoids are rigged with Mixamo's auto-rigger. Quadrupeds need a freelance rigger or keep the upscaled originals.
- The captured sprites map onto UO body/action/direction IDs (5 drawn directions, 3 mirrored).

### Style lock
- Train a project LoRA on 20–50 approved images (seeded from the mockup).
- Fix the seed, sampler and CFG per art category, and version the workflows in the repo (`Tools/ComfyWorkflows/*.json`).

---

## 8. Phased roadmap

The art pipeline and the client build run in parallel. The two proofs of concept come first, because they decide whether the look works.

| Phase | Deliverable | Done when |
|---|---|---|
| **0. Setup** | Unity 6 project, asmdefs, test project, ModernUO running locally, ComfyUI API reachable | `dotnet test` and the editor both build clean, and the editor can queue a ComfyUI job |
| **1. Assets** | MUL/UOP readers, art/tiledata/hues/map/statics decoders, editor art browser | Any art ID renders and matches the reference |
| **2. Map renderer** | Land, statics, stretched land, hues, draw order, camera pan/zoom, offline from map data | Britain renders correctly with the original art |
| **3. ★ Environment proof of concept** | Composite export, ownership map, ComfyUI restyle, slicing, HD override pack | One Britain block (around the bank) in the new style with no visible seams |
| **4. ★ Character proof of concept** | Unity sprite-capture tool, one modular character, walk/run in all directions, stylized | That character walks on the proof-of-concept block |
| **5. Login** | Login → server list → char list → enter world, packet logger | Standing in the world on ModernUO |
| **6. Living world** | Mobiles, items, animations, equipment layers, walking with prediction | Walk around town, NPCs moving |
| **7. Interaction + new UI** | Containers (grid), drag/drop, paperdoll (live portrait), status bars, tabbed journal, minimap | The mockup screen, working |
| **8. Gameplay** | Targeting, combat, spells, skills, server gumps, context menus, vendors | Fight, cast, buy from a vendor |
| **9. Atmosphere** | Light buffer, day/night, weather, water/foliage shaders, graphics tiers | Low-end tier holds 60 fps on the target floor machine |
| **10. Content production** | Art production for the full world, characters, creatures, sound/music | Daily-drivable on your shard |

Phase 5 (networking) has no rendering dependency and can start any time after phase 1.

---

## 9. Key risks

| Risk | Mitigation |
|---|---|
| AI art inconsistency or seams | Composite-then-slice, ControlNet from the originals, style LoRA, fixed workflows. Proven in phase 3 before committing. |
| Animation flicker | Low denoise and fixed seed on frames. Long term, the Unity-rendered 3D sprites. |
| VRAM on low-end machines with 2× art | BC7 packs, atlas page eviction, Classic tier fallback. |
| Asset licensing | Asset Store packs are fine for a game, but check the license covers rendered sprites. Never distribute EA-derived art. |
| Draw order bugs (the #1 source of visual glitches) | Port ClassicUO's sort rules exactly. Keep a debug overlay that shows sort keys. |
| Client-version packet drift | Keep the version-keyed length table. Pin one version and test against ServUO's config. |
| Animation file complexity | Start with `anim.mul` bodies only. Add UOP animation frames and conversions afterwards. |
| Performance at crowded spots | Batched mesh, atlas reuse, no per-sprite GameObjects. Profile with 500 mobiles on screen. |
| Movement desync / rubber-banding | Mirror the server's walk rules. Log every 0x21 deny along with the predicted vs. server position. |
| Legal | Ship no EA assets. Require the user's install path. Keep ClassicUO-derived code under its BSD-2 notice. |

---

## 10. Where to start

**Start with phases 0–2: read UO's files and render Britain with the original art.** Everything depends on it. The environment proof of concept needs the client to render a block and its ownership map, and the real client is built on the same renderer.

1. Create the Unity 6 project (URP 2D) with the four asmdefs and a `dotnet test` project.
2. `UopFile` / `MulFile` containers, then the `tiledata`, `hues`, `art`, `map` and `statics` decoders, with unit tests against your UO install.
3. An editor art browser window to check decoding by eye.
4. A minimal batched world renderer: land, statics and draw order for one region, offline (no server).
5. In the meantime, outside the code: stand up ModernUO locally, and in ComfyUI start collecting 20–50 style images from the mockup for the LoRA.
