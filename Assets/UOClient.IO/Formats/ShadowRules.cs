namespace UOClient.IO.Formats
{
    /// <summary>
    /// Which objects cast a sun shadow of their own outline (the client's CharacterShadow: the art laid on the ground from
    /// its base) instead of a tile-wide column in the ground's sun mask. Free-standing things: trees and their leaves,
    /// plants, rocks, posts, signs, lamps, furniture. Building parts keep the tile-aligned sun mask (walls, roofs, floors
    /// and other surfaces, bridges, doors, windows): a slanted picture of one wall piece would not join its neighbours'
    /// shadows. Water and internal art cast nothing.
    /// Tiledata heights undersell many upright things (a signpost is 0, a rock 2, the leaves on a tree 2), so an object
    /// also casts when its art stands at least <see cref="MinArtHeight"/> pixels tall (unscaled), and foliage always
    /// does; flat things (papers, coins, rugs) are short in both and cast nothing. Note UO's trees carry the Background
    /// flag, so it does not exclude.
    /// </summary>
    public static class ShadowRules
    {
        /// <summary>A tiledata height (Z) from which an object casts whatever its art.</summary>
        public const int MinHeight = 3;

        /// <summary>An art height (unscaled pixels, half a tile) from which a low-tiledata object still casts.</summary>
        public const int MinArtHeight = 22;

        private const TileFlags BuildingParts = TileFlags.Wall | TileFlags.Roof | TileFlags.Surface | TileFlags.Bridge |
                                                TileFlags.Door | TileFlags.Window;

        /// <param name="artHeight">The art's height in unscaled pixels, or 0 when unknown (tiledata alone decides).</param>
        public static bool CastsOutline(in StaticTileData td, int artHeight = 0)
        {
            if ((td.Flags & (BuildingParts | TileFlags.Wet | TileFlags.Internal)) != 0)
                return false;
            // Wall pieces whose flags do not say so (posts and panels of a wall, wall trim): named for it.
            string name = td.Name ?? "";
            if (name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return td.Height >= MinHeight || (td.Flags & TileFlags.Foliage) != 0 || artHeight >= MinArtHeight;
        }
    }
}
