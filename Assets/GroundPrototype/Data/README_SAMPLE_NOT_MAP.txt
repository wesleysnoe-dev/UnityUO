==========================================================================
 SAMPLE NOT LIVE MAP  -  GroundPrototype P1 (isolated terrain prototype)
==========================================================================

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
session, renderer, standalone build). The assembly UOClient.GroundPrototype
is Editor-platform only and references no UOClient.* assembly.

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

Play-mode numbers (NOT yet observed)
------------------------------------
On Play, GroundCompareBootstrap calls HeightParityCheck.Measure(grid,
terrain), which reads back the live TerrainData (GetHeight at corners,
GetInterpolatedHeight and Terrain.SampleHeight at the same interior points),
logs "[GroundPrototype] ..." to the Console and shows it in the overlay.
Public API:  GroundCompareBootstrap.LastParityReport / OfflineParityReport,
             HeightParityCheck.Measure / ComputeOffline.
Those readback numbers are authoritative; the offline model above is a
prediction until someone presses Play.

Opening it
----------
File > Open Scene > Assets/GroundPrototype/Scenes/GroundCompare.unity, then
Play. Keys/buttons: 1 = Terrain, 2 = Mesh, 3 = Side by side (default).

Assembly / Play Mode note (2026-11-06)
--------------------------------------
P1 originally set includePlatforms to Editor only. In this Unity 6 project that
made scene MonoBehaviours fail at Play with:
  "The class named '...GroundCompareBootstrap' is not derived from MonoBehaviour"
Game view stayed black; HeightParityCheck.Measure never ran.

Fix: includePlatforms is empty (any platform) so Editor Play can load the types.
Still: autoReferenced=false, no UOClient.* references, GroundCompare is NOT in
EditorBuildSettings / PlayerBuilder scenes — so the standalone Unity UO player
does not open this scene. Tradeoff: the assembly *could* be pulled into a player
build only if someone later adds GroundCompare (or refs this asmdef) to a build;
it does not auto-enter production scenes today.

