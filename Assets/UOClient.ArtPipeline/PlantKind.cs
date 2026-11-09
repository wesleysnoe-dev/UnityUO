using System;
using UOClient.IO.Formats;

namespace UOClient.ArtPipeline
{
    /// <summary>
    /// Which statics are plants, for the wind (they lean with it) and the season (leaves turn). Leafy parts sway the most
    /// and take the season's colour; trunks lean a little and keep theirs. Encoded for the sprite shader in the second UV's
    /// y (WorldSprite), where 1 already means water: leafy 2..3, woody 4..5, evergreen 6..7, the fraction the vertex's
    /// sway (0 at the art's foot, the weight at its top). Evergreens (needles) sway but keep their green until winter.
    /// </summary>
    public static class PlantKind
    {
        public const float None = 0f, Leafy = 2f, Woody = 4f, Evergreen = 6f;

        // Conifers: their needles stay green through the fall.
        private static readonly string[] Needles = { "needle", "pine", "cedar", "spruce", "fir ", "yew", "juniper", "cypress" };

        /// <summary>Sway at the art's top for a tree's canopy, a single-sprite palm, a small plant and a trunk.</summary>
        public const float CanopyWeight = 0.95f, PalmWeight = 0.8f, SmallWeight = 0.6f, TrunkWeight = 0.3f;

        // Anything built, carried or potted: no wind, no season (a potted fern indoors, a bale of hay, a log).
        private static readonly string[] NotPlants =
        {
            "pot", "vase", "basket", "dead", "fallen", "stump", "log", "rock", "hay", "barrel", "crate", "table", "statue",
            "painting", "carpet", "rug", "banner", "seed", "bowl", "box", "sign", "lamp", "candle", "bale"
        };

        // Single-sprite trees whose fronds are the art (no separate Foliage canopy).
        private static readonly string[] Palms = { "palm", "o'hii", "banana", "bamboo", "yucca", "cypress" };

        private static readonly string[] SmallPlants =
        {
            "grass", "flower", "weed", "sapling", "rush", "reed", "fern", "bush", "bramble", "plant", "cattail", "vine",
            "lily", "lilies", "poppy", "poppies", "rose", "orfluer", "foxglove", "campion", "snowdrop", "pampas",
            "juniper", "hedge", "wheat", "stalk", "cotton", "flax", "shrub", "thistle", "heather", "clover", "ivy", "moss"
        };

        /// <summary>
        /// The plant code of a static: <see cref="Leafy"/> or <see cref="Woody"/> plus its top's sway weight, or
        /// <see cref="None"/>.
        /// </summary>
        public static float Of(in StaticTileData td)
        {
            const TileFlags built = TileFlags.Wall | TileFlags.Door | TileFlags.Window | TileFlags.Roof | TileFlags.Surface
                                    | TileFlags.Wet | TileFlags.Container | TileFlags.LightSource;
            if ((td.Flags & built) != 0)
                return None;
            string name = (td.Name ?? "").ToLowerInvariant();
            if ((td.Flags & TileFlags.Foliage) != 0)
                return (Contains(name, Needles) ? Evergreen : Leafy) + CanopyWeight; // a tree's needles or leaves
            if (name.Length == 0 || Contains(name, NotPlants))
                return None;
            if (Contains(name, Palms))
                return (Contains(name, Needles) ? Evergreen : Leafy) + PalmWeight;
            if (name.EndsWith("tree") && td.Height >= 10)
                return Woody + TrunkWeight;
            if (Contains(name, SmallPlants))
                return Leafy + SmallWeight;
            return None;
        }

        /// <summary>The value at a vertex: the kind at the art's foot, the kind plus the weight at its top.</summary>
        public static float AtFoot(float code) => code <= 0f ? 0f : (float)Math.Floor(code);

        private static bool Contains(string name, string[] words)
        {
            foreach (string w in words)
                if (name.Contains(w))
                    return true;
            return false;
        }
    }
}
