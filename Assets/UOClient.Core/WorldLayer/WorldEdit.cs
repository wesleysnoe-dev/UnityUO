using System;
using System.Collections.Generic;

namespace UOClient.WorldLayer
{
    // Shared with the server (ModernUO) as source: only System, C# 9, no Unity, no client types.

    /// <summary>
    /// The world layer of one map in memory: regions by (rx, ry), made on first use from the UO map
    /// (<see cref="WorldInit"/>), read and written by world sample so brushes ignore region borders. A write to an
    /// edge sample goes to every region holding a copy of it.
    /// </summary>
    public sealed class WorldStore
    {
        private readonly Dictionary<(int, int), WorldRegion> _regions = new Dictionary<(int, int), WorldRegion>();
        private readonly Func<int, int, WorldRegion> _create;

        /// <param name="map">The map index.</param>
        /// <param name="create">Makes a missing region (rx, ry), normally <see cref="WorldInit.FromMap"/>; null result: off the map.</param>
        public WorldStore(int map, Func<int, int, WorldRegion> create)
        {
            Map = map;
            _create = create;
        }

        public int Map { get; }

        public IEnumerable<WorldRegion> Regions => _regions.Values;

        public WorldRegion Find(int rx, int ry) => _regions.TryGetValue((rx, ry), out WorldRegion r) ? r : null;

        public WorldRegion Get(int rx, int ry)
        {
            if (_regions.TryGetValue((rx, ry), out WorldRegion r))
                return r;
            r = _create(rx, ry);
            if (r != null)
                _regions[(rx, ry)] = r;
            return r;
        }

        /// <summary>Puts a loaded region in (replacing any in memory).</summary>
        public void Put(WorldRegion region) => _regions[(region.Rx, region.Ry)] = region;

        private readonly Dictionary<(int, int), WorldRegion> _pristine = new Dictionary<(int, int), WorldRegion>();

        /// <summary>
        /// A sample as the original map gives it (the region's factory, never the painted copy): the eraser's target.
        /// False off the map.
        /// </summary>
        public bool GetPristine(int sx, int sy, out short height, float[] weights)
        {
            (int rx, int ry) = WorldRegion.RegionOfSample(sx, sy);
            if (!_pristine.TryGetValue((rx, ry), out WorldRegion r))
                _pristine[(rx, ry)] = r = _create(rx, ry);
            if (r == null)
            {
                height = 0;
                return false;
            }
            int i = WorldRegion.Index(sx - r.SampleX0, sy - r.SampleY0);
            height = r.Height[i];
            r.GetWeights(i, weights);
            return true;
        }

        private WorldRegion Owner(int sx, int sy, out int index)
        {
            (int rx, int ry) = WorldRegion.RegionOfSample(sx, sy);
            WorldRegion r = Get(rx, ry);
            index = r == null ? -1 : WorldRegion.Index(sx - r.SampleX0, sy - r.SampleY0);
            return r;
        }

        /// <summary>Every region holding a copy of a sample, with the sample's index there (1, 2 or 4 of them).</summary>
        private IEnumerable<(WorldRegion Region, int Index)> Copies(int sx, int sy)
        {
            (int rx, int ry) = WorldRegion.RegionOfSample(sx, sy);
            int lx = sx - rx * WorldRegion.SamplesPerRegion, ly = sy - ry * WorldRegion.SamplesPerRegion;
            for (int dx = 0; dx <= (lx == 0 ? 1 : 0); dx++)
                for (int dy = 0; dy <= (ly == 0 ? 1 : 0); dy++)
                {
                    WorldRegion r = Get(rx - dx, ry - dy);
                    if (r != null)
                        yield return (r, WorldRegion.Index(lx + dx * WorldRegion.SamplesPerRegion, ly + dy * WorldRegion.SamplesPerRegion));
                }
        }

        public short GetHeight(int sx, int sy)
        {
            WorldRegion r = Owner(sx, sy, out int i);
            return r == null ? (short)0 : r.Height[i];
        }

        public void SetHeight(int sx, int sy, short h)
        {
            foreach ((WorldRegion r, int i) in Copies(sx, sy))
                r.Height[i] = h;
        }

        public void GetWeights(int sx, int sy, float[] weights)
        {
            WorldRegion r = Owner(sx, sy, out int i);
            if (r == null)
            {
                Array.Clear(weights, 0, weights.Length);
                weights[WorldMaterials.Classic] = 1f;
                return;
            }
            r.GetWeights(i, weights);
        }

        public void SetWeights(int sx, int sy, float[] weights)
        {
            foreach ((WorldRegion r, int i) in Copies(sx, sy))
                r.SetWeights(i, weights);
        }

        /// <summary>Raw slots of a sample (for undo snapshots).</summary>
        public void GetSlots(int sx, int sy, byte[] ids, byte[] w)
        {
            WorldRegion r = Owner(sx, sy, out int i);
            for (int s = 0; s < WorldRegion.Slots; s++)
            {
                ids[s] = r == null ? (byte)0 : r.MatId[i * WorldRegion.Slots + s];
                w[s] = r == null ? (byte)0 : r.MatW[i * WorldRegion.Slots + s];
            }
        }

        public void SetSlots(int sx, int sy, byte[] ids, byte[] w)
        {
            foreach ((WorldRegion r, int i) in Copies(sx, sy))
                for (int s = 0; s < WorldRegion.Slots; s++)
                {
                    r.MatId[i * WorldRegion.Slots + s] = ids[s];
                    r.MatW[i * WorldRegion.Slots + s] = w[s];
                }
        }

        /// <summary>A tile's painted UO ground (0: none, the blend decides).</summary>
        public ushort GetGround(int x, int y)
        {
            (int rx, int ry) = WorldRegion.RegionOfTile(x, y);
            WorldRegion r = Get(rx, ry);
            return r == null ? (ushort)0 : r.Ground[(y - r.TileY0) * WorldRegion.Tiles + (x - r.TileX0)];
        }

        public void SetGround(int x, int y, ushort graphic)
        {
            (int rx, int ry) = WorldRegion.RegionOfTile(x, y);
            WorldRegion r = Get(rx, ry);
            if (r != null)
                r.Ground[(y - r.TileY0) * WorldRegion.Tiles + (x - r.TileX0)] = graphic;
        }

        /// <summary>Marks a tile edited (its server tile is derived from the layer from now on).</summary>
        public void MarkEdited(int x, int y)
        {
            (int rx, int ry) = WorldRegion.RegionOfTile(x, y);
            WorldRegion r = Get(rx, ry);
            if (r != null)
                r.Edited[(y - r.TileY0) * WorldRegion.Tiles + (x - r.TileX0)] = true;
        }
    }

    /// <summary>Makes a region from the UO map: smooth heights through the map's corners and blended materials.</summary>
    public static class WorldInit
    {
        /// <param name="cornerZ">The map's Z at tile (x, y) (UO: the tile's top corner).</param>
        /// <param name="tileMaterial">The material of tile (x, y) (<see cref="WorldMaterials.Classify"/> of its tiledata name).</param>
        public static WorldRegion FromMap(int map, int rx, int ry, Func<int, int, int> cornerZ, Func<int, int, byte> tileMaterial)
        {
            var region = new WorldRegion(map, rx, ry);
            int sx0 = region.SampleX0, sy0 = region.SampleY0;
            const int s = WorldRegion.SamplesPerTile;
            Func<int, int, float> corner = (x, y) => cornerZ(x, y);
            var weights = new float[WorldMaterials.Count];
            for (int ly = 0; ly < WorldRegion.N; ly++)
                for (int lx = 0; lx < WorldRegion.N; lx++)
                {
                    float wx = (sx0 + lx) / (float)s, wy = (sy0 + ly) / (float)s;
                    int i = WorldRegion.Index(lx, ly);
                    region.Height[i] = (short)Math.Round(SmoothZ(corner, wx, wy) * WorldRegion.ZScale);
                    Blend(tileMaterial, wx, wy, weights);
                    region.SetWeights(i, weights);
                }
            return region;
        }

        /// <summary>
        /// Height at world tile coordinates on a Catmull-Rom surface through the corners (exact at every corner),
        /// clamped to the cell's lowest and highest corner so cliffs never overshoot.
        /// </summary>
        public static float SmoothZ(Func<int, int, float> cornerZ, float wx, float wy)
        {
            int i = (int)Math.Floor(wx), j = (int)Math.Floor(wy);
            float u = wx - i, v = wy - j;
            Span<float> rows = stackalloc float[4];
            for (int r = 0; r < 4; r++)
            {
                int y = j - 1 + r;
                rows[r] = CatmullRom(cornerZ(i - 1, y), cornerZ(i, y), cornerZ(i + 1, y), cornerZ(i + 2, y), u);
            }
            float z = CatmullRom(rows[0], rows[1], rows[2], rows[3], v);
            float a = cornerZ(i, j), b = cornerZ(i + 1, j), c = cornerZ(i, j + 1), e = cornerZ(i + 1, j + 1);
            float lo = Math.Min(Math.Min(a, b), Math.Min(c, e)), hi = Math.Max(Math.Max(a, b), Math.Max(c, e));
            return Math.Max(lo, Math.Min(hi, z));
        }

        private static float CatmullRom(float p0, float p1, float p2, float p3, float t) =>
            0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);

        /// <summary>
        /// Material weights at a world point (tile units): the point wanders by smooth noise, then each tile within reach
        /// adds its material by distance, as far as the softer of the two materials allows. Natural ground blends over
        /// most of a tile; roads and classic ground keep a firm edge.
        /// </summary>
        public static void Blend(Func<int, int, byte> tileMaterial, float wx, float wy, float[] w)
        {
            Array.Clear(w, 0, w.Length);
            int ownMat = tileMaterial((int)Math.Floor(wx), (int)Math.Floor(wy));
            float ownSoft = WorldMaterials.Softness(ownMat);
            float amp = Math.Min(1f, ownSoft / 0.9f);
            float px = wx + amp * (0.42f * Signed(Noise(wx * 0.85f, wy * 0.85f, 11)) + 0.18f * Signed(Noise(wx * 2.3f, wy * 2.3f, 23)));
            float py = wy + amp * (0.42f * Signed(Noise(wx * 0.85f, wy * 0.85f, 37)) + 0.18f * Signed(Noise(wx * 2.3f, wy * 2.3f, 41)));
            int cx = (int)Math.Floor(px), cy = (int)Math.Floor(py);
            for (int tx = cx - 2; tx <= cx + 2; tx++)
                for (int ty = cy - 2; ty <= cy + 2; ty++)
                {
                    int k = tileMaterial(tx, ty);
                    float soft = Math.Min(WorldMaterials.Softness(k), ownSoft);
                    float dx = Math.Max(0f, Math.Max(tx - px, px - (tx + 1))), dy = Math.Max(0f, Math.Max(ty - py, py - (ty + 1)));
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (d >= soft)
                        continue;
                    float f = 1f - d / soft;
                    w[k] += f * f * (3f - 2f * f);
                }
            float sum = 0f;
            foreach (float v in w)
                sum += v;
            if (sum <= 0f)
                w[ownMat] = 1f;
        }

        private static float Signed(float n) => n * 2f - 1f;

        /// <summary>Smooth value noise in [0, 1], deterministic per world position and seed (the same on client and server).</summary>
        public static float Noise(float x, float y, int seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
            float fx = x - ix, fy = y - iy;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy);
            float a = Hash(ix, iy, seed), b = Hash(ix + 1, iy, seed), c = Hash(ix, iy + 1, seed), d = Hash(ix + 1, iy + 1, seed);
            return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
        }

        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }
    }

    public enum BrushTool : byte
    {
        Raise = 0,
        Lower = 1,
        Smooth = 2,
        Flatten = 3, // toward TargetZ
        Paint = 4,
        Erase = 5    // back toward the original map (heights, materials, painted UO ground)
    }

    /// <summary>The brush's footprint.</summary>
    public enum BrushShape : byte
    {
        Circle = 0, // distance from the centre
        Square = 1, // square in tile space (a diamond on screen)
        Tile = 2    // whole tiles, hard edged: the diameter in tiles, snapped to the tile grid
    }

    /// <summary>One brush application, as the client sends it and the server applies it.</summary>
    public struct BrushStroke
    {
        public BrushTool Tool;

        /// <summary>Centre in world tile units (fractional).</summary>
        public float X, Y;

        /// <summary>Radius in tiles.</summary>
        public float Radius;

        /// <summary>0: a hard edge; 1: the falloff spans the whole radius.</summary>
        public float Softness;

        /// <summary>0..1 per application (the client spreads its flow over time as several strokes).</summary>
        public float Strength;

        public byte Material;

        /// <summary>
        /// Paint with <see cref="WorldMaterials.Classic"/>: a UO land graphic laid on every tile whose centre the brush
        /// covers (the SURFACE palette). 0 with any other material: painting natural ground clears it there.
        /// </summary>
        public ushort Ground;

        /// <summary>Flatten's target, in UO Z.</summary>
        public float TargetZ;

        public BrushShape Shape;

        public const float MaxRadius = 24f;

        /// <summary>
        /// The Tile shape's footprint: N x N tiles from (X0, Y0), N the brush's diameter in tiles (at least 1), centred on the
        /// tile under the brush (odd N) or the nearest tile corner (even N).
        /// </summary>
        public (int X0, int Y0, int N) TileFootprint()
        {
            int n = Math.Max(1, (int)Math.Round(Radius * 2f));
            return ((int)Math.Floor(X - n / 2f + 0.5f), (int)Math.Floor(Y - n / 2f + 0.5f), n);
        }

        /// <summary>The stroke with every field clamped to what the server accepts.</summary>
        public BrushStroke Clamped() => new BrushStroke
        {
            Tool = Tool > BrushTool.Erase ? BrushTool.Raise : Tool,
            Shape = Shape > BrushShape.Tile ? BrushShape.Circle : Shape,
            X = X,
            Y = Y,
            Radius = Math.Max(0.25f, Math.Min(MaxRadius, Radius)),
            Softness = Math.Max(0f, Math.Min(1f, Softness)),
            Strength = Math.Max(0f, Math.Min(1f, Strength)),
            Material = Material < WorldMaterials.Count ? Material : WorldMaterials.Classic,
            Ground = Material < WorldMaterials.Count && Material == WorldMaterials.Classic ? Ground : (ushort)0,
            TargetZ = Math.Max(-127f, Math.Min(127f, TargetZ))
        };
    }

    /// <summary>What one stroke changed, enough to put it back exactly (undo) or re-apply it (redo).</summary>
    public sealed class EditStep
    {
        public readonly List<(int Sx, int Sy, short Height, byte[] Ids, byte[] Weights)> Before = new List<(int, int, short, byte[], byte[])>();
        public readonly List<(int Sx, int Sy, short Height, byte[] Ids, byte[] Weights)> After = new List<(int, int, short, byte[], byte[])>();
        public readonly HashSet<(int, int)> Tiles = new HashSet<(int, int)>();

        /// <summary>Painted UO ground per tile, before and after.</summary>
        public readonly List<(int X, int Y, ushort Before, ushort After)> Ground = new List<(int, int, ushort, ushort)>();
    }

    /// <summary>The brushes (deterministic: the same stroke on the same layer gives the same result everywhere).</summary>
    public static class WorldBrush
    {
        /// <summary>Raise / Lower: UO Z added at the centre at full strength per application.</summary>
        public const float RaisePerStroke = 2f;

        /// <summary>The brush's weight at a distance (tiles) from its centre.</summary>
        public static float Falloff(float distance, float radius, float softness)
        {
            if (distance >= radius)
                return 0f;
            float inner = radius * (1f - softness);
            if (distance <= inner)
                return 1f;
            float t = 1f - (distance - inner) / Math.Max(1e-4f, radius - inner);
            return t * t * (3f - 2f * t);
        }

        /// <summary>The brush's weight (0..1, before strength) at a point in world tile units, by its shape.</summary>
        public static float Weight(in BrushStroke b, float x, float y)
        {
            float dx = x - b.X, dy = y - b.Y;
            switch (b.Shape)
            {
                case BrushShape.Square:
                    return Falloff(Math.Max(Math.Abs(dx), Math.Abs(dy)), b.Radius, b.Softness);
                case BrushShape.Tile:
                {
                    (int x0, int y0, int n) = b.TileFootprint();
                    const float e = 1e-4f; // the footprint's edge samples (its tiles' corners) are in
                    return x >= x0 - e && y >= y0 - e && x <= x0 + n + e && y <= y0 + n + e ? 1f : 0f;
                }
                default:
                    return Falloff((float)Math.Sqrt(dx * dx + dy * dy), b.Radius, b.Softness);
            }
        }

        /// <summary>The tiles a stroke's ground painting covers: whole tiles for Tile, else the centres at half weight or more.</summary>
        private static bool CoversTile(in BrushStroke b, int tx, int ty)
        {
            if (b.Shape == BrushShape.Tile)
            {
                (int x0, int y0, int n) = b.TileFootprint();
                return tx >= x0 && ty >= y0 && tx < x0 + n && ty < y0 + n;
            }
            return Weight(b, tx + 0.5f, ty + 0.5f) >= 0.5f;
        }

        /// <summary>Applies a stroke; returns what changed (null if nothing did).</summary>
        public static EditStep Apply(WorldStore store, BrushStroke stroke)
        {
            BrushStroke b = stroke.Clamped();
            const int s = WorldRegion.SamplesPerTile;
            float bx0 = b.X - b.Radius, bx1 = b.X + b.Radius, by0 = b.Y - b.Radius, by1 = b.Y + b.Radius;
            if (b.Shape == BrushShape.Tile)
            {
                (int tx0, int ty0, int n) = b.TileFootprint();
                (bx0, bx1, by0, by1) = (tx0, tx0 + n, ty0, ty0 + n);
            }
            int sx0 = (int)Math.Floor(bx0 * s), sx1 = (int)Math.Ceiling(bx1 * s);
            int sy0 = (int)Math.Floor(by0 * s), sy1 = (int)Math.Ceiling(by1 * s);
            int w = sx1 - sx0 + 1, h = sy1 - sy0 + 1;

            // Read everything first, so smoothing reads the layer as it was before this stroke.
            var height = new short[w, h];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                    height[i, j] = store.GetHeight(sx0 + i, sy0 + j);

            var step = new EditStep();
            var weights = new float[WorldMaterials.Count];
            var pristine = new float[WorldMaterials.Count];
            for (int i = 0; i < w; i++)
                for (int j = 0; j < h; j++)
                {
                    int sx = sx0 + i, sy = sy0 + j;
                    float f = Weight(b, sx / (float)s, sy / (float)s) * b.Strength;
                    if (f <= 0f)
                        continue;
                    short before = height[i, j];
                    var ids = new byte[WorldRegion.Slots];
                    var ws = new byte[WorldRegion.Slots];
                    store.GetSlots(sx, sy, ids, ws);
                    short after = before;
                    switch (b.Tool)
                    {
                        case BrushTool.Raise:
                            after = ClampHeight(before + f * RaisePerStroke * WorldRegion.ZScale);
                            break;
                        case BrushTool.Lower:
                            after = ClampHeight(before - f * RaisePerStroke * WorldRegion.ZScale);
                            break;
                        case BrushTool.Smooth:
                        {
                            float sum = 0f;
                            int n = 0;
                            for (int a = -1; a <= 1; a++)
                                for (int c = -1; c <= 1; c++)
                                {
                                    int ii = i + a, jj = j + c;
                                    sum += ii >= 0 && jj >= 0 && ii < w && jj < h ? height[ii, jj] : store.GetHeight(sx + a, sy + c);
                                    n++;
                                }
                            after = ClampHeight(before + f * (sum / n - before));
                            break;
                        }
                        case BrushTool.Flatten:
                            after = ClampHeight(before + f * (b.TargetZ * WorldRegion.ZScale - before));
                            break;
                    }
                    bool painted = false;
                    if (b.Tool == BrushTool.Paint)
                    {
                        store.GetWeights(sx, sy, weights);
                        for (int m = 0; m < weights.Length; m++)
                            weights[m] *= 1f - f;
                        weights[b.Material] += f;
                        painted = true;
                    }
                    else if (b.Tool == BrushTool.Erase && store.GetPristine(sx, sy, out short original, pristine))
                    {
                        // Back toward the original map: the height and the materials, as far as the brush reaches.
                        after = ClampHeight(before + f * (original - before));
                        store.GetWeights(sx, sy, weights);
                        bool differs = false;
                        for (int m = 0; m < weights.Length; m++)
                        {
                            float back = weights[m] + f * (pristine[m] - weights[m]);
                            differs |= Math.Abs(back - weights[m]) > 0.5f / 255f;
                            weights[m] = back;
                        }
                        painted = differs;
                    }
                    if (after == before && !painted)
                        continue;
                    step.Before.Add((sx, sy, before, ids, ws));
                    if (after != before)
                        store.SetHeight(sx, sy, after);
                    if (painted)
                        store.SetWeights(sx, sy, weights);
                    var ids2 = new byte[WorldRegion.Slots];
                    var ws2 = new byte[WorldRegion.Slots];
                    store.GetSlots(sx, sy, ids2, ws2);
                    step.After.Add((sx, sy, after, ids2, ws2));
                    // The tiles this sample belongs to (a corner sample touches up to four).
                    int tx = WorldRegion.FloorDiv(sx, s), ty = WorldRegion.FloorDiv(sy, s);
                    step.Tiles.Add((tx, ty));
                    if (sx % s == 0)
                        step.Tiles.Add((tx - 1, ty));
                    if (sy % s == 0)
                        step.Tiles.Add((tx, ty - 1));
                    if (sx % s == 0 && sy % s == 0)
                        step.Tiles.Add((tx - 1, ty - 1));
                }
            // Painting lays (or, with a natural material, clears) the UO ground of the tiles whose centre the brush covers
            // at least half strength (whole tiles for the Tile shape); erasing clears it (the original tile shows again).
            if ((b.Tool == BrushTool.Paint || b.Tool == BrushTool.Erase && b.Strength >= 0.5f) && b.Strength > 0f)
            {
                ushort ground = b.Tool == BrushTool.Paint && b.Material == WorldMaterials.Classic ? b.Ground : (ushort)0;
                for (int ty = (int)Math.Floor(by0); ty <= (int)Math.Ceiling(by1); ty++)
                    for (int tx = (int)Math.Floor(bx0); tx <= (int)Math.Ceiling(bx1); tx++)
                    {
                        if (!CoversTile(b, tx, ty))
                            continue;
                        ushort before = store.GetGround(tx, ty);
                        if (before == ground)
                            continue;
                        store.SetGround(tx, ty, ground);
                        step.Ground.Add((tx, ty, before, ground));
                        step.Tiles.Add((tx, ty));
                    }
            }
            if (step.Before.Count == 0 && step.Ground.Count == 0)
                return null;
            foreach ((int x, int y) in step.Tiles)
                store.MarkEdited(x, y);
            return step;
        }

        /// <summary>Puts a step's samples back to before (undo) or after (redo).</summary>
        public static void Restore(WorldStore store, EditStep step, bool after)
        {
            foreach ((int sx, int sy, short height, byte[] ids, byte[] ws) in after ? step.After : step.Before)
            {
                store.SetHeight(sx, sy, height);
                store.SetSlots(sx, sy, ids, ws);
            }
            foreach ((int x, int y, ushort before, ushort afterGround) in step.Ground)
                store.SetGround(x, y, after ? afterGround : before);
        }

        private static short ClampHeight(float v) =>
            (short)Math.Max(-127 * WorldRegion.ZScale, Math.Min(127 * WorldRegion.ZScale, Math.Round(v)));
    }

    /// <summary>The server's tiles from the world layer (WORLD_EDITOR.md, "Derivation rules").</summary>
    public static class WorldDerive
    {
        /// <summary>
        /// For an edited tile: its Z (the layer's height at the tile's top corner, rounded) and land graphic (the dominant
        /// material at the tile's centre; the original graphic when that is the tile's own material or classic, else a
        /// plain graphic of the material chosen by position).
        /// </summary>
        public static (int Graphic, int Z) Tile(WorldStore store, int x, int y, int originalGraphic, byte originalMaterial, int[][] plain)
        {
            const int s = WorldRegion.SamplesPerTile;
            int z = (int)Math.Round(store.GetHeight(x * s, y * s) / (float)WorldRegion.ZScale);
            z = Math.Max(-128, Math.Min(127, z));
            ushort painted = store.GetGround(x, y);
            if (painted != 0)
                return (painted, z); // UO ground laid by hand (SURFACE)
            var w = new float[WorldMaterials.Count];
            store.GetWeights(x * s + s / 2, y * s + s / 2, w);
            int dominant = WorldMaterials.Classic;
            float best = -1f;
            for (int m = 0; m < w.Length; m++)
                if (w[m] > best)
                {
                    best = w[m];
                    dominant = m;
                }
            if (dominant == WorldMaterials.Classic || dominant == originalMaterial || plain[dominant].Length == 0)
                return (originalGraphic, z);
            int[] choices = plain[dominant];
            int pick = (int)(WorldInit.Hash(x, y, 97) * choices.Length) % choices.Length;
            return (choices[pick], z);
        }
    }
}
