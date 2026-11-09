using System;
using System.Collections.Generic;
using UOClient.IO.Formats;
using UOClient.Unity;
using UOClient.WorldLayer;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// The natural terrain's answers for <see cref="GroundFollow"/> (main thread): the drawn ground's height at any point
    /// (the world editor's painted samples where a region is held, bilinear, else the same smooth surface the terrain is
    /// built from: TerrainLandBuilder.SmoothZ over the map's corner Z), and UO's average land Z of a tile (the server's
    /// rule for where a character stands on land). Land blocks are cached and dropped whenever the map's land changes
    /// (MapFile.OverrideVersion, the world editor's derived tiles) or the facet does.
    /// Not modelled: the water bed's lowering near shores (characters do not stand in water).
    /// </summary>
    internal sealed class DrawnGround
    {
        private readonly WorldStreamer _world;
        private readonly Dictionary<long, LandCell[]> _blocks = new Dictionary<long, LandCell[]>();
        private MapFile _map;
        private int _version = -1;

        public DrawnGround(WorldStreamer world) => _world = world;

        public void Install()
        {
            GroundFollow.DrawnZ = DrawnZ;
            GroundFollow.LandAverageZ = LandAverageZ;
        }

        public static void Uninstall()
        {
            GroundFollow.DrawnZ = null;
            GroundFollow.LandAverageZ = null;
        }

        private MapFile Map()
        {
            MapFile map = _world?.Data?.GetMap(_world.Map);
            if (map != _map || map != null && map.OverrideVersion != _version || _blocks.Count > 4096)
            {
                _blocks.Clear();
                _map = map;
                _version = map?.OverrideVersion ?? -1;
            }
            return map;
        }

        private int LandZ(MapFile map, int x, int y)
        {
            x = Math.Max(0, Math.Min(map.Width - 1, x));
            y = Math.Max(0, Math.Min(map.Height - 1, y));
            long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            if (!_blocks.TryGetValue(key, out LandCell[] cells))
                _blocks[key] = cells = map.ReadLandBlock(x >> 3, y >> 3);
            return cells[((y & 7) << 3) | (x & 7)].Z;
        }

        private float? DrawnZ(float wx, float wy)
        {
            MapFile map = Map();
            if (map == null)
                return null;
            // Painted (or made by the editor): its samples, bilinear.
            WorldLayerClient layer = WorldLayerClient.Current;
            if (layer != null)
            {
                const int s = WorldRegion.SamplesPerTile;
                float fx = wx * s, fy = wy * s;
                int sx = (int)Math.Floor(fx), sy = (int)Math.Floor(fy);
                (int rx, int ry) = WorldRegion.RegionOfSample(sx, sy);
                if (layer.LiveRegion(_world.Map, rx, ry) is WorldRegion r)
                {
                    int lx = sx - r.SampleX0, ly = sy - r.SampleY0;
                    if (lx >= 0 && ly >= 0 && lx + 1 < WorldRegion.N && ly + 1 < WorldRegion.N)
                    {
                        float u = fx - sx, v = fy - sy;
                        float h00 = r.Height[WorldRegion.Index(lx, ly)], h10 = r.Height[WorldRegion.Index(lx + 1, ly)];
                        float h01 = r.Height[WorldRegion.Index(lx, ly + 1)], h11 = r.Height[WorldRegion.Index(lx + 1, ly + 1)];
                        float h = (h00 + (h10 - h00) * u) * (1 - v) + (h01 + (h11 - h01) * u) * v;
                        return h / WorldRegion.ZScale;
                    }
                }
            }
            return TerrainLandBuilder.SmoothZ((x, y) => LandZ(map, x, y), wx, wy);
        }

        /// <summary>UO's average land Z of a tile (Map.GetAverageZ): the mean of the diagonal with the smaller difference.</summary>
        private int? LandAverageZ(int x, int y)
        {
            MapFile map = Map();
            if (map == null)
                return null;
            int top = LandZ(map, x, y), left = LandZ(map, x, y + 1), right = LandZ(map, x + 1, y), bottom = LandZ(map, x + 1, y + 1);
            return Math.Abs(top - bottom) > Math.Abs(left - right)
                ? FloorAverage(left, right)
                : FloorAverage(top, bottom);
        }

        private static int FloorAverage(int a, int b)
        {
            int sum = a + b;
            return sum < 0 ? (sum - 1) / 2 : sum / 2;
        }
    }
}
