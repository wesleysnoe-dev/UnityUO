==========================================================================
 SAMPLE NOT LIVE MAP  -  SAMPLE TEXTURES NOT MAP ART
 GroundPrototype P1 + P2 (isolated terrain prototype)
==========================================================================
 P2 notes are in the section "P2: blended textures, water, lava" below.

What this is
------------
A hand-authored 32x32-tile test patch used ONLY to compare two ways of
rendering ground in Unity (built-in render pipeline):

  LEFT  / mode 1 : Unity Terrain (TerrainData + TerrainLayers built in memory)
  RIGHT / mode 2 : Custom mesh (two planar triangles per tile)

Both are built from the SAME corner heights in SampleElevation_32x32.json,
lit by the SAME directional light + flat ambient, and coloured from the SAME
palette (Materials/Mat_Grass, Mat_Dirt, Mat_Sand, Mat_Stone).

It is NOT derived from any UO map file, makes NO claim of UO map fidelity,
and is NOT finished art. Nothing here is used by the client (WorldDemo,
session, renderer). The assembly UOClient.GroundPrototype references no
UOClient.* assembly (see the Assembly note at the end: it is no longer
Editor-platform only).

Conventions
-----------
  Tiles            : 32 x 32 tiles, 33 x 33 corner heights
  Storage          : heights[cy * 33 + cx], tileMaterial[ty * 32 + tx]
  Axes             : grid +x -> world +X, grid +y -> world +Z, height -> world +Y
  Tile size        : 1 tile = 1.0 world unit (tileSize)
  Height scale     : 1 Z unit = 0.1 world unit (zScale)  -> 10 Z = 1 tile of rise
  Z range          : -2 .. 32  (world Y -0.2 .. 3.2)
  Materials        : 0 Grass, 1 Dirt, 2 Sand, 3 Stone (one material per tile)
  Side origins     : Terrain at world (0,0,0); mesh at world (200,0,0)
  Camera           : orthographic, pitch 30, yaw 45, fixed (no free-fly);
                     both sides viewed with identical pose relative to origin.

These conventions are prototype-only and are not a statement about UO's
own tile/Z projection.

How the sample was authored
---------------------------
Corner Z values were written by a throwaway rule script (not in the repo);
the JSON is the authority. Rules, in priority order:
  - Flat:      x 0..10, y 0..12          Z = 0
  - Twisted:   x 11..19, y 0..10         Z = 0 / 6 checkerboard  (every quad
               is a saddle, so interior interpolation differs maximally)
  - Plateau:   x 21..32, y 0..14         Z = 30..32  (cliff between corner
               columns x=20 and x=21: ~30 Z = 3 world units in one tile)
  - Ramp:      x 0..14, y 16..32         Z = y - 16  (gentle, 1 Z per tile);
               the ramp's east edge drops to the jitter field (second cliff)
  - Elsewhere: deterministic jitter Z in -2..2
  - Hill:      added on top, centre (25,25), radius 7, peak +20 (cos^2)
Tile materials: Stone where corner span >= 10 Z or mean >= 25 (cliff faces,
plateau); Dirt on the ramp; Sand on the west flat strip and low hollows;
Grass elsewhere. Counts: Grass 526, Dirt 224, Sand 85, Stone 189.
594 of 1024 tiles are non-planar ("twisted") quads.

Why the two surfaces are NOT identical (read before judging visuals)
--------------------------------------------------------------------
Same corner heights does NOT mean same surface.
  - Mesh: each tile = 2 planar triangles split on the SW(0,0)-NE(1,1)
    diagonal. Height inside a tile is planar interpolation of 3 corners.
  - Unity Terrain: heights are stored as 16-bit normalised samples
    (quantisation at corners), queried with bilinear-style interpolation
    (TerrainData.GetInterpolatedHeight), and RENDERED by Unity's own
    patch/LOD triangulation, whose diagonal choice we do not control.
    heightmapPixelError is set to 1 to keep it near full detail.
  - On tile edges both are linear between 2 corners -> only quantisation.
  - At tile centres bilinear = mean of 4 corners, mesh = mean of SW/NE
    corners; difference = |(h00 + h11) - (h10 + h01)| / 4 (quad twist).
  - Terrain splat uses a 128x128 alphamap (4 texels per tile); Unity blends
    layers across tile borders, the mesh has hard per-tile material edges.
  - Mesh normals come from central differences of the corner grid; Terrain
    computes its own normals. Shading will differ slightly on slopes.

Height parity (OFFLINE model - computed, not observed in Play)
--------------------------------------------------------------
Produced by HeightParityCheck.ComputeOffline() - the exact repo source,
compiled outside Unity against UnityEngine DLLs and run on this JSON.
Model: terrain = bilinear of 16-bit-quantised corners (max sample 32766,
an assumption about Unity's storage); mesh = planar SW-NE triangles.
All values in world units.

  Corner samples           : 1089
  Interior samples         : 1024 tile centres + 2112 mid-edges
  corner max |err|         : 0.0000488   (16-bit quantisation only)
  interior max |err|       : 0.7250      (at grid (20.5, 14.5), cliff/plateau
                                          corner tile, 7.25 Z)
    tile-centre max        : 0.7250
    mid-edge max           : 0.0000488   (quantisation only, as expected)
  analytic twist max (unquantised) : 0.7250
  sensitivity: if Unity's sampler were planar triangles on the OPPOSITE
  (SE-NW) diagonal, interior max would be 1.4500.

Play-mode numbers (OBSERVED - authoritative)
--------------------------------------------
On Play, GroundCompareBootstrap calls HeightParityCheck.Measure(grid,
terrain), which reads back the live TerrainData (GetHeight at corners,
GetInterpolatedHeight and Terrain.SampleHeight at the same interior points),
logs "[GroundPrototype] ..." to the Console and shows it in the overlay.
Public API:  GroundCompareBootstrap.LastParityReport / OfflineParityReport,
             HeightParityCheck.Measure / ComputeOffline.

Observed in Editor Play (reported after the P1 assembly fix, 2026-11-06):
  GetInterpolatedHeight vs mesh planar tris, interior max ~0.000049 world u
  (same order as the 16-bit corner quantisation).
So the live TerrainData sampler agrees with the mesh's SW-NE planar
triangles on this sample; it does NOT behave like the bilinear offline
model. The offline 0.725 figure above is a model prediction that Play did
not confirm - it is kept only as a record of the (wrong) assumption and
must not be treated as observed truth. Heights were NOT changed to match
either number. The checks still run on every Play; trust the overlay /
Console readback over this file. Note that GetInterpolatedHeight agreeing
does not prove Unity's RENDERED terrain triangulation (LOD patches) matches
the mesh at every pixel.

Opening it
----------
File > Open Scene > Assets/GroundPrototype/Scenes/GroundCompare.unity, then
Play. Keys/buttons: 1 = Terrain, 2 = Mesh, 3 = Side by side (default).
P2 keys/buttons: T = textures on/off, F = fluids on/off,
                 L = lava point light on/off, N = night on/off.

Assembly / Play Mode note (2026-11-06)
--------------------------------------
P1 originally set includePlatforms to Editor only. In this Unity 6 project that
made scene MonoBehaviours fail at Play with:
  "The class named '...GroundCompareBootstrap' is not derived from MonoBehaviour"
Game view stayed black; HeightParityCheck.Measure never ran.

Fix: includePlatforms is empty (any platform) so Editor Play can load the types.
Still: autoReferenced=false, no UOClient.* references, GroundCompare is NOT in
EditorBuildSettings / PlayerBuilder scenes - so the standalone Unity UO player
does not open this scene and nothing in it starts automatically (no
RuntimeInitializeOnLoad; everything is driven from the scene bootstrap).
Correction: scene exclusion does NOT keep the DLL out of a player build. With
includePlatforms empty, UOClient.GroundPrototype.dll IS compiled into the
standalone build output (it was already present there before P2). It is inert
there because no shipped scene references its types, but it is not absent.
P2 leaves this as is (no asmdef / build changes).

==========================================================================
 P2: blended textures, water, lava  (SAMPLE TEXTURES NOT MAP ART)
==========================================================================
Everything below is created at runtime from GroundCompareBootstrap.Start
(AddComponent; the scene YAML is unchanged). No textures, materials,
prefabs, TerrainData or TerrainLayer assets are written to disk; all of
them are owned by GroundVisualFactory and destroyed in
GroundCompareBootstrap.OnDestroy. No QualitySettings, GraphicsSettings,
pipeline, post-processing or bloom changes. Built-in render pipeline,
gamma colour space (project setting, untouched).

Textures and blend (shared by both sides)
  - 4 procedural 128x128 stylized albedo textures (grass, dirt, sand,
    stone), tinted from the Mat_* palette colours, seamless, mipmapped.
    Placeholder look only - not map art, not final art.
  - One soft blend mask: box filter (half-width 0.35 tile) of the per-tile
    material map with 0.18-tile deterministic jitter, slightly sharpened.
    Stored once as 256x256 weights, corner-aligned (texel 0 on the map's
    near edge, texel 255 on the far edge = Unity Terrain's alphamap
    convention). Terrain gets it via SetAlphamaps; the mesh gets the same
    data as a Control texture and samples it with Unity's own formula
    splatUV = (tc * (res - 1) + 0.5) / res.
  - metallic 0 and smoothness 0.08 on both sides (TerrainLayer smoothness
    source forced to Constant; albedo alpha = 1).

UV convention (local map coordinates)
  uv = (worldXZ - sideOrigin.xz) / 4.0   (one texture repeat = 4 tiles)
  Terrain: TerrainLayer.tileSize = (4,4), offset 0, terrain origin = side
  origin (0,0,0). Mesh: MeshGroundBlend uses worldPos - _MapOrigin with
  _MapOrigin = (200,0,0) set per renderer. So both sides show the same
  texel at the same map position. Water/lava animation uses the same
  local coordinates, so patterns match between sides too.

Backends (NOT one universal shader)
  - Terrain: built-in Nature/Terrain/Standard (Unity's terrain adapter).
  - Mesh: Assets/GroundPrototype/Shaders/MeshGroundBlend.shader - Standard
    lighting surface shader, 4-layer blend, casts/receives shadows,
    forward-add point lights.
  Known blend/shading mismatches (expected, visible on close inspection):
  - normals: Terrain computes its own from the heightmap; mesh uses
    corner central differences -> slopes/cliffs shade differently;
  - Unity Terrain LOD/patch triangulation vs fixed SW-NE mesh diagonals
    (heightmapPixelError = 1 keeps this small);
  - terrain splat path also scales the final colour by the summed weight
    (~1 here) and may use instancing/basemap paths we disabled;
  - 8-bit weights on both, but Terrain's internal alphamap texture is
    Unity-managed - identical data, not a shared GPU texture.
  T toggles textures off: flat palette colours with the SAME soft mask
  (not the P1 hard per-tile look).

Fluids (bounded rect metadata, heights unchanged)
  JSON "fluids": {type, tx, ty, w, h, surfaceZ} in tiles / Z units.
    water  tx 14, ty 22, w 6, h 6, surfaceZ 1  (world Y 0.1)
           low valley between the ramp's east cliff and the hill
           (floor Z -2..2); ground above Z 1 pokes through as islands.
    lava   tx 19, ty 1,  w 2, h 9, surfaceZ 1  (world Y 0.1)
           trench at the foot of the plateau cliff (corner column x=20,
           Z -2..2), between the twisted 0/6 field and the plateau.
  The overlay prints, per rect, how many rect corners are actually below
  the surface. Surfaces are flat meshes (2 subdivisions per tile) at
  surfaceZ * zScale, built identically on both sides; corner heights and
  the P1 parity numbers are not affected. Where the ground under a rect's
  border is lower than the surface, the bounded rect edge is visible as a
  thin lip (water fades it over the outer 2% of the rect; lava does not).
  Lava uses a small polygon offset to win depth ties where a corner sits
  exactly at surfaceZ.

Water limits (StylizedWater.shader)
  Transparent, sun + flat ambient only (ForwardBase) - it ignores the lava
  point light. Animated analytic ripples (normals only, no vertex motion),
  colour/alpha by depth and a shore-foam band. Depth comes from
  _CameraDepthTexture, enabled per camera on the two prototype compare
  cameras only; orthographic depth is linearised as near + d * (far -
  near) (reversed-Z handled) and converted to vertical depth along the
  camera forward axis (exact for ortho; perspective is approximate). NO
  reflection, refraction, SSR, caustics or fluid simulation.

Lava (StylizedLava.shader) - emission vs real lighting
  Matte crust (smoothness 0) + scrolling domain-warped emission. Emission
  only makes the lava pixels bright; it lights NOTHING (no GI, no bloom).
  The orange light on nearby ground and on the 3 matte grey probe cubes
  west of the trench is one real Point light per side (range 6, intensity
  2.6, no shadows, ForcePixel per light). L toggles those lights; the
  emission stays on, which shows the difference.

Night (N)
  SharedLightRig dims the same directional sun (0.16, cool tint) and the
  flat ambient (0.07,0.08,0.12). Same rig for both sides; sun direction,
  camera, exposure (none/no post) and palette unchanged.

Play status for P2
  Shaders compile in the open Editor (cached DX12 variants for all three)
  and UOClient.GroundPrototype compiles with 0 errors/warnings. P2 visuals
  had NOT been looked at in Play when this was written - see the commit
  log / PLAY_HANDOFF steps for screenshots.

