using System;
using System.Collections.Generic;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Which world art gives light, with which light.mul shape and colour, as TazUO's GameScene.AddLight
    /// (GameScene.cs:577-721) and LightColors (LightColors.cs): a ground item with the tiledata LightSource flag lights by
    /// the light byte the server sends (ModernUO's Item.Light, in 0xF3's direction byte); a map static or multi part by
    /// its tiledata Layer; fields 0x3E02-0x3E0B, 0x3914-0x3929 and 0x0B1D use shape 2. An id over 200 is shape 1 in colour
    /// id - 200. Lights are drawn only while the personal light is below the overall level (UseLights, GameScene.cs:198-202).
    /// </summary>
    public static class LightSources
    {
        /// <summary>TazUO's LightsLoader.MAX_LIGHTS_DATA_INDEX_COUNT: shapes past it are not drawn.</summary>
        public const int MaxShape = 100;

        public static bool IsLight(TileFlags flags) => (flags & TileFlags.LightSource) != 0;

        private static bool FieldShape(int graphic) =>
            graphic >= 0x3E02 && graphic <= 0x3E0B || graphic >= 0x3914 && graphic <= 0x3929 || graphic == 0x0B1D;

        /// <summary>TazUO's ItemView: a LightSource item, or a field graphic, adds a light (ItemView.cs:31-38).</summary>
        public static bool ItemGivesLight(int graphic, TileFlags flags) =>
            IsLight(flags) || graphic >= 0x3E02 && graphic <= 0x3E0B || graphic >= 0x3914 && graphic <= 0x3929;

        /// <summary>
        /// The shape and colour a light draws with: <paramref name="id"/> is the item's light byte (ground item) or the
        /// tiledata Layer (static, multi part). Null when it draws nothing (shape past <see cref="MaxShape"/>).
        /// </summary>
        public static (int Shape, int Color)? Resolve(int graphic, int id)
        {
            int shape = FieldShape(graphic) ? 2 : id, color = 0;
            if (shape > 200)
            {
                color = shape - 200;
                shape = 1;
            }
            if (ColorFor(graphic) is int c)
                color = c;
            if (shape >= MaxShape)
                return null;
            return (shape, color);
        }

        /// <summary>LightColors.GetHue's built-in table (the data-file overrides are not shipped with the client).</summary>
        public static int? ColorFor(int id)
        {
            int color = -1;
            switch (id)
            {
                case 0x088C: color = 31; break;
                case 0x0FAC: color = 30; break;
                case 0x0FB1: color = 60; break;
                case 0x1647: color = 61; break;
                case 0x19BB:
                case 0x1F2B: color = 40; break;
                case 0x9F66: color = 0; break;
            }
            if (In(id, 0x09FB, 0x0A14)) color = 30;
            else if (In(id, 0x0A15, 0x0A29) || In(id, 0x0B1A, 0x0B1F) || In(id, 0x0B20, 0x0B25) || In(id, 0x0B26, 0x0B28)) color = 0;
            else if (In(id, 0x0DE1, 0x0DEA)) color = 31;
            else if (In(id, 0x1849, 0x1850) || In(id, 0x1853, 0x185A)) color = 61;
            else if (In(id, 0x197A, 0x19A9) || In(id, 0x19AB, 0x19B6)) color = 60;
            else if (In(id, 0x1ECD, 0x1ED2)) color = 1;
            if (id == 0x1FD4 || id == 0x0F6C)
                color = 2;
            if (In(id, 0x0E2D, 0x0E30)) color = 62;
            else if (In(id, 0x0E31, 0x0E33)) color = 40;
            else if (In(id, 0x0E5C, 0x0E6A)) color = 6;
            else if (In(id, 0x12EE, 0x134D) || In(id, 0x306A, 0x329B) || In(id, 0x343B, 0x346C) || In(id, 0x3547, 0x354C)) color = 31;
            else if (In(id, 0x3914, 0x3929)) color = 1;
            else if (In(id, 0x3946, 0x3964) || In(id, 0x3967, 0x397A)) color = 6;
            else if (In(id, 0x398C, 0x399F)) color = 31;
            else if (In(id, 0x3E02, 0x3E0B)) color = 1;
            else if (In(id, 0x3E27, 0x3E3A)) color = 31;
            else if (id == 0x40FE) color = 40;
            else if (id == 0x40FF) color = 10;
            else if (id == 0x4100) color = 20;
            else if (id == 0x4101) color = 32;
            else if (In(id, 0x983B, 0x983D) || In(id, 0x983F, 0x9841)) color = 30;
            return color < 0 ? (int?)null : color;
        }

        private static bool In(int id, int lo, int hi) => id >= lo && id <= hi;

        /// <summary>
        /// A light colour's RGB (0-1), LightColors.MakeDefaultShaders' base colours (its per-channel curves are not modelled);
        /// 0 and the rest are white.
        /// </summary>
        public static (float R, float G, float B) Rgb(int color)
        {
            uint c = color switch
            {
                1 => 0x00FF00, 2 => 0x7F7FFF, 6 => 0xFF00FF, 10 => 0x3F3FFF, 20 => 0x00FF00, 30 => 0xFF7F00, 31 => 0xFF7F00,
                32 => 0xFF00FF, 40 => 0xFF0000, 50 => 0xFFFF00, 60 => 0xFFFF00, 61 => 0xFFFF00, _ => 0xFFFFFF
            };
            return (((c >> 16) & 0xFF) / 255f, ((c >> 8) & 0xFF) / 255f, (c & 0xFF) / 255f);
        }

        /// <summary>UseLights: item and static lights show only while the personal light is below the overall level.</summary>
        public static bool LightsOn(int overall, int personal) => personal < overall;

        /// <summary>
        /// The darkness the scene is lit at: TazUO's IsometricLevel = max(personal, 32 - overall) / 32, i.e. an overall level
        /// of min(overall, 32 - personal). Night Sight (0x4E) brightens without making the world full-bright.
        /// </summary>
        public static int EffectiveLevel(int overall, int personal) => Math.Min(overall, 32 - Math.Max(0, personal));

        /// <summary>
        /// The <paramref name="max"/> candidates nearest (<paramref name="px"/>, <paramref name="py"/>) within
        /// <paramref name="range"/> tiles, nearest first (ties by order given); reuses <paramref name="into"/>.
        /// </summary>
        public static void Nearest<T>(IReadOnlyList<T> candidates, Func<T, (int X, int Y)> at, int px, int py, int range, int max, List<T> into)
        {
            into.Clear();
            var keyed = new List<(int D, int I)>();
            for (int i = 0; i < candidates.Count; i++)
            {
                (int x, int y) = at(candidates[i]);
                int d = Math.Max(Math.Abs(x - px), Math.Abs(y - py));
                if (d <= range)
                    keyed.Add((d, i));
            }
            keyed.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.I.CompareTo(b.I));
            for (int k = 0; k < keyed.Count && k < max; k++)
                into.Add(candidates[keyed[k].I]);
        }
    }
}
