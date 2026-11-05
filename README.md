# Unity UO

**Unity UO** is an overhauled Ultima Online client in Unity 6. The code uses `UOClient` in its assembly and namespace names. It keeps UO's isometric look and adds updated art from a local ComfyUI pipeline. The target server is ModernUO. See [ARCHITECTURE.md](ARCHITECTURE.md) for the full plan.

The client reads the player's own UO install. **EA's data files and anything derived from them, including `Exports/`, are never committed or shipped.**

## Layout

| Path | What |
|---|---|
| `Assets/UOClient.IO/` | MUL/UOP containers, decoders for art, gumps, tiledata, hues, map and statics. Pure C#, `noEngineReferences`. |
| `Assets/UOClient.ArtPipeline/` | `IsoCompositor`: a CPU isometric render of a map region plus a per-pixel ownership map, used for the ComfyUI restyle-and-slice workflow. |
| `Assets/UOClient.Unity/Editor/` | **UOClient → Art Browser** (single art pieces) and **UOClient → World Preview** (a map region from original, baked or per-graphic art). |
| `DotNet/` | Builds the same `Assets/` sources outside Unity (netstandard2.1, C# 9), plus the `uoclient` CLI and xUnit tests. |

## Connecting to ModernUO

The client logs in to a ModernUO server as an unencrypted 7.0.23.1 client. That's the version this shard's own data folder verifies against.

**Server and client data: `UserSettings/unity-uo-settings.json`.** The game, editor windows, CLI and tests all read this file. The game writes it on its first run, filling in the folders it detects:

```json
{ "host": "127.0.0.1", "port": 2593,
  "uoDataPath": "C:\\Users\\…\\AppData\\Local\\UOSmallz\\UOData\\7.0.23.1\\7.0.23.1",
  "overridesPath": "C:\\Users\\…\\AppData\\Local\\UOSmallz\\Client", "hdArt": true }
```

- **`host` / `port`:** the shard, by default the local test server. Typing another server on the login screen saves it here.
- **`uoDataPath`:** the UO data files, read in place from the UO Smallz client install (the live client release), never copied into the project. When it's empty, the client uses that install's `UOData` if present, else the EA Classic install.
- **`overridesPath`:** the UO Smallz client folder. From it the client reads `ExternalImages` (custom and replacement art), `TileData/*.csv` (the shard items in 0x4C00–0x4CFF and 0xFE00–0xFE0D), `EquipConv/*.csv` (how shard clothing is worn), and the `HDArt` pack beside it. These follow the same rules as the client's TazUO patches 0005–0010. `none` turns overrides off.
- **`hdArt`:** draws 2x art from the HD pack where the renderer draws at 2x.

Launch arguments win over the file: `-server host:port`, `-uodata <dir>`, `-overrides <dir>|none`. The CLI takes the same with `--`. Switching to the live shard is only `-server uosmallz.duckdns.org:2593`. Read [LIVE_PARITY.md](LIVE_PARITY.md) first.

**Account:** the test account lives in `UserSettings/uoclient-account.json`, which git ignores, and holds no server:

```json
{ "account": "…", "password": "…", "character": "Adventurer", "startCity": "Britain", "clientVersion": "7.0.23.1" }
```

With `accountHandler.enableAutoAccountCreation` on, the account is created on first login, and the character is created in `startCity` if it doesn't exist.

**What the capture tour verifies live** (`Library/UOClient/capture-demo`, or `-uoclient-capture` on the player), each step reported in `demo_capture_done.txt` with screenshots:
- **Moving and windows:** walking across chunks, the paperdoll, backpack and help windows, and the paperdoll buttons.
- **Combat basics:** war mode, a skill's target cursor answered, and overhead speech.
- **Context menu and vendor:** a vendor's context menu, buying from it (pack and gold change) and selling the item back.
- **Item handling:** moving an item within the backpack, dropping it on the ground and picking it back up.
- **Shard gumps:** the Group Finder and a chat link's gump.
- **Macros:** Hiding, and bandage-self with its cursor answered.
- **Tooltips:** in the world and on the paperdoll (above it).
- **Chat, map and bar:** the chat window with links, the world map and the top bar.
- **Drawing and sound:** spell effects, a house, sound and music.

**Useful CLI checks:**
- `uoclient online --items` drops one item on the ground and back, with the packet log.
- `--say "<text>"`, repeatable, sends commands.
- `--buy` buys from a vendor in earshot.
- `--gumps` dumps every gump layout.
- `uoclient tile <x> <y>` lists what stands on a tile.
- `uoclient hue <n>` prints a hue's color ramp.
- `uoclient animdata <graphic>` prints an animation's frames.
- `uoclient overrides` lists what the client folder adds, and how a few shard items resolve.
- `uoclient bakecheck <baked dir> <other data dir>` checks that baked art is still of the same originals and on the same map.

To test headless, logging in, entering the world and walking a loop:

```bash
dotnet run --project DotNet/UOClient.Cli -- online --walk 8 --log Exports/net/packets.log
```

The packet log blanks password fields.

| Assembly | What |
|---|---|
| `Assets/UOClient.Net/` | TCP connection on a background reader thread, Huffman decompression, packet framing by the version-adjusted length table, packet reader and writer. |
| `Assets/UOClient.Core/` | `GameSession`: login → server list → relay → game login → character select or create → world. Handles world packets (mobiles, items, equipment, status, speech and cliloc, light) and sequence-numbered movement with server rejections. `World` is the client-side model with events. |

## In Unity

**UOClient → Play World Demo** opens `Assets/Scenes/WorldDemo.unity`, creating it on first use, and presses Play.

**Online** (the default when the account file exists and the server is up):
- Logs in and plays the character from the account file. Walking is server-confirmed: each step is predicted locally and snaps back if the server rejects it.
- Draws every mobile in view with its equipment, as layered UO animations sharing one dynamic atlas, with names colored by notoriety.
- Draws ground items such as doors, signs and lamp posts.
- The journal shows live speech, chat and system messages. Press Enter to talk.
- Lighting follows the server's light level. T overrides it.
- The map streams in 16×16-tile chunks as you walk. A background thread composes each chunk; the main thread spends at most about 4 ms per frame uploading finished ones, and chunks far behind you are unloaded with their textures. Depth comes from each sprite's own sort key (see `DepthKey`), so chunks built separately still sort correctly against each other and against characters.
- Character frames (per look, action and direction) and ground-item sprites (per graphic and hue) are built on a second background thread. The main thread copies finished pixels within about 2 ms per frame; until then a character keeps its last frames.
- Steps are paced on real time at the server's walk and run delays. Starting steps early makes ModernUO's movement throttle reject them, which shows up as rubber-banding.
- F3 shows frame-time stats and the breakdown of the last hitch (our own Update sections, garbage collections, and Unity's player loop, editor loop, render, GPU wait and IMGUI). Two 60-step runs through Britain in the editor measured an average of 6.4–6.6 ms, a 99th percentile of about 18 ms, a worst frame of 22–24 ms, and no frames over 33 ms.

**Windows (gumps)**, online:
- **P** opens your paperdoll, **B** your backpack, **F1** the server's help menu.
- Double-click a character in the world for their paperdoll (yourself too), or a ground item to use it.
- Windows are drawn from real UO gump art. Drag a window's background to move it, and right-click to close it. Closing a server gump this way answers it with button 0, unless the server marked it `noclose`.
- **Paperdoll:** background, body in your skin hue, and each worn item's paperdoll art. Double-click the backpack icon to open it, or other items to use them. Drag items off to take them off, and drop items onto it to put them on.
- **Containers:** items sit at the server's positions. Double-click to use, and drag between containers, onto the paperdoll, or out into the world (dropped at your feet).
- **Server gumps** (`0xB0`/`0xDD`): pages, `resizepic` backgrounds, pictures, tiled pictures, item art, text, cropped text, HTML and cliloc HTML (with arguments), buttons (page and reply), checkboxes, radios, and text entries. Replies send the switches and texts back (`0xB1`), and the server can close them (`0xBF`/`0x04`).
- Double-clicks are paced at the server's action delay (1 s on AOS shards), so quick successive uses all go through instead of "You must wait to perform another action".

**Playing**, online:
- **Starting the game:** launching normally opens the login screen, then the shard list (when there is more than one), then character select (play, delete, new) and character creation. `-uoclient-autologin` skips straight in with the account file, as the capture tours do.
- **Paperdoll buttons:** all eight work: Help, Options, Log Out, Journal, Skills, Chat, War/Peace and Status. **K** opens skills, **C** status, **Tab** toggles war mode, and **Esc** cancels a target cursor or closes the top window.
- **Combat:** a crosshair appears when the server asks for a target. Click an object or the ground; with no cursor up, double-click attacks in war mode. You also get damage numbers, health bars, overhead speech and single-click names.
- **Context menus:** Shift+click a character or an item, in the world or in a container. Vendors' Buy and Sell open shop windows.
- **Tooltips:** rest the mouse on an item for 0.4 s.
- **Creatures and mounts:** drawn from anim.mul through anim6.mul via `Bodyconv.def` and `Body.def`. Riders sit on their mounts and move at mounted speed.
- **Houses and boats:** drawn from multi.mul, and walkable. Upper floors hide while you're inside.
- **Spell effects** (missiles, explosions, auras, lightning), **sound effects** and **region music**. Volumes are in Options.
- **Spellbooks:** double-click a book to list its spells, then click one to cast.
- **Top bar:** UO Smallz client buttons — Map, Paperdoll, Inventory, Journal, Chat, Help, World Map, Skills, Status, Groups (`[groups`, the server's Group Finder) and Options.
- **Shard chat window** (Chat):
  - Tabs: All, Global, Trade, Faction, Guild, Whispers and News, with unread counts.
  - The message box sends on the tab's channel: `[g`, `[t`, `[f`, `[gu`, `[w Name`.
  - Double-click a line to whisper that player.
  - Offers carry Join / View / Fill buttons. The client says `[uoclient 1` at login so the server sends them, and strips the `{{link …}}` marker from the text.
- **World map:** the whole facet. Drag to pan, scroll to zoom, Center to follow you again. It is drawn once on a background thread and cached under `Library/UOClient`.
- **Dropping in the world:** goes where you release it: the tile under the mouse, a character (give / pack animal) or an item (chest, stack), as in the classic client.
- **Macros:** `UserSettings/unity-uo-hotkeys.json` (`cast Heal`, `skill Hiding`, `bandageself`, `lasttarget`, `say …`, …). The defaults are on F2 and F4–F9; edit the file, then press Reload in Options.

**Offline** (no account file, or the server isn't reachable): a look-and-feel demo with no server:
- Britain around the restyled house, drawn in real time from batched atlas meshes with depth-sorted cutout sprites. The baked ComfyUI art is used where it exists, and upscaled original art everywhere else.
- A walking robed character built from UO animation layers (body, robe, hair). It sorts correctly behind walls and furniture.
- Roofs hide when you step indoors, showing the painted interior from the roofless pass.
- Time of day (day, golden hour, dusk, night), with a light around the character and a vignette.
- A mockup-style HUD: status bars, a tabbed journal, a round minimap from `radarcol.mul`, and an overhead name.

Controls: WASD or the arrow keys walk (Shift runs), hold the right mouse button to walk toward the cursor, scroll to zoom. T changes the time of day, R toggles roofs and H hides the HUD.

Region, character name, baked folder and zoom are fields on the **UO World Demo** object in the scene.

### Standalone build

**UOClient → Build Windows Player (Unity UO)** builds `Builds/Windows/Unity UO.exe` from the world demo scene: windowed at 1920×1080 by default, resizable, and kept running in the background. It also writes `unity-uo-paths.json` next to the exe, pointing at this project, so the player uses the project's baked art and `UserSettings` account file without copying them into the build.

To run the measured route unattended (it writes the report to `Library/UOClient/demo_capture_done.txt`, then quits):

```bash
"Builds/Windows/Unity UO.exe" -uoclient-capture -screen-fullscreen 0 -screen-width 1920 -screen-height 1080
```

Measured on this machine with vSync on: an average of 6.1 ms (164 fps, the display's refresh rate), a 99th percentile of 7.4 ms, a worst frame of 15.5 ms, and no frames over 33 ms, over a 60-step run across several chunks with 42 characters in view. The editor's numbers on the same route are about 18 ms for the 99th percentile and 23 ms for the worst frame. The difference is editor overhead.

**UOClient → World Preview** renders a map region inside the editor with the art pipeline's compositor. It's a preview of the art, not the game renderer.

- **Art:**
  - *Original*: the UO install, at scale 1–4×.
  - *Baked*: a folder from `bake`. It defaults to `Exports/comfy_test/baked`.
  - *Sprites*: a folder from `slice`.
- **Roofs** hides roofs. **Coverage** tints baked or sliced art by pixel source.
- **Controls:** drag to pan, scroll to zoom around the cursor. **Fit** and **1:1** reset the view. Click a pixel to show the placement that drew it.
- **Defaults:** the region starts on the restyled test house (map 0, 1460,1664, 14×14).

To type-check the editor code while the project is open in Unity:

```bash
dotnet build DotNet/UOClient.EditorCheck
```

## Commands

The UO install comes from `UO_PATH` or `--uo <dir>`. The default is `C:\Program Files (x86)\Electronic Arts\Ultima Online Classic`.

```bash
dotnet test DotNet/UOClient.Tests
```

```bash
dotnet run --project DotNet/UOClient.Cli -- info
```

```bash
dotnet run --project DotNet/UOClient.Cli -- compose 0 1408 1664 64 64 Exports/britain_bank
```

`compose` writes:
- `composite.png`: the rendered region
- `ownership.png`: debug colors
- `ownership.bin`: an int32 item index per pixel, after an 8-byte width/height header
- `items.bin` / `items.json`: one entry per sprite, with graphic, tile, z and pixel rectangle

Other CLI commands: `land|static|gump <id> <out.png>` (the id can be hex, e.g. `0x0FAF`).

### Restyle-and-slice workflow

1. Render each region twice. The second pass leaves out roofs to expose interiors:

   ```bash
   dotnet run --project DotNet/UOClient.Cli -- compose 0 1408 1664 64 64 Exports/britain_bank
   ```

   ```bash
   dotnet run --project DotNet/UOClient.Cli -- compose 0 1408 1664 64 64 Exports/britain_bank_noroof --no-roofs
   ```

2. Run each `composite.png` through ComfyUI at 2×. With ComfyUI running on `127.0.0.1:8188`:

   ```bash
   E:/ComfyUI/python_embeded/python.exe Tools/ComfyRestyle/restyle.py Exports/britain_bank
   ```

   This writes `restyled.png` and `restyle_graph.json` into the folder. The pipeline:
   - 4x-UltraSharp upscale down to 2×, then SDXL img2img (DreamShaper XL, denoise 0.55)
   - ControlNet Union in tile mode, so edges stay on UO's tile grid
   - IP-Adapter style transfer from `Tools/ComfyRestyle/style/mockup_scene.png`

   Run `--help` for the tuning flags. `Tools/ComfyRestyle/seam_score.py` measures how well the sliced sprites reproduce the restyle in place.

   To test without ComfyUI, `upscale <in> 2 <out>` makes a stand-in.

3. Slice all passes together into one sprite folder:

   ```bash
   dotnet run --project DotNet/UOClient.Cli -- slice Exports/britain_bank_sprites Exports/britain_bank Exports/britain_bank_noroof
   ```

   Each graphic gets one HD sprite:
   - The base is its most-visible placement.
   - Gaps are filled from its other placements.
   - Pixels hidden everywhere use the original art, color-matched to the restyle.
   - The original transparency mask is always re-applied.

   `report.csv` lists coverage per sprite.

4. Check seams by re-rendering from the sprites. Add `--coverage` to paint pixels by source instead (green = best placement, yellow = other placements, red = fallback):

   ```bash
   dotnet run --project DotNet/UOClient.Cli -- reassemble 0 1408 1664 64 64 Exports/britain_bank_sprites Exports/britain_bank_2x.png
   ```

Sloped land is skipped by the slicer for now. Its HD form will be texmaps.

### Baking per placement (towns)

Per-graphic slicing can't reproduce AI-painted art without seams (see ARCHITECTURE.md §7). For fixed map content, bake instead. Every land tile and static keeps exactly the pixels ComfyUI painted for it, including sloped land:

```bash
dotnet run --project DotNet/UOClient.Cli -- bake Exports/britain_bank_baked Exports/britain_bank Exports/britain_bank_noroof
```

```bash
dotnet run --project DotNet/UOClient.Cli -- reassemble 0 1408 1664 64 64 Exports/britain_bank_baked Exports/britain_baked_2x.png
```

The output folder holds:
- `placements.bin`: placement key → atlas rect plus offset from the tile anchor
- `page_NNN.png`: the atlas pages
- `report.csv`: per-placement pixel sources
- `coverage/`: the pages tinted by source (green = painted in place, cyan = roofless pass, yellow = per-graphic slice, red = color-matched original)

`reassemble` accepts either a sprite folder or a baked folder.

## Status

- [x] UOP (including zlib and Mythic/BWT compression) and MUL readers
- [x] Art (land and statics), gumps, tiledata (64-bit flags), hues, map land and statics
- [x] CPU compositor with ownership map; Britain renders correctly
- [x] Unity editor Art Browser; all assemblies compile in Unity 6000.6.8f1
- [x] Stretched (sloped) land: texmap quads from the four corner heights, with face shading. UV orientation is checked against the land art in the tests.
- [x] Restyle-and-slice: composite save/load, 2× rendering with sprite overrides, slicer with multi-placement merge and color-matched fallback, roofless pass, coverage overlays. Identity round-trip is pixel-exact in tests.
- [x] ComfyUI restyle driver (`Tools/ComfyRestyle/restyle.py`) and seam scoring
- [x] Per-placement baking with atlas pages; ComfyUI block reproduced exactly (seam score 0.0 vs 16.1 sliced)
- [ ] Roofless pass as an inpaint of the main pass (consistent look across the roof line); chunked baking with overlapping margins for whole towns
- [ ] Tighter atlas packing (rects are ~60% empty) and BC7 compression
- [ ] Slice sloped land into HD texmaps; sprite-sheet pass for art that is always hidden
- [ ] Hue application, animations, multis, cliloc, fonts
- [x] Batched Unity world renderer: atlas meshes, depth-sorted cutout sprites, roof hiding (built-in pipeline; URP later)
- [x] Networking to ModernUO: login, character creation, world entry, mobiles, items, equipment, speech, light, server-confirmed movement. Tested headless and in Unity against the local shard.
- [ ] Mobiles in anim2–anim5 / UOP animations (bodyconv.def, mobtypes.txt), mounts
- [x] Streaming map chunks on a background thread; minimap built off the main thread
- [x] Character frames and item sprites built off the main thread; step pacing matched to the server
- [x] Standalone Windows player ("Unity UO") with an unattended performance run
- [ ] Free shared-atlas space on long journeys; hide overhead names behind roofs
- [ ] Multis (houses, boats)
- [x] Double-click, paperdoll, containers, drag and drop, server gumps; compressed `Cliloc.enu` from newer clients
- [ ] Targeting cursor, context menus, single-click names, status and skills gumps, vendor buy/sell, spellbook, UO bitmap fonts in gumps, paperdoll buttons
- [ ] Verify button replies, page switching and drag and drop by hand (the automated run opens the windows but doesn't click)
- [x] Custom houses: `0xD8` designs (zlib floor planes and stair lists) drawn in place of the foundation's multi.mul layout, cached per house by revision (`0xBF 0x1D`, asked for with `0xBF 0x1E`), and customize mode (`0xBF 0x20`). Covered by injected-packet tests; not yet seen from the server.
- [x] Server change (`0x76`): moves the player and resizes the map; the facet follows from `0xBF 0x08`. Injected-packet test only.
- [x] House and boat placement cursor (`0x99`): the multi is previewed under the mouse where the server will center it, and the click answers with `0x6C`. Injected-packet test only.
- [x] Gump text entry dialogs (`0xAB`, answered with `0xAC`). ModernUO doesn't use this packet; the layout follows RunUO/POL and ClassicUO. Injected-packet test only.

Credits: parts of the format handling are ported from [ClassicUO](https://github.com/ClassicUO/ClassicUO) (BSD-2-Clause): the Mythic decompressor, the Huffman decoding tree, the packet length table and the MUL animation layout. Packet layouts were checked against the ModernUO source.
