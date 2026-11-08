using System;
using System.Collections.Generic;

namespace UOClient.WorldLayer
{
    // Shared with the server (ModernUO) as source: only System, C# 9, no Unity, no client types.
    /// <summary>
    /// The world layer's ground materials (WORLD_EDITOR.md). Ids are stored in region files and sent over the wire:
    /// never renumber, only append. <see cref="Classic"/> keeps a tile's original UO ground (built surfaces, caves,
    /// anything without a material of its own).
    /// </summary>
    public static class WorldMaterials
    {
        public const byte Grass = 0, Dirt = 1, Forest = 2, Jungle = 3, Sand = 4, Rock = 5, Snow = 6, Cobble = 7;

        /// <summary>Keep the tile's original UO ground.</summary>
        public const byte Classic = 8;

        /// <summary>Number of materials (ids 0 .. Count - 1).</summary>
        public const int Count = 9;

        public static readonly string[] Names = { "grass", "dirt", "forest", "jungle", "sand", "rock", "snow", "cobble", "classic" };

        /// <summary>The material a UO land tile's tiledata name stands for ("decimated_" variants count as their base).</summary>
        public static byte Classify(string name)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            if (n.StartsWith("decimated_"))
                n = n.Substring("decimated_".Length);
            switch (n)
            {
                case "grass":
                    return Grass;
                case "dirt":
                case "embank":
                case "furrows":
                    return Dirt;
                case "forest":
                case "tree":
                case "leaves":
                    return Forest;
                case "jungle":
                    return Jungle;
                case "sand":
                case "sand stone":
                    return Sand;
                case "rock":
                    return Rock;
                case "snow":
                case "snow dirt path":
                case "snow grass path":
                case "snow stone path":
                case "snow sand path":
                    return Snow;
                case "cobblestones":
                case "cobbleston":
                case "stone":
                case "stone moss":
                case "stone moss2":
                case "flagstone":
                case "stone_paver":
                    return Cobble;
                default:
                    return Classic;
            }
        }

        /// <summary>
        /// How far (tiles) a material reaches past a tile edge in the initial blend: natural ground wanders, built
        /// surfaces (cobble, classic) keep a firm edge.
        /// </summary>
        public static float Softness(int material) => material == Cobble || material == Classic ? 0.22f : 0.9f;

        /// <summary>
        /// The plain (non-transition) UO land graphics each material paints with, as ranges of the stock art: the
        /// full-tile variants, not the hand-drawn edge pieces. <see cref="Plain"/> keeps only those tiledata agrees with.
        /// </summary>
        private static readonly (int First, int Last)[][] PlainRanges =
        {
            new[] { (0x0003, 0x0006) },                 // grass
            new[] { (0x0071, 0x0078) },                 // dirt
            new[] { (0x00C4, 0x00C7) },                 // forest
            new[] { (0x00AC, 0x00AF) },                 // jungle
            new[] { (0x0016, 0x0019) },                 // sand
            new[] { (0x00DC, 0x00DF), (0x022C, 0x022F) }, // rock
            new[] { (0x011A, 0x011D) },                 // snow
            new[] { (0x03EC, 0x03EF), (0x0402, 0x0405) }, // cobble
            new (int, int)[0]                           // classic: the tile keeps its own
        };

        /// <summary>
        /// Per material, the plain land graphics to paint with: the stock ranges whose tiledata name classifies as that
        /// material. A material with none of its stock graphics agreeing falls back to every graphic of that material.
        /// </summary>
        public static int[][] Plain(Func<int, string> landName, int landCount)
        {
            var result = new int[Count][];
            for (int m = 0; m < Count; m++)
            {
                var list = new List<int>();
                foreach ((int first, int last) in PlainRanges[m])
                    for (int g = first; g <= last && g < landCount; g++)
                        if (Classify(landName(g)) == m)
                            list.Add(g);
                if (list.Count == 0 && m != Classic)
                    for (int g = 0; g < landCount; g++)
                        if (Classify(landName(g)) == m)
                            list.Add(g);
                result[m] = list.ToArray();
            }
            return result;
        }
    }
}
