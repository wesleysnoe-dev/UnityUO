using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Which UO art the FX layer dresses up, and how. Flames: light-source art whose tiledata name says fire (torches,
    /// candles, braziers, campfires, fireplaces, fire fields); the flame sits on the art's own fire pixels, found once per
    /// graphic. Glowing effects: server graphic effects drawn additive and unlit (they are light, not things), some with
    /// a particle trail or burst and a light of their own.
    /// </summary>
    public static class FxCatalog
    {
        public enum FlameKind { None, Candle, Torch, Brazier, Campfire, Hearth, Field }

        /// <summary>Where a graphic's fire is on its art (1x pixels from the art's top-left), and how wide.</summary>
        public struct FireSpot
        {
            public bool Found;
            public float X, Y, Width;
        }

        private static readonly Dictionary<int, FlameKind> Kinds = new Dictionary<int, FlameKind>();
        private static readonly Dictionary<int, FireSpot> Spots = new Dictionary<int, FireSpot>();

        /// <summary>-no-fx turns the whole FX layer off (flames, glow, effect lights, bloom).</summary>
        public static readonly bool Enabled = !HasArg("-no-fx");

        /// <summary>The loaded UO data, for lookups that only have a graphic (ItemLights' flicker).</summary>
        public static UOData Data { get; set; }

        public static bool Flickers(int graphic) => Flame(Data, graphic) != FlameKind.None;

        public static FlameKind Flame(UOData data, int graphic)
        {
            if (!Enabled || data == null)
                return FlameKind.None;
            if (Kinds.TryGetValue(graphic, out FlameKind k))
                return k;
            string n = (data.TileData.GetStatic(graphic).Name ?? "").ToLowerInvariant();
            k = n.Contains("fire field") || n.Contains("firefield") || n.Contains("field of fire") ? FlameKind.Field
                : n.Contains("campfire") || n.Contains("camp fire") || n.Contains("bonfire") ? FlameKind.Campfire
                : n.Contains("brazier") || n.Contains("firepit") || n.Contains("fire pit") ? FlameKind.Brazier
                : n.Contains("fireplace") || n.Contains("hearth") || n.Contains("oven") || n.Contains("forge") ? FlameKind.Hearth
                : n.Contains("torch") || n.Contains("sconce") ? FlameKind.Torch
                : n.Contains("candle") || n.Contains("candelabra") || n.Contains("lantern") || n.Contains("lamp") ? FlameKind.Candle
                : n == "fire" || n.StartsWith("fire ") || n.EndsWith(" fire") ? FlameKind.Campfire
                : FlameKind.None;
            Kinds[graphic] = k;
            return k;
        }

        /// <summary>
        /// The fire on a graphic's art: the centroid and spread of its flame-coloured pixels (bright, red over green over
        /// blue), on the base frame. Not found (no such pixels): the flame goes at the art's top centre.
        /// </summary>
        public static FireSpot Spot(UOData data, int graphic)
        {
            if (Spots.TryGetValue(graphic, out FireSpot s))
                return s;
            s = new FireSpot();
            Bitmap32 art = data?.GetStatic(graphic);
            if (art != null && art.Width > 0)
            {
                double sx = 0, sy = 0;
                int n = 0, minX = int.MaxValue, maxX = int.MinValue, top = int.MaxValue;
                for (int y = 0; y < art.Height; y++)
                    for (int x = 0; x < art.Width; x++)
                    {
                        uint p = art.Pixels[y * art.Width + x];
                        if ((p >> 24) == 0)
                            continue;
                        if (y < top)
                            top = y;
                        int r = (int)(p >> 16) & 0xFF, g = (int)(p >> 8) & 0xFF, b = (int)p & 0xFF;
                        if (r > 170 && r >= g + 30 && g >= b && g > 60)
                        {
                            sx += x; sy += y; n++;
                            minX = Math.Min(minX, x);
                            maxX = Math.Max(maxX, x);
                        }
                    }
                if (n >= 3)
                    s = new FireSpot { Found = true, X = (float)(sx / n), Y = (float)(sy / n), Width = Math.Max(3, maxX - minX + 1) };
                else if (top != int.MaxValue)
                    s = new FireSpot { Found = false, X = art.Width / 2f, Y = top + 2, Width = Math.Max(3, art.Width / 4f) };
            }
            Spots[graphic] = s;
            return s;
        }

        /// <summary>How a server graphic effect is drawn: additive glow, a trail, a burst, a light (colour, radius in tiles).</summary>
        public struct EffectStyle
        {
            public bool Glow, Unlit, Trail, Burst;
            public Color Light;
            public float Radius;
        }

        private static readonly Color Fire = new Color(1f, 0.55f, 0.18f), Energy = new Color(0.45f, 0.6f, 1f),
            Holy = new Color(1f, 0.95f, 0.6f), Poison = new Color(0.4f, 1f, 0.3f);

        public static EffectStyle Effect(int graphic)
        {
            if (!Enabled)
                return default;
            switch (graphic)
            {
                case 0x36D4: // fireball
                case 0x36E4: // magic arrow (a small fire missile)
                    return new EffectStyle { Glow = true, Trail = true, Light = Fire, Radius = graphic == 0x36D4 ? 4f : 2.5f };
                case 0x36B0: case 0x36BD: case 0x36CB: // explosions
                    return new EffectStyle { Unlit = true, Burst = true, Light = Fire, Radius = 7f };
                case 0x3709: // flame strike
                    return new EffectStyle { Unlit = true, Burst = true, Light = Fire, Radius = 6f };
                case 0x379F: // energy bolt
                    return new EffectStyle { Glow = true, Trail = true, Light = Energy, Radius = 4f };
                case 0x3779: case 0x37B9: case 0x376A: case 0x375A: case 0x373A: case 0x374A: // sparkles: heal, cure, bless, ...
                    return new EffectStyle { Glow = true, Light = Holy, Radius = 2.5f };
                case 0x36FE: case 0x3818: // energy field flashes
                    return new EffectStyle { Glow = true, Light = Energy, Radius = 3f };
                case 0x113A: case 0x36D5: // poison clouds
                    return new EffectStyle { Glow = true, Light = Poison, Radius = 2.5f };
                default:
                    return default;
            }
        }

        internal static bool HasArg(string name)
        {
            foreach (string a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
