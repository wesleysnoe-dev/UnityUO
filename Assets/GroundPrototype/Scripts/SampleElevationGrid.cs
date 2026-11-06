using System;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Hand-authored SAMPLE elevation grid (NOT a live UO map). Loaded from
    /// Data/SampleElevation_32x32.json via JsonUtility.
    ///
    /// Conventions (also in Data/README_SAMPLE_NOT_MAP.txt):
    ///  - 32x32 tiles, 33x33 corner heights, row-major: heights[cy * cornersX + cx].
    ///  - Grid +x maps to world +X, grid +y maps to world +Z.
    ///  - 1 tile = tileSize world units (1.0). 1 Z unit = zScale world units (0.1).
    ///  - Corner (cx, cy) sits at world (cx * tileSize, z * zScale, cy * tileSize) relative to a side's origin.
    ///  - tileMaterial[ty * tilesX + tx] indexes materialNames (Grass, Dirt, Sand, Stone).
    ///  - fluids (P2, optional): bounded tile rects {type, tx, ty, w, h, surfaceZ} placed in existing
    ///    low basins. They are visual metadata only and never change corner heights.
    /// The pure-math members use System.Math only so they can be evaluated outside Unity.
    /// </summary>
    [Serializable]
    public sealed class SampleElevationGrid
    {
        public string note;
        public int cornersX;
        public int cornersY;
        public float tileSize = 1f;
        public float zScale = 0.1f;
        public int[] heights;
        public int[] tileMaterial;
        public string[] materialNames;
        public FluidRect[] fluids;

        /// <summary>Flat fluid surface over tiles [tx, tx + w) x [ty, ty + h) at Z = surfaceZ.</summary>
        [Serializable]
        public sealed class FluidRect
        {
            public const string Water = "water";
            public const string Lava = "lava";

            public string type;
            public int tx;
            public int ty;
            public int w;
            public int h;
            public int surfaceZ;

            public bool IsLava => string.Equals(type, Lava, StringComparison.OrdinalIgnoreCase);
            public bool IsWater => string.Equals(type, Water, StringComparison.OrdinalIgnoreCase);

            public override string ToString() => $"{type} tiles({tx},{ty}) {w}x{h} surfaceZ={surfaceZ}";
        }

        public int TilesX => cornersX - 1;
        public int TilesY => cornersY - 1;

        public static SampleElevationGrid FromJson(string json)
        {
            var grid = JsonUtility.FromJson<SampleElevationGrid>(json);
            grid.Validate();
            return grid;
        }

        public void Validate()
        {
            if (cornersX < 2 || cornersY < 2)
                throw new InvalidOperationException("SampleElevationGrid: need at least 2x2 corners.");
            if (heights == null || heights.Length != cornersX * cornersY)
                throw new InvalidOperationException($"SampleElevationGrid: heights length {heights?.Length} != {cornersX}x{cornersY}.");
            if (tileMaterial == null || tileMaterial.Length != TilesX * TilesY)
                throw new InvalidOperationException($"SampleElevationGrid: tileMaterial length {tileMaterial?.Length} != {TilesX}x{TilesY}.");
            if (tileSize <= 0f || zScale <= 0f)
                throw new InvalidOperationException("SampleElevationGrid: tileSize and zScale must be positive.");
            if (fluids == null) fluids = new FluidRect[0];
            foreach (var f in fluids)
            {
                if (f == null || !(f.IsWater || f.IsLava))
                    throw new InvalidOperationException($"SampleElevationGrid: fluid type must be water or lava ({f}).");
                if (f.w < 1 || f.h < 1 || f.tx < 0 || f.ty < 0 || f.tx + f.w > TilesX || f.ty + f.h > TilesY)
                    throw new InvalidOperationException($"SampleElevationGrid: fluid rect out of bounds ({f}).");
            }
        }

        /// <summary>Count of rect corners (inside + border) whose Z is below the fluid surface, i.e. actually submerged.</summary>
        public int SubmergedCorners(FluidRect f)
        {
            int n = 0;
            for (int cy = f.ty; cy <= f.ty + f.h; cy++)
            for (int cx = f.tx; cx <= f.tx + f.w; cx++)
                if (CornerZ(cx, cy) < f.surfaceZ) n++;
            return n;
        }

        public int CornerZ(int cx, int cy) => heights[cy * cornersX + cx];

        /// <summary>Corner height in world units (relative to the side origin).</summary>
        public double CornerWorldY(int cx, int cy) => CornerZ(cx, cy) * (double)zScale;

        public int TileMaterial(int tx, int ty) => tileMaterial[ty * TilesX + tx];

        public int MinZ()
        {
            int m = int.MaxValue;
            foreach (var h in heights) m = Math.Min(m, h);
            return m;
        }

        public int MaxZ()
        {
            int m = int.MinValue;
            foreach (var h in heights) m = Math.Max(m, h);
            return m;
        }

        /// <summary>
        /// Height of the custom mesh surface at fractional grid position (gx, gy), in world units.
        /// The mesh uses two planar triangles per quad split along the SW(0,0)-NE(1,1) diagonal,
        /// so this is the exact rendered mesh height (no smoothing).
        /// </summary>
        public double MeshPlanarHeight(double gx, double gy)
        {
            Locate(gx, gy, out int tx, out int ty, out double fx, out double fy);
            double h00 = CornerWorldY(tx, ty), h10 = CornerWorldY(tx + 1, ty);
            double h01 = CornerWorldY(tx, ty + 1), h11 = CornerWorldY(tx + 1, ty + 1);
            return fx >= fy
                ? h00 + fx * (h10 - h00) + fy * (h11 - h10)   // tri (0,0)(1,0)(1,1)
                : h00 + fy * (h01 - h00) + fx * (h11 - h01);  // tri (0,0)(0,1)(1,1)
        }

        /// <summary>Bilinear interpolation of an arbitrary corner sampler (used to model heightmap sampling).</summary>
        public double Bilinear(double gx, double gy, Func<int, int, double> corner)
        {
            Locate(gx, gy, out int tx, out int ty, out double fx, out double fy);
            double h00 = corner(tx, ty), h10 = corner(tx + 1, ty);
            double h01 = corner(tx, ty + 1), h11 = corner(tx + 1, ty + 1);
            double south = h00 + (h10 - h00) * fx;
            double north = h01 + (h11 - h01) * fx;
            return south + (north - south) * fy;
        }

        /// <summary>Planar triangles split on the opposite (SE-NW) diagonal, for a sensitivity bound.</summary>
        public double OppositeDiagonalHeight(double gx, double gy, Func<int, int, double> corner)
        {
            Locate(gx, gy, out int tx, out int ty, out double fx, out double fy);
            double h00 = corner(tx, ty), h10 = corner(tx + 1, ty);
            double h01 = corner(tx, ty + 1), h11 = corner(tx + 1, ty + 1);
            return fx + fy <= 1.0
                ? h00 + fx * (h10 - h00) + fy * (h01 - h00)                 // tri (0,0)(1,0)(0,1)
                : h11 + (1.0 - fx) * (h01 - h11) + (1.0 - fy) * (h10 - h11); // tri (1,1)(0,1)(1,0)
        }

        private void Locate(double gx, double gy, out int tx, out int ty, out double fx, out double fy)
        {
            gx = Math.Max(0.0, Math.Min(TilesX, gx));
            gy = Math.Max(0.0, Math.Min(TilesY, gy));
            tx = Math.Min((int)Math.Floor(gx), TilesX - 1);
            ty = Math.Min((int)Math.Floor(gy), TilesY - 1);
            fx = gx - tx;
            fy = gy - ty;
        }
    }
}
