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
  backend is really drawing.
- The same line goes to `Player.log` as `UOClient LiveGround (created|attach|toggle|terrain failed): …`. It is a warning
  when it is a fallback. The journal still gets the short `Ground: …` line.

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
  pixels as the original corners. It also adds a directional light for that layer only. While Terrain is active, the
  main camera skips layer 31 and clears depth only. On dispose it gets its old mask and clear flags back.
- Material: `Legacy Shaders/Diffuse` with the baked chunk texture. This is not a splat/alphamap terrain shader, because
  the player only always-includes that built-in shader (GraphicsSettings) and splat blending would smear per-tile art.

## Terrain caveats (honest; do not read corner matches as parity)

- **Sorting:** all terrain draws **behind every sprite**. Original sorts land per diagonal against statics and mobiles
  (`DepthKey`), so with Terrain a static or mobile that is behind a hill shows through the hill.
- **Surface between samples:** Unity's heightmap cell triangulation and LOD (`heightmapPixelError` 1, max LOD 0) decide
  the surface between samples. Only the samples are guaranteed. The RIGHT–LEFT diagonal is reproduced only where
  Unity's cell diagonal happens to agree.
- **Flat-drawn sloped tiles:** sloped tiles without a texmap are drawn flat at Z by Original and Mesh. A heightfield
  cannot step, so Terrain slopes them using the map's corners.
- **Lighting:** Lambert from the compositor's light direction, scaled so that flat ground ≈ 1 after the scene's ambient.
  It has no 0.7/1.25 clamp and uses smooth heightmap normals instead of per-face normals. It does not follow the
  time-of-day ambient or the player's light (sprites do).
- **Holes:** they are set in TerrainData and the collider. The Legacy Diffuse material does not cut them visually, so they
  show black. Baked (HD) land, the only land that is not diverted, draws over them as sprites.
- **Not yet seen in the player:** this was compiled in the Editor and the math is unit-tested. Nobody has looked at it
  in a TEST build yet. If the shader, camera or a chunk fails, the switch goes back to **Original** and shows
  `FALLBACK: Terrain failed: …` in red. It never fails silently.

## Build

`UOClient/Build Windows Player (GroundCompare TEST)` writes to `Builds/Windows-GroundCompare/Unity UO GroundCompare.exe`
and never touches `Builds/Windows`. It can also be requested headless by touching `Library/UOClient/build-groundcompare`.
