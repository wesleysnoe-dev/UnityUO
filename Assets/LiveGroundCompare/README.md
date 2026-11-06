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
  `_Time.y`. A screenshot or Player.log cannot prove motion; watch it live (or compare two frames).
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
- **Fallback:** if the lit shader is missing/unsupported, Legacy Diffuse + the old isolated light, and the indicator
  says `light ISOLATED: <why>` (Player.log: `UOClient LiveGround light: isolated (…)`). Otherwise
  `light shared (TOD+player), sun shadow CPU mask terrain+static columns (N tiles), sprites unshadowed` and
  `UOClient LiveGround light: shared (…)`.

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
- **Chunk edges:** a ray that leaves its 16×16 chunk counts as lit (neighbours are not visible to the builder), so a hill
  or building in the west/north neighbour does not shade this chunk (tested and documented).
- **Not shadowed:** sprites (statics, mobiles, items, roofs) do not receive the shadow, and mobiles/items do not cast;
  the water does not receive it. The indicator says `sun shadow CPU mask terrain+static columns (N tiles), sprites
  unshadowed`.

Why not Unity shadow maps (checked, not done): the terrain camera's view is the UO projection (sheared, in pixel units,
~10000 units deep), while the active quality level's `shadowDistance` is 150 (Ultra; Standalone default) — every terrain
pixel is beyond it, and changing it is a global QualitySettings change (out of scope). Sprites live in the main camera's
XY plane, not in terrain space, so they could not cast into the same shadow map without proxies.

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
- **Default path untouched:** `UOClient/WorldSprite` is not changed (no keyword, no global; source-guarded by a test);
  the main camera's clear flags are the presenter's as before. Original and Mesh never build a proxy.
- **Limits:** between samples Unity's terrain triangulation/LOD may differ from the proxy by about a pixel at a
  silhouette. Tiles not diverted (holes, baked HD land) get no proxy, as they are sprites themselves.
- **Indicator:** `occlusion depthkey N/M chunks` (proxies attached / terrain chunks) or `occlusion off: <why>`;
  Player.log: `UOClient LiveGround occlusion: on (…)` / `off (…)`.

## Still isolated (architecture, not done)

- **Sprites do not receive any shadow.** A sprite pixel only knows its screen position; darkening it by the terrain or
  building shadow needs each sprite's foot tile in its vertex data (WorldStreamer's quad format and the mobile/item
  renderers) plus a WorldSprite seam. That is outside this folder's allowlist.
- **No Unity shadow maps** (unchanged, see above): the ortho UO projection is ~10000 units deep against a 150
  `shadowDistance`, and billboards have no volume to cast.
- Original/Mesh land keep the compositor's per-face shade only (no sun shadow).

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
  two stills therefore show that a frozen frame is not motion; they do not show the live `_Time` path.
- **Limits:** one chunk. The camera is orthographic along `_EyeDir`, not the presenter's oblique matrix. The night and
  player-light values are illustrative. Tile colour is the mean classic art, not the baked chunk texture. The chunk has
  no static occluders, so the shadow still shows terrain self-shadow only. Sorting uses geometric depth on the fixture
  camera, not production `DepthKey` values. No FPS figures.

## Build

`UOClient/Build Windows Player (GroundCompare TEST)` writes to `Builds/Windows-GroundCompare/Unity UO GroundCompare.exe`
and never touches `Builds/Windows`. It can also be requested headless by touching `Library/UOClient/build-groundcompare`.
