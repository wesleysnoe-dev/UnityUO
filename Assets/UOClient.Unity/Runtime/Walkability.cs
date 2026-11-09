using System;
using System.Collections.Generic;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Simplified UO walking rules for the demo, offline (no server): stand on the highest surface
    /// within a step of the current Z, unless an impassable static fills the 16-unit body height.
    /// The real client will port ClassicUO's pathfinder rules into UOClient.Core.
    /// </summary>
    public sealed class Walkability
    {
        private const int StepUp = 12;
        private const int BodyHeight = 16;

        private readonly MapFile _map;
        private readonly TileData _tileData;
        private readonly Dictionary<long, StaticCell[]> _statics = new Dictionary<long, StaticCell[]>();

        public Walkability(UOData data, int map)
        {
            _map = data.GetMap(map);
            _tileData = data.TileData;
        }

        /// <summary>Z the mobile would stand at on (x, y) coming from <paramref name="currentZ"/>, or null if blocked.</summary>
        public int? TryStep(int x, int y, int currentZ)
        {
            int best = int.MinValue;

            // Land: averaged corners, as the classic client does for sloped ground.
            LandCell land = _map.GetLand(x, y);
            LandTileData landData = _tileData.Land[land.Graphic & 0x3FFF];
            if ((landData.Flags & TileFlags.Impassable) == 0 && land.Graphic > 2)
            {
                int avg = AverageLandZ(x, y);
                if (avg <= currentZ + StepUp)
                    best = avg;
            }

            StaticCell[] statics = StaticsAt(x, y);
            foreach (StaticCell s in statics)
            {
                StaticTileData td = _tileData.GetStatic(s.Graphic);
                if ((td.Flags & (TileFlags.Surface | TileFlags.Bridge)) == 0 || (td.Flags & TileFlags.Impassable) != 0)
                    continue;
                int top = s.Z + ((td.Flags & TileFlags.Bridge) != 0 ? td.Height / 2 : td.Height);
                if (top <= currentZ + StepUp && top > best)
                    best = top;
            }

            if (best == int.MinValue)
                return null;

            foreach (StaticCell s in statics)
            {
                StaticTileData td = _tileData.GetStatic(s.Graphic);
                bool solid = (td.Flags & TileFlags.Impassable) != 0
                             || ((td.Flags & TileFlags.Surface) != 0 && s.Z > best); // a floor above our head
                if (!solid)
                    continue;
                int bottom = s.Z, topOfStatic = s.Z + Math.Max(1, (int)td.Height);
                if (bottom < best + BodyHeight && topOfStatic > best)
                    return null;
            }

            return best;
        }

        /// <summary>
        /// The height of the lowest thing over our head on this tile (a floor above, a roof): at least 14 above
        /// the feet, as the classic client measures it. Null in the open. Everything from there up is not drawn,
        /// which opens up the upper floors of banks, shops and houses while we are inside.
        /// </summary>
        public int? OverheadZ(int x, int y, int z)
        {
            int? lowest = null;
            foreach (StaticCell s in StaticsAt(x, y))
                if (s.Z >= z + 14 && (lowest == null || s.Z < lowest))
                    lowest = s.Z;
            return lowest;
        }

        /// <summary>
        /// Whether a wall stands between a light on (lx, ly) and a tile (x, y) at about Z <paramref name="z"/>: any tile on
        /// the straight line from the light (its own tile left out: wall sconces hang on wall tiles) to the tile, the tile
        /// itself included, with a wall, window or door static spanning z + 2 .. z + 14.
        /// </summary>
        public bool WallBetween(int lx, int ly, int x, int y, int z)
        {
            int dx = Math.Abs(x - lx), dy = Math.Abs(y - ly), sx = lx < x ? 1 : -1, sy = ly < y ? 1 : -1, err = dx - dy;
            int cx = lx, cy = ly;
            while (cx != x || cy != y)
            {
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; cx += sx; }
                if (e2 < dx) { err += dx; cy += sy; }
                foreach (StaticCell s in StaticsAt(cx, cy))
                {
                    StaticTileData td = _tileData.GetStatic(s.Graphic);
                    if ((td.Flags & (TileFlags.Wall | TileFlags.Window | TileFlags.Door)) != 0 && s.Z <= z + 14 && s.Z + Math.Max(1, (int)td.Height) >= z + 2)
                        return true;
                }
            }
            return false;
        }

        /// <summary>True when something overhead (a roof, or a floor above) covers the tile, like the classic client's indoor check.</summary>
        public bool IsUnderRoof(int x, int y, int z)
        {
            foreach (StaticCell s in StaticsAt(x, y))
            {
                StaticTileData td = _tileData.GetStatic(s.Graphic);
                if (s.Z >= z + BodyHeight && (td.IsRoof || td.IsSurface))
                    return true;
            }
            return false;
        }

        /// <summary>The height something at (x, y) would stand on, looking from about <paramref name="refZ"/>: the highest surface within reach, else the land.</summary>
        public int SurfaceZ(int x, int y, int refZ)
        {
            int best = AverageLandZ(x, y);
            foreach (StaticCell s in StaticsAt(x, y))
            {
                StaticTileData td = _tileData.GetStatic(s.Graphic);
                if ((td.Flags & (TileFlags.Surface | TileFlags.Bridge)) == 0)
                    continue;
                int top = s.Z + ((td.Flags & TileFlags.Bridge) != 0 ? td.Height / 2 : td.Height);
                if (top <= refZ + 20 && top > best)
                    best = top;
            }
            return best;
        }

        /// <summary>
        /// The static a target cursor click on (x, y) means for someone at <paramref name="refZ"/> (map statics and
        /// house parts), with its tiledata surface flag and height, or null to target the land.
        /// </summary>
        public (StaticCell Cell, bool Surface, int Height)? TargetStaticAt(int x, int y, int refZ)
        {
            StaticCell[] statics = StaticsAt(x, y);
            int i = UOClient.Core.GameSession.PickTargetStatic(statics, refZ);
            if (i < 0)
                return null;
            StaticTileData td = _tileData.GetStatic(statics[i].Graphic);
            return (statics[i], td.IsSurface, td.Height);
        }

        public int AverageLandZ(int x, int y)
        {
            int zTop = _map.GetLand(x, y).Z, zRight = _map.GetLand(x + 1, y).Z;
            int zBottom = _map.GetLand(x + 1, y + 1).Z, zLeft = _map.GetLand(x, y + 1).Z;
            return Math.Abs(zTop - zBottom) <= Math.Abs(zLeft - zRight) ? (zTop + zBottom) >> 1 : (zLeft + zRight) >> 1;
        }

        /// <summary>The map statics and house / boat parts on a tile (for a click naming what is there).</summary>
        public StaticCell[] StaticsOn(int x, int y) => StaticsAt(x, y);

        private StaticCell[] StaticsAt(int x, int y)
        {
            long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            if (!_statics.TryGetValue(key, out StaticCell[] block))
                _statics[key] = block = _map.ReadStaticBlock(x >> 3, y >> 3);

            var result = new List<StaticCell>();
            foreach (StaticCell s in block)
                if (s.X == (x & 7) && s.Y == (y & 7))
                    result.Add(s);
            if (_extra.TryGetValue((x, y), out List<(uint Owner, StaticCell Cell)> extra))
                foreach (var e in extra)
                    result.Add(e.Cell);
            return result.ToArray();
        }

        // Houses and boats: their fixed parts count as statics while they stand there.
        private readonly Dictionary<(int, int), List<(uint Owner, StaticCell Cell)>> _extra = new Dictionary<(int, int), List<(uint, StaticCell)>>();

        public void AddMultiPart(uint owner, int x, int y, int z, ushort graphic)
        {
            if (!_extra.TryGetValue((x, y), out var list))
                _extra[(x, y)] = list = new List<(uint, StaticCell)>();
            list.Add((owner, new StaticCell { Graphic = graphic, X = (byte)(x & 7), Y = (byte)(y & 7), Z = (sbyte)Math.Max(-128, Math.Min(127, z)) }));
        }

        public void RemoveMulti(uint owner)
        {
            foreach (var list in _extra.Values)
                list.RemoveAll(e => e.Owner == owner);
        }
    }
}
