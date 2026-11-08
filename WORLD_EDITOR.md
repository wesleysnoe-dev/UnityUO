# World editor (design)

An in-game, admin-only **world painter** for the Unity UO client: sculpt the land, paint the ground with soft
brushes, and place trees, rocks, plants and buildings, all in one tool. It is **not** a tile editor: the admin never
picks or replaces tiles. What is painted is the real world; the tile grid the server needs is worked out from it
automatically.

Decided with the shard owner on 2026-11-08. Editing happens on the shard **clone** (`G:\UOShardClone`, ModernUO on
`127.0.0.1:2595`); the live shard gets the result later as a patch, when the owner decides. Live streaming of edits
to TazUO players is out of scope until then.

## Why not edit the UO map directly

The UO map stores one height and one ground type per tile. Every attempt to make a natural world by deriving it
from that grid (blending, smoothing, water beds: the `-terrainstyle natural` pilot in `LiveGroundCompare`) keeps
showing the grid, because the detail is not in the data. So the editor authors **new, finer data**, and the grid
becomes an output.

## The three layers an admin paints

| Layer | What the admin does | What the server gets (derived) |
|---|---|---|
| **Shape** | Raise, lower, smooth, flatten, set height, carve water, at finer than tile detail | Each tile's Z (the height at the tile's top corner, as UO stores it); water where carved; too-steep ground stays impassable through the server's own step rules |
| **Ground** | Paint materials (grass, dirt, forest floor, sand, rock, snow, cobble, ...) with soft blended brushes | Each tile's land graphic: the dominant material's UO ground type, a variant chosen by position (mining, fishing and TazUO's view use it) |
| **Objects** | Place trees, rocks, plants, decorations and buildings from a palette; a scatter brush for forests; move, raise, delete | Placed objects in the world save (static items), on a tile, at any height. Everyone sees them, TazUO too, with no map patch |

Objects snap to a tile in position (the server needs that for walking and collision); their height is free, so they
sit on sloped ground naturally. Buildings use the game's own building art (walls, floors, roofs, multis).

## Data

**World layer** (shape + ground), per map, in regions of 64 x 64 tiles:

- Heights: `SamplesPerTile` = 4 per tile edge, 257 x 257 samples per region (shared edges), in 1/8 UO Z units (int16).
- Materials: per sample up to 4 (material id, weight) pairs, weights summing to 255. Material ids index a material
  table (name, client texture, UO land graphics for derivation, water flag).
- A region exists only where someone has painted. Elsewhere the world layer is generated on the fly from the UO
  map exactly as the natural pilot does now (smooth heights, blended materials), so unpainted areas still look
  natural and painting starts from what is already there.
- Each region has a version number, bumped on every change.

**Server storage** (clone): `Distribution/Data/WorldLayer/map{N}/r_{rx}_{ry}.bin` (compressed), never the original
map files. At startup the server loads every region and applies the derived tiles with `TileMatrix.SetLandBlock`
(swaps a live 8 x 8 land block; existing ModernUO API). Placed objects live in the normal world save.

**Undo:** per admin, a stack of edit steps (the affected rectangle's previous samples, or the object action).

## Messages (custom 0xBF extended subcommands; ids to be picked from the unused range)

- Client to server: **brush stroke** (tool, centre in fractional tile coordinates, radius, softness, strength,
  material or target height). The server applies the brush itself, deterministically, so every client agrees and
  nothing can be forged beyond the brush rules. Also: undo, redo, place / move / delete object, request region.
- Server to client: **region data** (whole region on request, or a changed sub-rectangle after an edit) with its
  version; **edit result** (ok / refused and why).
- Every edit is checked server side: account is Administrator or higher, the map and rectangle are valid, a rate
  limit. Refused edits change nothing.

## Client

- **Rendering:** the terrain backend (`LiveGroundCompare` Terrain + TerrainSplat + depth water) draws the world
  layer: from the server's regions where they exist, from the UO map elsewhere. The tile grid is no longer what is
  drawn.
- **Characters follow the drawn ground:** standing and walking, a mobile is drawn at the world layer's height under
  it; between tile centres it follows the surface instead of a straight line. Server positions are untouched.
- **Editor panel** (the owner's mockup): Raise, Create, Set Z, Soft (smooth), Paint, Look, Nav modes; brush shapes;
  brush size (tiles), edge softness, paint flow (per second), texture tiling; terrain / surface material palettes;
  save, undo, redo, reset. A brush circle on the ground under the mouse. F5 opens and closes it. Shown only to
  Administrator accounts (the server refuses edits from anyone else regardless).
- **Objects:** a palette of static art (searchable, with categories), click to place, scatter brush with density and
  variety, select / move / raise / delete.

## Derivation rules (world layer to tiles)

- Tile Z = the world layer height at the tile's top corner, rounded.
- Land graphic = the dominant material at the tile centre; among that material's UO land graphics, a variant picked
  by a position hash (so painted ground is not one repeated tile). Water material gives a water land graphic.
- UO edge / transition tiles are not used (the classic view of a painted area has hard edges); a later pass can add
  them for TazUO.

## After an edit

- The server's walking, line of sight and spawning use the new tiles immediately.
- Bot navigation data (waypoints, walk fields, the map editor's walk atlas) is marked stale for the changed area; a
  command rebuilds it (step 5).
- Houses and other multis: edits under them are refused (step 5).

## Build order

1. **World layer foundation:** region format and material table (Unity-free, tested); server storage, loading and
   tile derivation on the clone; the region and brush messages; the client draws regions from the server.
2. **Terrain brushes:** the editor panel with Raise / Lower / Smooth / Set Z / Paint, brush circle, undo, live updates.
3. **Characters follow the drawn ground.**
4. **Objects:** palette, place, scatter, move, delete; buildings from building art.
5. **Safety:** protect multis, bot navigation rebuild, backups of the world layer, edit log.
6. **Patch to live** when the owner decides: the world layer files and server code to the live shard, derived map
   files for TazUO.

Each step is usable on the clone on its own.

## Status

**Step 1 (foundation) done, 2026-11-08.**

- Shared core: `Assets/UOClient.Core/WorldLayer/` (regions, materials, store, init from the map, brushes, undo,
  derivation, message payloads); 12 tests in `DotNet/UOClient.Tests/WorldLayerTests.cs`.
- Server (clone only, not committed there; the clone's tree carries the live shard's uncommitted work):
  `G:/UOShardClone/ModernUO/Projects/UOContent/WorldEditor/` holds `WorldLayerSystem.cs` (load, derive into the live
  map, messages, undo, save every 5 s to `Distribution/Data/WorldLayer/map{N}/`), `WorldEditorTestAccount.cs` (a
  local test Administrator from `Distribution/Data/WorldLayer/test-admin.txt`; clone only) and `Shared/` (a copy of
  the core, refreshed by `E:/UOOverhaul/tools/sync_worldlayer.sh`).
- Client: `GameSession.WorldLayer.cs` (messages), `World/WorldLayerClient.cs` (under `-worldeditor`: hello, regions
  around the player, the server's tiles overlaid on the client's map via `MapFile.SetLandOverride`, affected chunks
  rebuilt via `WorldStreamer.RebuildArea`), and the terrain draws a painted region's own heights and materials
  (`TerrainLandChunk`, `TerrainLandBuilder.UseSampleHeights`). `-worldeditor` turns the natural terrain on.
- Checked end to end against the clone with the headless client (`uoclient online --worldlayer`, account file
  `G:/UOShardClone/local/worldeditor-account.json`): hello answered as an editor, three raises and a sand paint at
  the character's feet (+6 Z, sand dominant, 33 tiles edited, both regions the brush crossed bumped), the region
  fetched in parts, undo restored it, regions saved to their own files; the map files untouched.
- Not yet seen in the Unity client itself (it needs the editor panel of step 2 to paint, and a login to the clone).

**Step 2 (terrain brushes) done, 2026-11-08.**

- Panel `UI/WorldEditorWindow.cs` (F5, administrators only): Raise, Lower, Set Z, Soft, Paint, Look (picks Set Z's
  height from the ground); brush size, edge softness, flow, strength, Set Z target; the 8 ground materials as
  swatches; undo / redo (also Ctrl+Z / Ctrl+Y). `World/WorldEditorTool.cs`: the brush circle on the ground, holding
  the left mouse paints at the flow rate (WorldInput leaves the left mouse alone while the panel is open over the
  world; the right mouse still walks).
- Strokes apply at once on the client (the same shared code as the server, on a local copy made from the original
  map), redraw only the touched chunks a few times a second, overlay the derived tiles, and go to the server; when
  the drag ends the server's copy is taken (no redraw if identical).
- Fixed on the way: the natural terrain's weight textures were named `_Splat0..2`, which Unity's Terrain overwrites
  every frame, so the shader always saw "classic art" (the natural-terrain pilot was mostly showing tile art). They
  are `_LGWeights0..2` now; painted ground shows the blended materials.
- Checked in the real client against the clone with `-worldeditor-selftest` (opens the panel and paints a hill, a
  sand path, a rocky top and a smoothed rim near the character): smooth hill, soft-edged sand path over it, rock on
  top, panel on screen. Not yet painted by hand with the mouse.

**All UO ground (SURFACE palette), 2026-11-08.** The panel's TERRAIN tab holds the blended natural materials; SURFACE
lists every UO ground family from tiledata (search box, family list, the family's tiles with their art) and paints the
chosen tile as is on every tile the brush covers (crisp, as every client draws it). Regions gained a per-tile `Ground`
(format version 2; version 1 files still load); strokes carry the graphic (protocol version 2); the server checks it
is a real, drawn land tile; natural paint clears it; undo covers it. Fixed on the way: the chunk compositor cached
land blocks and never saw edited tiles (MapFile.OverrideVersion now drops the cache).
