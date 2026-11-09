using System;

namespace UOClient.IO.Formats
{
    /// <summary>
    /// UO's own water art (land tiles and ocean statics): tiledata Wet and named "water", the same test as the LiveGround
    /// water (WaterTiles.IsWater). The client draws it as UO does and animates it in the sprite shader (WorldSprite's
    /// water shimmer), instead of a separate water surface.
    /// </summary>
    public static class WaterArt
    {
        public static bool IsWater(TileFlags flags, string name) =>
            (flags & TileFlags.Wet) != 0 && string.Equals(name?.Trim(), "water", StringComparison.OrdinalIgnoreCase);

        public static bool IsWater(in LandTileData land) => IsWater(land.Flags, land.Name);

        public static bool IsWater(in StaticTileData item) => IsWater(item.Flags, item.Name);
    }
}
