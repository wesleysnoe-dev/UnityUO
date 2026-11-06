using System;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Numeric height comparison between the Unity Terrain side and the custom mesh side,
    /// both built from the SAME corner heights in SampleElevationGrid.
    ///
    /// Honesty notes (do not remove):
    ///  - Sharing corner heights does NOT make the two surfaces identical.
    ///  - The mesh is two planar triangles per quad, split SW(0,0)-NE(1,1). Its height inside a
    ///    tile is exact planar interpolation of three corners (SampleElevationGrid.MeshPlanarHeight).
    ///  - Unity Terrain stores heights as 16-bit normalized samples (quantization at corners) and
    ///    is queried via TerrainData.GetInterpolatedHeight / Terrain.SampleHeight. The rendered
    ///    terrain is triangulated by Unity's own patch/LOD scheme, which we do not control.
    ///  - Along tile edges both are linear between two corners, so mid-edge error is only quantization.
    ///    At tile centers bilinear = mean of 4 corners, the mesh = mean of the SW/NE diagonal corners;
    ///    the difference is |(h00 + h11) - (h10 + h01)| / 4 ("quad twist").
    ///
    /// ComputeOffline() is pure System.Math (no engine calls) and is what produced the numbers in
    /// Data/README_SAMPLE_NOT_MAP.txt. Measure() reads back the live TerrainData in Play mode.
    /// </summary>
    public static class HeightParityCheck
    {
        /// <summary>Assumed Unity 16-bit heightmap max sample (heights stored 0..32766). Model only; Measure() is authoritative.</summary>
        public const double HeightmapMaxSample = 32766.0;

        [Serializable]
        public struct ParityReport
        {
            public string source;
            public int cornerSamples;
            public int centerSamples;
            public int midEdgeSamples;
            /// <summary>Max |terrain - mesh| at grid corners (world units).</summary>
            public double cornerMaxAbs;
            /// <summary>Max |terrain - mesh| over tile centers and mid-edges (world units).</summary>
            public double interiorMaxAbs;
            public double centerMaxAbs;
            public double midEdgeMaxAbs;
            public double worstInteriorGx;
            public double worstInteriorGy;
            /// <summary>Offline only: interior max if the terrain sampler were triangles on the opposite (SE-NW) diagonal. NaN in Measure().</summary>
            public double interiorMaxAbsOppositeDiagonal;
            /// <summary>Measure() only: interior max using Terrain.SampleHeight instead of GetInterpolatedHeight. NaN offline.</summary>
            public double interiorMaxAbsSampleHeight;

            public override string ToString()
            {
                return $"[{source}] corners={cornerSamples} cornerMaxAbs={cornerMaxAbs:0.######} | " +
                       $"interior(centers={centerSamples}, midEdges={midEdgeSamples}) interiorMaxAbs={interiorMaxAbs:0.######} " +
                       $"(centerMax={centerMaxAbs:0.######}, midEdgeMax={midEdgeMaxAbs:0.######}) worst@grid({worstInteriorGx:0.##},{worstInteriorGy:0.##}) " +
                       $"oppDiag={interiorMaxAbsOppositeDiagonal:0.######} sampleHeight={interiorMaxAbsSampleHeight:0.######}";
            }
        }

        /// <summary>
        /// Offline model: terrain = bilinear interpolation of 16-bit-quantized corner heights
        /// (our model of TerrainData.GetInterpolatedHeight); mesh = planar triangles.
        /// </summary>
        public static ParityReport ComputeOffline(SampleElevationGrid grid)
        {
            int minZ = grid.MinZ(), maxZ = grid.MaxZ();
            double baseY = minZ * (double)grid.zScale;
            double rangeY = Math.Max(1, maxZ - minZ) * (double)grid.zScale;

            Func<int, int, double> quantized = (cx, cy) =>
            {
                double n = (grid.CornerWorldY(cx, cy) - baseY) / rangeY;
                n = Math.Max(0.0, Math.Min(1.0, n));
                return baseY + Math.Round(n * HeightmapMaxSample) / HeightmapMaxSample * rangeY;
            };

            var r = Sweep(grid, "offline-model bilinear(q16) vs mesh planar tris",
                (cx, cy) => quantized(cx, cy),
                (gx, gy) => grid.Bilinear(gx, gy, quantized));

            var opp = Sweep(grid, "opp",
                (cx, cy) => quantized(cx, cy),
                (gx, gy) => grid.OppositeDiagonalHeight(gx, gy, quantized));
            r.interiorMaxAbsOppositeDiagonal = opp.interiorMaxAbs;
            r.interiorMaxAbsSampleHeight = double.NaN;
            return r;
        }

        /// <summary>
        /// Play-mode readback from the in-memory TerrainData built by TerrainSideBuilder.
        /// Terrain heightmap resolution must equal the grid's corner count (33) so samples align with corners.
        /// </summary>
        public static ParityReport Measure(SampleElevationGrid grid, Terrain terrain)
        {
            var data = terrain.terrainData;
            Vector3 pos = terrain.transform.position;
            double tilesX = grid.TilesX, tilesY = grid.TilesY;

            var r = Sweep(grid, "play readback TerrainData.GetInterpolatedHeight vs mesh planar tris",
                (cx, cy) => pos.y + data.GetHeight(cx, cy),
                (gx, gy) => pos.y + data.GetInterpolatedHeight((float)(gx / tilesX), (float)(gy / tilesY)));

            var sh = Sweep(grid, "sh",
                (cx, cy) => pos.y + data.GetHeight(cx, cy),
                (gx, gy) =>
                {
                    var world = new Vector3(pos.x + (float)(gx * grid.tileSize), 0f, pos.z + (float)(gy * grid.tileSize));
                    return pos.y + terrain.SampleHeight(world);
                });
            r.interiorMaxAbsSampleHeight = sh.interiorMaxAbs;
            r.interiorMaxAbsOppositeDiagonal = double.NaN;
            return r;
        }

        private static ParityReport Sweep(SampleElevationGrid grid, string source,
            Func<int, int, double> terrainCorner, Func<double, double, double> terrainAt)
        {
            var r = new ParityReport { source = source };

            for (int cy = 0; cy < grid.cornersY; cy++)
            for (int cx = 0; cx < grid.cornersX; cx++)
            {
                double err = Math.Abs(terrainCorner(cx, cy) - grid.CornerWorldY(cx, cy));
                r.cornerMaxAbs = Math.Max(r.cornerMaxAbs, err);
                r.cornerSamples++;
            }

            void Interior(double gx, double gy, bool center)
            {
                double err = Math.Abs(terrainAt(gx, gy) - grid.MeshPlanarHeight(gx, gy));
                if (center) { r.centerSamples++; r.centerMaxAbs = Math.Max(r.centerMaxAbs, err); }
                else { r.midEdgeSamples++; r.midEdgeMaxAbs = Math.Max(r.midEdgeMaxAbs, err); }
                if (err > r.interiorMaxAbs)
                {
                    r.interiorMaxAbs = err;
                    r.worstInteriorGx = gx;
                    r.worstInteriorGy = gy;
                }
            }

            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
                Interior(tx + 0.5, ty + 0.5, true);

            // Mid-edges: horizontal edges (between corners along x) and vertical edges (along y).
            for (int cy = 0; cy < grid.cornersY; cy++)
            for (int tx = 0; tx < grid.TilesX; tx++)
                Interior(tx + 0.5, cy, false);
            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int cx = 0; cx < grid.cornersX; cx++)
                Interior(cx, ty + 0.5, false);

            return r;
        }
    }
}
