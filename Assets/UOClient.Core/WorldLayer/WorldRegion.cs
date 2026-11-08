using System;
using System.IO;
using System.IO.Compression;

namespace UOClient.WorldLayer
{
    // Shared with the server (ModernUO) as source: only System, C# 9, no Unity, no client types.
    /// <summary>
    /// One region of the world layer (WORLD_EDITOR.md): <see cref="Tiles"/>² map tiles, painted shape and ground at
    /// <see cref="SamplesPerTile"/> samples per tile edge. Samples sit on a grid of <see cref="N"/>² that includes both
    /// edges, so a region shares its edge samples with its neighbours (both keep a copy; the store writes both).
    ///
    /// Sample (sx, sy) of the world is tile (sx / SamplesPerTile, sy / SamplesPerTile) plus a fraction: sample 0 of a
    /// tile is its top corner, the corner UO stores the tile's Z at. Heights are in 1/<see cref="ZScale"/> UO Z.
    /// Materials: per sample up to <see cref="Slots"/> (id, weight) pairs, weights summing to 255 (0 weight = unused).
    /// <see cref="Edited"/> marks the tiles any edit touched: only those get new tiles derived for the server; the
    /// rest keep the map exactly as it was.
    /// </summary>
    public sealed class WorldRegion
    {
        public const int Tiles = 64, SamplesPerTile = 4, N = Tiles * SamplesPerTile + 1, Slots = 4, ZScale = 8;
        public const int SamplesPerRegion = Tiles * SamplesPerTile; // 256: region rx owns samples rx*256 .. rx*256+256
        private const int Magic = 0x4C574F55; // "UOWL"
        private const int FormatVersion = 2; // 2: per-tile Ground (version 1 files still load, without it)

        public readonly int Map, Rx, Ry;

        /// <summary>Bumped on every change (the client re-requests a region whose version moved).</summary>
        public int Version;

        public readonly short[] Height = new short[N * N];
        public readonly byte[] MatId = new byte[N * N * Slots];
        public readonly byte[] MatW = new byte[N * N * Slots];
        public readonly bool[] Edited = new bool[Tiles * Tiles];

        /// <summary>
        /// Per tile: a UO land graphic painted as is (the SURFACE palette: floors, caves, lava, any classic ground), or 0
        /// for none (the tile's ground comes from the blended materials). Indexed [ly * Tiles + lx].
        /// </summary>
        public readonly ushort[] Ground = new ushort[Tiles * Tiles];

        public WorldRegion(int map, int rx, int ry)
        {
            Map = map;
            Rx = rx;
            Ry = ry;
        }

        /// <summary>A copy (the client's redraw reads a snapshot while painting goes on in the original).</summary>
        public WorldRegion Clone()
        {
            var c = new WorldRegion(Map, Rx, Ry) { Version = Version };
            Array.Copy(Height, c.Height, Height.Length);
            Array.Copy(MatId, c.MatId, MatId.Length);
            Array.Copy(MatW, c.MatW, MatW.Length);
            Array.Copy(Edited, c.Edited, Edited.Length);
            Array.Copy(Ground, c.Ground, Ground.Length);
            return c;
        }

        public int TileX0 => Rx * Tiles;
        public int TileY0 => Ry * Tiles;
        public int SampleX0 => Rx * SamplesPerRegion;
        public int SampleY0 => Ry * SamplesPerRegion;

        public static int Index(int lx, int ly) => ly * N + lx;

        /// <summary>The region that owns a world sample (an edge sample is also in the region before it).</summary>
        public static (int Rx, int Ry) RegionOfSample(int sx, int sy) =>
            (FloorDiv(sx, SamplesPerRegion), FloorDiv(sy, SamplesPerRegion));

        public static (int Rx, int Ry) RegionOfTile(int x, int y) => (FloorDiv(x, Tiles), FloorDiv(y, Tiles));

        public static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        // ---- materials ----

        /// <summary>The material with the most weight at a local sample.</summary>
        public byte Dominant(int index)
        {
            int o = index * Slots, best = -1;
            byte id = WorldMaterials.Classic;
            for (int s = 0; s < Slots; s++)
                if (MatW[o + s] > best)
                {
                    best = MatW[o + s];
                    id = MatId[o + s];
                }
            return id;
        }

        /// <summary>Sets one material at full weight.</summary>
        public void SetSingle(int index, byte material)
        {
            int o = index * Slots;
            for (int s = 0; s < Slots; s++)
            {
                MatId[o + s] = 0;
                MatW[o + s] = 0;
            }
            MatId[o] = material;
            MatW[o] = 255;
        }

        /// <summary>
        /// Sets a sample's materials from full weights (index = material id): the <see cref="Slots"/> heaviest are kept and
        /// renormalised to 255, the rest dropped.
        /// </summary>
        public void SetWeights(int index, float[] weights)
        {
            int o = index * Slots;
            Span<int> top = stackalloc int[Slots];
            for (int s = 0; s < Slots; s++)
                top[s] = -1;
            for (int m = 0; m < weights.Length; m++)
            {
                if (weights[m] <= 0f)
                    continue;
                for (int s = 0; s < Slots; s++)
                    if (top[s] < 0 || weights[m] > weights[top[s]])
                    {
                        for (int k = Slots - 1; k > s; k--)
                            top[k] = top[k - 1];
                        top[s] = m;
                        break;
                    }
            }
            float sum = 0f;
            for (int s = 0; s < Slots; s++)
                if (top[s] >= 0)
                    sum += weights[top[s]];
            int given = 0;
            for (int s = 0; s < Slots; s++)
            {
                if (top[s] < 0 || sum <= 0f)
                {
                    MatId[o + s] = 0;
                    MatW[o + s] = 0;
                    continue;
                }
                int w = s == 0 ? 0 : (int)Math.Round(255f * weights[top[s]] / sum);
                MatId[o + s] = (byte)top[s];
                MatW[o + s] = (byte)w;
                given += w;
            }
            if (top[0] >= 0 && sum > 0f)
                MatW[o] = (byte)Math.Max(0, 255 - given); // the heaviest takes the rounding, so the total is exactly 255
        }

        /// <summary>Adds a sample's weights into a full array (index = material id), as fractions of 1.</summary>
        public void GetWeights(int index, float[] weights)
        {
            Array.Clear(weights, 0, weights.Length);
            int o = index * Slots;
            for (int s = 0; s < Slots; s++)
                if (MatW[o + s] > 0 && MatId[o + s] < weights.Length)
                    weights[MatId[o + s]] += MatW[o + s] / 255f;
        }

        // ---- file / wire format ----

        public byte[] Serialize()
        {
            using var raw = new MemoryStream();
            using (var z = new DeflateStream(raw, CompressionLevel.Optimal, true))
            using (var w = new BinaryWriter(z))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(Map);
                w.Write(Rx);
                w.Write(Ry);
                w.Write(Version);
                foreach (short h in Height)
                    w.Write(h);
                w.Write(MatId);
                w.Write(MatW);
                foreach (bool e in Edited)
                    w.Write(e);
                foreach (ushort g in Ground)
                    w.Write(g);
            }
            return raw.ToArray();
        }

        public static WorldRegion Deserialize(byte[] data)
        {
            using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
            using var r = new BinaryReader(z);
            if (r.ReadInt32() != Magic)
                throw new InvalidDataException("not a world layer region");
            int format = r.ReadInt32();
            if (format < 1 || format > FormatVersion)
                throw new InvalidDataException($"world layer region format {format}, expected 1 to {FormatVersion}");
            var region = new WorldRegion(r.ReadInt32(), r.ReadInt32(), r.ReadInt32()) { Version = r.ReadInt32() };
            for (int i = 0; i < region.Height.Length; i++)
                region.Height[i] = r.ReadInt16();
            ReadExactly(r, region.MatId);
            ReadExactly(r, region.MatW);
            for (int i = 0; i < region.Edited.Length; i++)
                region.Edited[i] = r.ReadBoolean();
            if (format >= 2)
                for (int i = 0; i < region.Ground.Length; i++)
                    region.Ground[i] = r.ReadUInt16();
            return region;
        }

        private static void ReadExactly(BinaryReader r, byte[] into)
        {
            int read = 0;
            while (read < into.Length)
            {
                int n = r.Read(into, read, into.Length - read);
                if (n <= 0)
                    throw new EndOfStreamException("world layer region is truncated");
                read += n;
            }
        }
    }
}
