# LiveGroundCompare (TEST only)

Lets you compare land backends in the real streamed world. The camera, player, mobiles, items, statics and
roofs stay the same; only the land changes.

| Backend    | What it is | Status |
|------------|------------|--------|
| `original` | The production path (IsoCompositor flat sprites + CPU-rasterized stretched texmaps) | default |
| `mesh`     | GPU triangle mesh with the compositor's exact corners, RIGHT–LEFT split (top-right-left + right-bottom-left), the same per-face shade and the same per-tile sort depth | experimental |
| `terrain`  | Real `UnityEngine.Terrain` | **not wired yet**. It parses, but the guard falls back to `original` |

## Opt-in

```
"Unity UO GroundCompare.exe" -server 127.0.0.1:2593 -liveground -groundbackend mesh
```

- `-liveground` is required. Without it nothing from this folder runs, even with `-groundbackend` (WorldDemo creates the
  controller by type name only under the flag).
- `-groundbackend original|mesh|terrain`. A missing or unrecognized value gives `original`.
- **Ctrl+Shift+G** switches between Original and Mesh in the world. The journal shows the active backend.
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

## Terrain caveats (why it is not wired)

`UnityEngine.Terrain` ignores rotation and scale and is Y-up. Showing it in this orthographic XY sprite world needs a
dedicated camera with an affine UO projection matrix. Its depth would also be continuous, while the sprite depth is
stepped per diagonal (`DepthKey`), so land/static sorting near diagonal boundaries would differ. On top of that, its
heightmap interpolation and LOD (pixel error) do not reproduce the RIGHT–LEFT split, and holes are per-sample only.
`RealMapPatch` already provides the exact bounded corner grid a Terrain heightmap would sample.

## Build

`UOClient/Build Windows Player (GroundCompare TEST)` writes to `Builds/Windows-GroundCompare/Unity UO GroundCompare.exe`
and never touches `Builds/Windows`. It can also be requested headless by touching `Library/UOClient/build-groundcompare`.
