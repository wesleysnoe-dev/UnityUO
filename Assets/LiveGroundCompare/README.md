# LiveGroundCompare (TEST only)

Lets you compare land backends in the real streamed world. The camera, player, mobiles, items, statics and
roofs stay the same; only the land changes.

| Backend    | What it is | Status |
|------------|------------|--------|
| `original` | The production path (IsoCompositor flat sprites + CPU-rasterized stretched texmaps) | default |
| `mesh`     | GPU triangle mesh with the compositor's exact corners, RIGHT–LEFT split (top-right-left + right-bottom-left), the same per-face shade and the same per-tile sort depth | experimental |
| `terrain`  | Real `UnityEngine.Terrain` per 16x16 chunk (TerrainData heightmap + holes + TerrainCollider), drawn by its own camera under the sprites | experimental, caveats below |

## Why Mesh "looks the same" as Original (expected)

Mesh reuses the **same** land art (the same land sprites and texmaps from the data files and overrides), the same
corners, the same RIGHT–LEFT split, the same shade and the same sort depth. Only the rasterizer changes (GPU instead of
the compositor's CPU raster). Looking the same is what a working Mesh backend should do. It does **not** mean the client
fell back. To tell them apart, use the indicator below, or look for `Land (LiveGround mesh)` objects in the hierarchy.

LiveGroundCompare does **not** carry anything from P2 GroundPrototype: no stylized textures, water, lava, grass, dirt,
sand or stone, no MeshGroundBlend and no fluid rects. All three backends draw classic UO land art only.

## Reading the TEST indicator

While `-liveground` is set, a line is drawn at the top of the screen every frame. You do not need Ctrl+Shift+G to see it:

```
LiveGround TEST | requested Mesh | active Mesh | ok | host 127.0.0.1 | mesh chunks 24 | Ctrl+Shift+G cycles
```

- **requested**: what `-groundbackend` or the last toggle asked for. **active**: what the land is drawn with right now.
- **ok** (green): you got what you asked for. **FALLBACK: …** (red): active is not what you requested, and the text says
  why (a non-local host, a missing `-liveground`, or `Terrain failed: …; using Original`). **no world**: you are not in
  the world yet.
- **host**: the server the guard checked, which is the one you actually logged in to.
- **mesh/terrain chunks N**: how many land chunks that backend has built and attached right now. If this is above 0, the
  backend is really drawing. A swap makes the streamer rebuild every chunk over the next frames, so right after an
  attach or toggle it reads `chunks 0 (streaming)`; that is expected, not a failure.
- The same line goes to `Player.log` as `UOClient LiveGround (created|attach|toggle|chunks attached|terrain failed): …`.
  The `attach`/`toggle` line is written at the swap instant (so it shows 0); `chunks attached` follows once, when the
  first chunk of the new backend is really attached. It is a warning when it is a fallback. The journal still gets the
  short `Ground: …` line.

## Opt-in

```
"Unity UO GroundCompare.exe" -server 127.0.0.1:2593 -liveground -groundbackend mesh
```

- `-liveground` is required. Without it nothing from this folder runs, even with `-groundbackend` (WorldDemo creates the
  controller by type name only under the flag).
- `-groundbackend original|mesh|terrain`. A missing or unrecognized value gives `original`.
- **Ctrl+Shift+G** cycles Original → Mesh → Terrain → Original in the world. The indicator and the journal show the
  result.
- **Fail-closed:** experimental backends need a local host (`127.0.0.1`, `localhost`, `::1`; the same as
  `ClientSettings.IsLocal`), and any host containing `uosmallz` is refused. The check runs again every time a world is
  created, against the server you actually logged in to (a host typed on the login screen counts too).
- Nothing here logs in, teleports, creates accounts or sends packets.

## Geometry contract (tested in `DotNet/UOClient.Tests/LiveGroundCompareTests.cs`)

- TileHalf 22, ZStep 4, positions in UO pixels × art scale (`LiveGroundProjection.Corner`).
- Corners: top (x,y), right (x+1,y), bottom (x+1,y+1), left (x,y+1). Off-map corners clamp to the edge.
- Tiles the compositor does not stretch (flat, or sloped without a texmap) stay flat at Z and map their 44×44 diamond
  sprite. Stretched tiles map their texmap top(0,0) right(1,0) bottom(1,1) left(0,1).
- Every vertex of a tile has the depth that tile's sprite quad would have had (`DepthKey.Relative`, including ties), so
  sorting against statics and mobiles does not change.
- Baked (HD placement) land is never diverted.

## Known differences (mesh vs original)

- The GPU rasterizes the edges and the original uses the CPU rasterizer's ½-pixel crack tolerance, so edge pixels can
  differ by about one pixel.
- Texmap sampling is GPU point sampling from a per-chunk atlas with a 1px replicated gutter.
- The compositor's shade can go up to 1.25. Here it is a float vertex color multiplied in the WorldSprite shader, the same
  as the time-of-day lighting.

## Terrain backend: how it works

- `TerrainLandBuilder` (Unity-free, tested) builds the data for each streamed chunk from the land the streamer
  diverted. It uses a 33x33 heightmap (2 samples per tile edge) from the map's corner heights. Every sample lies on the
  compositor's RIGHT–LEFT surface (`LiveGroundProjection.HeightAt`). Tiles that were not diverted become holes. The
  builder also bakes one chunk texture: each tile's texmap, or its flat sprite diamond unrotated into the square the same
  way the mesh maps it.
- Terrain space: 1 unit per tile in X and Z, and `ZStep/(TileHalf·√2)` units per UO Z in Y, which is the compositor's
  shade ratio. Tile y runs along -Z.
- `TerrainLandChunk` makes a real `TerrainData` and `Terrain` (with its `TerrainCollider`) per chunk, on layer 31. When
  the chunk unloads, it destroys the TerrainData, texture, material and object.
- `TerrainIsoPresenter` adds a second camera that draws only layer 31, before the main camera. Its view matrix maps
  terrain space through the exact UO projection into the main camera's view, so the grid corners land on the same
  pixels as the original corners. Only in the fallback does it add a directional light for that layer. While Terrain is
  active, the main camera skips layer 31 and clears depth only. On dispose it gets its old mask and clear flags back.
- Material: `LiveGroundCompare/TerrainLit` (Resources, so it ships) with the baked chunk texture and the chunk's sun
  mask; fallback `Legacy Shaders/Diffuse` (always included by GraphicsSettings). Neither is a splat/alphamap terrain
  shader: splat blending would smear per-tile art.

## Terrain caveats (honest; do not read corner matches as parity)

- **Sorting:** see *Terrain ↔ sprite occlusion* below. The terrain's colour is still drawn first by its own camera;
  a depth-only proxy in the main camera gives every terrain tile its land sprite's `DepthKey`, so statics and mobiles
  behind a hill are hidden as with Original. If the proxy shader is missing, all terrain sorts behind every sprite (the
  old behaviour) and the indicator says `occlusion off: …`.
- **Surface between samples:** Unity's heightmap cell triangulation and LOD (`heightmapPixelError` 1, max LOD 0) decide
  the surface between samples. Only the samples are guaranteed. The RIGHT–LEFT diagonal is reproduced only where
  Unity's cell diagonal happens to agree.
- **Flat-drawn sloped tiles:** sloped tiles without a texmap are drawn flat at Z by Original and Mesh. A heightfield
  cannot step, so Terrain slopes them using the map's corners.
- **Brightness / lighting:** see *Terrain lighting and shadows* below. With the lit shader, flat ground in daylight is
  the same brightness as before (shade 1 × ambient 1); at dusk/night it now darkens with the sprites instead of staying
  bright. That follows the shared time of day; it is not a darkening "fix". Only the fallback (Legacy Diffuse) keeps the
  old isolated, brighter light.
- **Smooth elevation:** Unity interpolates the heightmap, so the hard per-tile edges of the diamond sprites are gone.
  This was reviewed and kept on purpose; do not restore hard tile edges.
- **Holes:** they are set in TerrainData and the collider. The Legacy Diffuse material does not cut them visually, so they
  show black. Baked (HD) land, the only land that is not diverted, draws over them as sprites.
- **Chunk seams under LOD:** neighbouring chunk terrains share identical edge samples (unit-tested), but they are not
  connected with `SetNeighbors` (`allowAutoConnect` off), so Unity does not stitch LOD across chunk borders. With
  `heightmapPixelError` 1 no cracks have been reported; if hairline gaps ever show at 16-tile borders, connect them.
- **Status:** seen working in the GroundCompare TEST build (toggle to Terrain logged `ok`). It is the preferred
  direction for further work, **not** the production default: Original stays the default and Original/Mesh stay as
  fallbacks. If the shader, camera or a chunk fails, the switch goes back to **Original** and shows
  `FALLBACK: Terrain failed: …` in red. It never fails silently.

## Terrain water (real map water tiles, animated)

Terrain backend only. Original keeps the classic water sprites; Mesh keeps the classic water art on its mesh (no water
surface there).

- **Which tiles:** a land graphic is water when its tiledata land row has `Wet` (0x80) **and** is named `water`
  (`WaterTiles`, tested). In the shipped 7.0.23.1 tiledata that is 0xA8–0xAB and 0x136–0x137. `Wet` alone is not used:
  it also marks the Ter Mur `acid` rows (0x2E0E–0x2E5F) and three `TerrainFallback` rows. The tiledata is the
  streamer's own `UOData` (overrides included), through the read-only `WorldStreamer.Data` accessor (typed; no
  reflection). `WaterTiles.TryLand` fails closed: no data, a tiledata that does not load or no land rows turn water off
  with a reason instead of an empty "no water" table. If that or the shader fails, the indicator shows
  `water off: <why>` and the terrain still draws.
- **Static water is not covered.** Shore/lake water placed as statics (0x1797… "water" statics) is not land, is never
  diverted and still draws as classic sprites on top of everything here.
- **Geometry:** `WaterSurfaceBuilder` (Unity-free, tested) builds one mesh per chunk over the water tiles only, in
  terrain space at the map's corner heights (the same corners the terrain heightmap uses), split RIGHT–LEFT. Heights,
  holes, the collider and movement are unchanged; the water has no collider.
- **Colour:** each vertex carries the mean colour of its tile's own classic art, so the surface takes the map's water
  colour. The classic water art in the terrain texture shows through the semi-transparent surface.
- **Shore:** per vertex, the distance in tiles (Chebyshev over corners, capped at 4) to the nearest corner touching
  non-water land in the same chunk. It drives alpha, a slight open-water darkening and a foam band. It is **not water
  depth** (UO has none; ocean land is one Z). Neighbouring chunks are not visible to the builder, so a shoreline that
  runs exactly along a 16-tile border gets no foam on that side.
- **Shader:** `Shaders/Resources/LiveGroundCompareWater.shader` (`LiveGroundCompare/Water`, built-in pipeline,
  ForwardBase, alpha-blended, `ZWrite Off`, `Offset -1,-1`). Loaded with `Resources.Load`, so it ships in every player
  built from this project (unused unless Terrain is active). One runtime material for all chunks.
- **Lighting:** the shared light (below): the compositor's sun shade on the wave normal × (time-of-day ambient + the
  player's light), ghost grey; the Blinn sun highlight is scaled by the ambient. No emission, no Unity light. The water
  does **not** sample the sun shadow mask.
- **Animation:** four analytic travelling waves perturb the normal, and the foam band drifts, both driven only by
  `_Time.y`. A screenshot or Player.log cannot prove motion. The isolated multi-frame check
  (`run-evidence-scratch.ps1 -WaterRealtime`, `stills/water_realtime.md`) renders the **shipped** shader on separate
  Editor frames, with a probe that writes the `_Time.y` each render really got. In two separate batch processes, `_Time.y`
  rose every frame and the pixels changed on every frame step (see *Evidence stills: dynamic cast* below).
- **Sorting:** the water is drawn by the terrain camera after the opaque terrain and shares its depth, so hills in front
  hide it. Against sprites its tiles are land like any other: the occlusion proxy carries their `DepthKey`.
- **Honesty counters:** the indicator adds `water tiles N in M chunks` while Terrain is active. Player.log gets
  `UOClient LiveGround water: on (water land rows with Wet: 6)` (or `off (…)`) when the presenter is made, and one line
  for the first chunk with water (tile count, vertices, graphics).
- Nothing here comes from P2 GroundPrototype (no StylizedWater/FluidSurfaceBuilder art or rects).

## Terrain lighting and shadows (shared light path)

How the sprites are lit (read-only, `UOClient.Unity.Lighting` + `UOClient/WorldSprite`): globals only. `_UOAmbient`
(time of day, season tint) + `_UOLightColor` × (1 − d/r)² around `_UOLightPos` (the player, in Unity world XY), then
`_UODead` drains to grey. There is no directional light and no shadow for sprites. Original land adds the compositor's
per-face shade (sun from the screen's upper left, clamp 0.7–1.25) as vertex colour.

What the Terrain backend now does (`LiveGroundLighting`, Unity-free and tested; `Shaders/LiveGroundLight.cginc`):

- **Shared:** the terrain material `LiveGroundCompare/TerrainLit` (`Shaders/Resources/LiveGroundCompareTerrain.shader`)
  and the water read the **same globals** as the sprites. Each terrain pixel finds its Unity x, y through
  `_LGTerrainToUnity` (the same rows as the presenter's view matrix, `TerrainToUnityRows`, tested against
  `ToWorldScreen`/`Corner`), so the player light falls on the terrain exactly where it falls on sprites. Time of day,
  season, `T` overrides and the ghost grey all follow.
- **Shade:** the compositor's formula and clamp (`Shade`, tested equal to `LiveGroundProjection.TriangleShade` for the
  same face), on Unity's smooth heightmap normals. Flat ground = 1, as Original.
- **No Unity light** in the shared path: the presenter's directional light is only created for the fallback.
- **The sun shadow dims only the ambient** (`LiveGroundLighting.Lit`, `LGLitShadowed`, the WorldSprite keyword block):
  colour = art × (ambient × shade(normal, sun visibility) + player light × shade(normal)). The player's light is local and
  is not darkened by a sun shadow; at night under the player's light a shadowed foot reads 0.93 instead of the day's
  0.70 (measured, `dynacast_capture.md`). In daylight without player light it is the same as before.
- **Fallback:** if the lit shader is missing/unsupported, Legacy Diffuse + the old isolated light, and the indicator
  says `light ISOLATED: <why>` (Player.log: `UOClient LiveGround light: isolated (…)`). Otherwise
  `light shared (TOD+player), sun shadow CPU mask terrain+static columns (N tiles), sprites receive CPU ceiling (M chunks, margins pending P); dynamic cast approx: C/K mobiles+items casting, D chunks redone, X ms`
  and `UOClient LiveGround light: shared (…)`.

Shadow model (**labelled approximation, not a dynamic shadow map**):

- **Terrain sun shadow:** per chunk, `ShadowMask` ray-casts from 4×4 points per tile toward the compositor's sun over the
  chunk's own heightfield (the same RIGHT–LEFT surface, `SampleZ`) and stores a 64×64 R8 visibility mask (bilinear,
  ¼-tile soft edge). The shader pulls the shade toward the 0.7 floor where the sun is blocked. It is computed from the
  map's heights every time the chunk is built (nothing painted), for the fixed sun direction this client has; it is not
  a per-frame shadow map and does not follow a moving light (there is none).
- **Static (building) columns:** `StaticOccluders` (tested, incl. a real Britain chunk) reads the chunk's own map statics
  (`WorldStreamer.Data` → map → static blocks, on the worker; map reads are locked) and makes every tile with a static
  of tiledata `Height` ≥ 3 that is not `Wet` a solid column up to its highest Z + Height. The same sun rays stop below a
  column top, so walls, buildings and trees throw a shadow onto the terrain away from the sun. It is a **projected
  silhouette from known heights on the CPU**, not a Unity shadow: no overhangs (a roof shades like a solid block), the
  column's own tile is in shade, only map statics (no server items or multis, so no player houses), and only the
  chunk's own statics. If reading them fails, the mask is terrain-only and the indicator says `statics off: …`.
- **Chunk edges:** the worker's mask and ceiling still see only their own chunk. With the sprite receive live, the
  **cross-chunk margin** (below) continues the rays into the **loaded** west/north/north-west neighbours on the main
  thread, and the terrain also reads that world ceiling (`min(mask, ceiling)`). Without a loaded neighbour those rays
  count as lit (fail-safe), as before.
- **Sprites receive it** (statics, roofs, mobiles, items; see *Sprite sun-shadow receive* below). **Mobiles and items
  cast** as CPU columns (see *Dynamic cast* below); effects and the hover outline do not receive; the water does not
  receive it.

Unity shadow maps: see *Route to real dynamic shadows* below (examined, not done).

## Terrain ↔ sprite occlusion (depth-only proxy)

- **Problem:** the terrain camera draws first and the main camera clears depth, so the terrain alone can never hide a
  sprite. A second camera's depth cannot be reused either: the terrain's depth is its own "nearness", not the sprites'
  `DepthKey`, and stacked built-in cameras do not reliably share depth.
- **What it does:** `TerrainDepthProxy` (Unity-free, tested) builds, per terrain chunk, a mesh over every diverted tile:
  a 3×3 grid on the same height samples as the terrain heightmap, each sub-quad split along RIGHT–LEFT (so it is the
  compositor's surface), and every vertex of a tile at that tile's production depth (`LandPlacement.Depth`, the land
  sprite quad's `DepthKey` — the Mesh backend's contract). It is a child of the streamer's chunk transform (same
  depth-origin offset as that chunk's sprites), drawn by the **main** camera with `LiveGroundCompare/DepthKey`
  (`Shaders/Resources/LiveGroundCompareDepthKey.shader`: `ColorMask 0`, `ZWrite On`, queue Geometry, before the
  AlphaTest sprites). Statics, mobiles and items then pass or fail the depth test exactly as against original land.
- **Default path untouched:** the proxy needs nothing from `UOClient/WorldSprite` (its default variant has no
  LiveGround global; source-guarded by a test); the main camera's clear flags are the presenter's as before. Original
  and Mesh never build a proxy.
- **Limits:** between samples Unity's terrain triangulation/LOD may differ from the proxy by about a pixel at a
  silhouette. Tiles not diverted (holes, baked HD land) get no proxy, as they are sprites themselves.
- **Indicator:** `occlusion depthkey N/M chunks` (proxies attached / terrain chunks) or `occlusion off: <why>`;
  Player.log: `UOClient LiveGround occlusion: on (…)` / `off (…)`.

## Sprite sun-shadow receive (Terrain + shared light only; labelled approximation)

A sprite pixel only knows its screen position. The receive puts each pixel back into the world and asks the same CPU
shadow the terrain uses whether that point is in it.

- **Shadow ceiling** (`LiveGroundLighting.ShadowCeiling`, Unity-free, tested): per ¼-tile texel, the UO Z below which a
  point standing there is in the sun shadow, `max over the sun ray of (occluder height − rise·distance)` over the
  chunk's heightfield and static columns. One number answers the visibility of *any* height in O(1); for ground points
  it gives exactly the terrain mask's answer (tested texel by texel, terrain-only and off column tiles). The column on
  the point's own tile is skipped, so a wall does not shade itself. Built on the worker next to the mask (~1.2 ms per
  town chunk vs ~0.4 ms for the mask, measured by `SpriteShadow_CeilingCostPerChunk_IsBoundedOnTheWorker`).
- **World texture** (`SpriteShadowCeiling`): one 1024² RHalf texture, Repeat + bilinear, tile (x, y) at texel
  (4x, 4y) mod 1024 (256 tiles wrap; the loaded window is ~144). Chunks write their 64² block on attach and clear it on
  dispose (owner-checked); unloaded ground reads `NoCeiling` (never shadowed). Uploaded at most once per frame, before
  the cameras (full 2 MB `Apply` only when a chunk changed). `wrap collisions N` in the indicator if the window ever
  grows past the wrap.
- **Foot metadata** (UV3, `MeshBuilder` foot overloads): Unity x, y of the foot anchor, its UO Z, a kind. Statics: the
  tile centre, upright when tiledata `Height > 1`, else ground (floors, rugs); land sprites that stay sprites (baked
  HD land): ground. Mobiles: the walking anchor and the Z between the two tiles. Items/corpses: their tile centre,
  upright. Effects and outlines: none.
- **Pixel → world** (`SpriteShadow.PixelPoint`, the shader mirrors it line for line; tested): upright = a vertical plane
  through the foot (4 px per Z above the foot), the pixels below the foot on the ground at the foot's Z (the tile's
  front half), kept ⅛ tile inside the foot tile; ground = on the ground at the foot's Z. Shade = 0.7 + 0.3 ×
  visibility (soft over ±1 Z of the ceiling), the terrain's own floor, multiplied before the time-of-day/player light.
  So a mobile beside a wall can have its feet in shadow and its head in the sun (fixture-verified on a real Britain
  chunk with a real body frame).
- **Opt-in and restore:** only `TerrainIsoPresenter` (Terrain backend with the shared light) creates the ceiling. It
  enables the global keyword `LG_SPRITE_SHADOW` (WorldSprite `multi_compile`), sets `_LGShadowCeil` /
  `_LGSpriteShadowMap` and `MeshBuilder.FootMetadata`; `Dispose` restores all of them. Without it nothing writes UV3
  and WorldSprite compiles its default variant, which is the 4aa51a6 shader text exactly (test strips the
  `#ifdef LG_SPRITE_SHADOW` blocks and compares to `Evidence~/WorldSprite.4aa51a6.shader.txt`) and renders 0 px
  different from it (evidence below). No QualitySettings, ProjectSettings or material changes.
- **Indicator:** `sprites receive CPU ceiling (N chunks, margins pending P); dynamic cast approx: C/K mobiles+items casting, D chunks redone, X ms`
  or `sprites unshadowed: <why>`; Player.log `UOClient LiveGround sprite shadow: on (…)` / `off (…)`.

Known limits (honest):

- Billboards: a sprite is a vertical plane through its foot tile centre. A long wall made of per-tile sprites gets a
  per-tile stair-step where its shadow edge crosses it; roof tiles can get a few dark pixels at their bottom edge where
  the neighbouring roof column toward the sun is higher (the column model has no overhangs).
- Casters: map statics, and (see *Dynamic cast* below) drawn mobiles and server items as columns. Chunk edges: only
  loaded neighbours cast across them. Fixed compositor sun.
- Sprites are not sorted against the shadow (there is nothing to sort): it is a multiplication of their light.

## Dynamic cast: mobiles and items (TEST, labelled dynamic moving approximation, not a shadow map)

The same CPU ceiling, with moving casters stamped in (`DynamicShadow`, `ShadowCeilingCore`: Unity-free and tested;
`SpriteShadowCeiling` uploads them).

- **What casts:** every mobile that is drawn (not a ghost) is a column `DynamicShadow.MobileHeight` = 16 Z high (TazUO
  `DEFAULT_CHARACTER_HEIGHT`, ModernUO `PersonHeight`) on the tile its feet are drawn in (`FootTile`, the receive's own
  foot tile). Every drawn server item is a column of its tiledata `Height` under the map-static rule
  (`StaticOccluders.Casts`: ≥ 3 and not `Wet`); smaller items are tracked and cast nothing. Multi parts drawn as
  `ItemView` cast by the same rule, from their real tiledata (nothing is invented for multis).
- **Exactness:** `Stamp` uses `CeilingAt`'s ray steps and own-tile skip. A stamped column gives exactly the ceiling
  `ShadowCeiling` would give with a static column there (bit-equal, tested on flat and uneven ground). A mobile never
  shades its own tile, so it cannot darken itself.
- **How they report:** `Avatar.Render` / `ItemView` call `MeshBuilder.ShadowCasters?.Mobile/Item(...)` inside their
  existing `FootMetadata` branch only (null by default, so the production path makes no call). Typed interface
  `IShadowCasterSink`, no reflection.
- **Invalidation (bounded):** a caster that keeps its tile costs one dictionary lookup per frame. A caster that moves,
  appears, or goes away dirties only the chunks its footprint touches (its tile and up to ~2.5 tiles away from the sun
  for a mobile; at most 2×2 chunks). A caster that was **not drawn** since the last frame is dropped (disposed,
  hidden, out of range, logout), so no shadow is left behind (tested; GPU still 0 px vs static). Items are stamped into
  a settled layer, and mobiles are stamped on top of it, so a step never restamps the items (max is order-free; tested
  equal).
- **Upload:** only the changed 64² chunk blocks go up (staging texture + `Graphics.CopyTexture`). More than 8 in one
  frame, or no CopyTexture support, sends one full 2 MB upload.
- **Cleanup:** detach, logout and backend swap dispose the presenter, which disposes the ceiling. That puts back the
  keyword, `_LGShadowCeil`, `_LGCeilOn`, `MeshBuilder.FootMetadata` and `MeshBuilder.ShadowCasters`, and drops every
  caster reference.
- **Terrain receives it too:** with `_LGCeilOn` (set only while the ceiling lives), `TerrainLit` takes
  `min(chunk mask, world ceiling at its tile point)`, so the dynamic casts and margins fall on the ground as well as on
  sprites.
- **Water receives it too:** `LiveGroundCompare/Water` reads `LGCeilVisibility` at its own surface point and lights with
  `LGLitShadowed`: the ambient (sun/sky) term goes to the 0.7 floor in shadow and the sun glint goes out, while the
  player's light stays unshadowed. Shore foam, open-water shade, `_Time` motion, blend/queue/offset are unchanged.
  With `_LGCeilOn` 0 (no presenter ceiling, light isolated, RHalf missing) or an empty ceiling, visibility is 1 and the
  water renders **0 px** different from the 83c0a85 shader (GPU, `stills/waterrecv.md`; CPU mirror in
  `LiveGroundWaterRecvTests`).

### Cross-chunk margins (loaded neighbours only)

- `DynamicShadow.Margin` continues each texel's sun ray after it leaves its chunk into the west, north and north-west
  neighbours (the sun is up-left, so rays only go −x/−y), using those chunks' corner heights and static columns as
  their terrain chunks have them **attached** (no map reads, nothing guessed). A ray stops (lit from there) where it
  enters a neighbour that is not loaded. Base = max(own ceiling, margin); it equals one world-wide ray over the loaded
  chunks texel for texel (tested).
- Attaching a chunk queues its own margin and its east/south/south-east neighbours' margins. Detaching queues those
  neighbours again, so a shadow goes away with the chunk that cast it (tested: no stale seam). At most
  `MarginsPerFrame` = 1 is computed per frame (0.6–0.8 ms each on the main thread, measured). Until then the edge reads
  lit, and the indicator counts `margins pending`.

### Measured (isolated, after warmup; `stills/dynacast.md`)

- CPU (DotNet, `LiveGroundDynCastFixtureTests`, a 3×3 block of real Britain chunks with 1,258 real static graphics as
  items (893 casting) and 40 walking mobiles, 600 frames after 120 warmup): reports + `Frame()` mean 0.23 ms, p95 0.39
  ms; 3.4 chunks redone per frame (max 8); **0 bytes allocated**. No FPS figure.
- Upload (Unity batch, RTX 5080, CPU submit): full 1024² RHalf 0.6–1.2 ms; 4 changed chunks by copy 0.03–0.08 ms; 0 B.

## Route to real dynamic shadows (examined, not done)

What was checked in code, so the next step is not "shadowDistance, impossible":

- `shadowDistance` is compared with the view depth of the camera that renders receivers. The terrain camera's depth
  row is the presenter's own choice (`TerrainIsoPresenter.OnPreCull`: `camPos.z + DepthCenter (10000) + k·nearness`).
  The main camera clears depth and the depth proxy carries occlusion, so that row could be re-centred to put the loaded
  terrain at view depth ~[1, 120) — inside the Ultra 150 — without touching QualitySettings. **shadowDistance alone is
  not the blocker.**
- The real work: (1) built-in directional cascades fit the camera frustum from its matrices; the terrain camera's view
  is sheared and in pixel units, so whether the fit is sane is unproven (needs an experiment); (2) `TerrainLit` has no
  ShadowCaster pass and does not sample the screen-space shadow mask (no AutoLight); (3) sprites are drawn by the main
  camera in its XY plane, so they can only receive by sampling an exported shadow map with their reconstructed world
  point (the reconstruction now exists: `SpriteShadow.PixelPoint`); (4) billboards cannot cast; casters would be proxy
  volumes in terrain space (boxes from `StaticOccluders`, capsules for mobiles).
- Options: **B (done, see *Dynamic cast*)** mobiles/items cast into the same CPU path as columns. **C (~1–2 days, medium)** compute the ceiling
  on the GPU each frame from a height texture (static columns + dynamic stamps), which also follows a moving sun if the
  time of day ever drives one. **A (~2–4 days, medium-high)** Unity shadow map on the terrain camera with re-centred
  depth, a TerrainLit shadow pass, box/capsule caster proxies, and sprites sampling the exported map
  (LightEvent.AfterShadowMap command buffer + own world→light matrix).

## Still not done

- **No Unity shadow maps** (see the route above).
- Mobiles/items cast only as tile-snapped columns (no silhouette, no self-shadow on their own tile; a moving mobile's
  shadow jumps a tile at a time). Effects do not receive; the water receives only at its surface point (no refraction or
  depth). The multi placement preview (`MultiPlacementGhost`
  draws `ItemView`s) casts like real items while it is shown.
- Original/Mesh land keep the compositor's per-face shade only (no sun shadow, no sprite receive).

## Evidence stills (Phase C, isolated fixture)

Rendered stills of the shipped shaders on one real chunk, as proof of motion, light and sort behaviour. It is a check
you run by hand, not a live session and not the streaming path. Everything is in `Evidence~/`, which Unity ignores.

- **Fixture:** `Evidence~/realWaterChunk.json` comes from `LiveGroundEvidenceFixtureTests` (DotNet, real UO data). It
  holds map 0 chunk (1456, 1456): 129 water tiles (0xA8–0xAB), 29 shoreline corners, and the
  `LiveGroundLighting.ShadowMask` terrain self-shadow. The water mesh is `WaterSurfaceBuilder`'s own output. The test
  fails if the builders drift from the saved file. Rewrite it with
  `$env:UOCLIENT_WRITE_LIVEGROUND_EVIDENCE='1'; dotnet test DotNet/UOClient.Tests --filter "FullyQualifiedName~LiveGroundEvidenceFixtureTests"`.
- **Capture:** `Editor/LiveGroundEvidenceCapture.cs` is an Editor-only asmdef with no InitializeOnLoad and no Play. It
  renders off-screen with `LiveGroundCompare/TerrainLit`, `/Water`, `/DepthKey` and `UOClient/WorldSprite`, then writes
  `Evidence~/stills/*.png` and `evidence.md` with the exact times, sha256s and pixel deltas. Two ways to run it:
  - Menu `UOClient/LiveGroundCompare/Capture evidence stills (TEST)`, with the project open and not in Play. It puts the
    shader globals back afterwards.
  - `Evidence~/run-evidence-scratch.ps1`, which works even while the Editor holds the project lock. It copies only the
    shaders and the capture into `%TEMP%\UOClient_LGEvidence` and runs `-batchmode -executeMethod
    UOClient.LiveGroundCompare.Editor.LiveGroundEvidenceCapture.CaptureBatch`, with the GPU and Gamma like this project.
- **Water time:** the shipped water shader reads `_Time.y` once. The capture compiles an in-memory copy where that read is
  replaced by `_LGEvidenceTime`, and renders t0 = 10.000 s and t1 = 10.500 s. Rendering t0 twice must differ by 0 px. Inside
  one `executeMethod` call Unity's own `_Time` does not advance (`realtime_a/b` are identical; Time.time stays 0). Those
  two stills therefore show that a frozen frame is not motion; they do not show the live `_Time` path. The separate-frame
  check below (`water_realtime.md`) does show it.
- **Limits:** one chunk. The camera is orthographic along `_EyeDir`, not the presenter's oblique matrix. The night and
  player-light values are illustrative. Tile colour is the mean classic art, not the baked chunk texture. The chunk has
  no static occluders, so the shadow still shows terrain self-shadow only. Sorting uses geometric depth on the fixture
  camera, not production `DepthKey` values. No FPS figures.

## Evidence stills: sprite shadow receive

- **Fixture:** `Evidence~/shadowRecvChunk.json` + `shadowrecv_ground.png` + `shadowrecv_atlas.png` from
  `LiveGroundSpriteShadowFixtureTests` (real data): the Britain chunk with the most static columns in a town box
  (1424, 1712; 235 columns), its statics exactly as `IsoCompositor.BuildDrawList` places them (348 quads, real art), the
  terrain bake, the sun mask and the shadow ceiling, and a real human body frame (anim body 400, stand) at three tiles
  picked by the CPU mirror: feet in shadow + head lit, wholly in shadow, wholly lit. The test fails if the builders
  drift; rewrite with `UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1` (filter `LiveGroundSpriteShadowFixtureTests`).
- **Capture:** menu `UOClient/LiveGroundCompare/Capture sprite shadow receive stills (TEST)` or
  `Evidence~/run-evidence-scratch.ps1 -ShadowRecv` (scratch project, works with the Editor open). One world pixel per
  output pixel, the shipped WorldSprite and TerrainLit. Writes `stills/shadowrecv_*.png` and `stills/shadowrecv.md`.
- **Measured (2026-11-06, D3D12, RTX 5080, Gamma):** keyword off vs the 4aa51a6 shader compiled in memory: **0 of
  1,048,576 px** different; keyword off with vs without UV3: 0 px; keyword on with an empty ceiling: 0 px. Keyword on:
  53,209 px darker (south/east wall faces, porch furniture under the eave, roof near the chimneys). All six body
  probes: GPU on/off ratio = CPU `SpriteShadow` shade within 0.008 (0.708/0.702 vs 0.700, 1.000 vs 1.000).
- **Limits:** one chunk; terrain sorting against sprites is not exercised (the Phase C sort stills cover it); the
  moving receiver is shown as three placements, not motion; no FPS figures.

## Evidence stills: dynamic cast, margins, water across frames

- **Fixture:** `Evidence~/dynCastChunk.json` from `LiveGroundDynCastFixtureTests` (real data; rewrite with
  `UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1`). It is the receive fixture's Britain chunk (1424, 1712), with ceilings made
  by the shipped `ShadowCeilingCore`: static only, a mobile column at A (1435, 1726, 20), moved to B (1435, 1727),
  swept, and the chunk with its 3 real west/north/north-west neighbours loaded.
- **Capture:** `run-evidence-scratch.ps1 -DynCast` (or the menu `Capture dynamic cast + margin stills (TEST)`). It
  writes `stills/dynacast_*.png`, `margin_*.png` and `dynacast_capture.md`. The ground is drawn in terrain space
  through the presenter's terrain→Unity rows, so `TerrainLit` reads the world ceiling as in the game. That path
  renders 0 px different from the receive capture's ground.
- **Measured (2026-11-06, D3D12, RTX 5080, Gamma; not logged in):** the receiver east of A has its foot at ratio
  **0.708** and its head at 1.000. With the caster at B the same foot is 1.000. After removal: **0 px** vs static (no
  stale shadow). Night + player light at the receiver: foot 0.931 (only the ambient is shadowed). Margin on vs off:
  1,649 px darker along the west/north seams. Keyword off with any ceiling bound: 0 px (production path ignores it).
  The receive capture re-run after this change: keyword off vs 4aa51a6 still **0 of 1,048,576 px**; day probes still
  match the CPU.
- **Water `_Time`:** `run-evidence-scratch.ps1 -WaterRealtime` renders the **shipped** water shader (no override) on 5
  separate Editor update ticks ≥ 0.5 s apart. A probe drawn by the same camera writes the `_Time.y` that render got
  into a float target. Two separate batch processes: `_Time.y` 0 → 0.316 → 0.326 → 0.659 → 1.267 s and 0 → 0.303 →
  0.410 → 0.743 → 1.077 s. The pixels changed on every frame step (41k–46k of 369k px), and same-frame repeats were 0
  px. Editor `_Time` advances with Unity's clamped delta (≤ 0.333 s per frame), not with wall time. These are Editor
  frames, not a live session's player loop.

## Evidence stills: water sun-ceiling receive

- **Fixture:** `Evidence~/waterRecvChunk.json` from `LiveGroundWaterRecvFixtureTests` (real data; rewrite with
  `UOCLIENT_WRITE_LIVEGROUND_EVIDENCE=1`): the coastal chunk (1456, 1456) of `realWaterChunk.json`, its own CPU ceiling
  (terrain only: no casting statics there, 0 water texels shadowed) and the same with a 16 Z column in open water at
  (1469, 1457, -5) (30 water texels shadowed on the CPU).
- **Capture:** `run-evidence-scratch.ps1 -WaterRecv` (menu `Capture water sun-ceiling receive stills (TEST)`) writes
  `stills/waterrecv_*.png` and `waterrecv.md`; `-WaterRealtime -WaterRecv` repeats the separate-frame `_Time` run with
  that ceiling live (`waterrecv_realtime*`).
- **Measured (2026-11-06, D3D12, RTX 5080, Gamma; not logged in):** 83c0a85 water vs shipped with the ceiling off, or
  on but empty, day and night + player light, time-patched and untimed: **0 of 368,640 px** each. Column on: 972 px
  darker, 687 of them on the water alone. Probe in its shadow: day 0.766, night 0.796, night + player light 0.900
  (only the ambient share is dimmed). t0/t1 with the receive on: 47,960 px moved; same t0 twice 0 px. Shipped water,
  Unity `_Time` on 5 separate frames with the ceiling live: 0 → 1.188 s, pixels changed on every step.

## Build

`UOClient/Build Windows Player (GroundCompare TEST)` writes to `Builds/Windows-GroundCompare/Unity UO GroundCompare.exe`
and never touches `Builds/Windows`. It can also be requested headless by touching `Library/UOClient/build-groundcompare`.
