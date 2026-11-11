using System;
using System.Collections.Generic;
using System.Globalization;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own.

    /// <summary>What the info bar can show (TazUO's InfoBarVars, the ones this client has the numbers for).</summary>
    public enum InfoVar
    {
        Hits, Mana, Stamina, Weight, Followers, Gold, Damage, Armor, Luck,
        FireResist, ColdResist, PoisonResist, EnergyResist, StatsCap, TithingPoints, Strength, Dexterity, Intelligence, Name
    }

    /// <summary>One entry of the info bar: a label, what it shows and the hue of its text.</summary>
    public struct InfoBarItem
    {
        public string Label;
        public InfoVar Var;
        public int Hue;
    }

    /// <summary>The player's numbers the info bar reads (the status packet's).</summary>
    public struct InfoStats
    {
        public string Name;
        public int Hits, HitsMax, Mana, ManaMax, Stamina, StaminaMax, Weight, MaxWeight, Followers, FollowersMax, Gold;
        public int DamageMin, DamageMax, Armor, Luck, FireResist, ColdResist, PoisonResist, EnergyResist, StatCap, Tithe, Str, Dex, Int;
    }

    /// <summary>One cell of the counter bar: an item kind (graphic and hue), or empty.</summary>
    public struct CounterCell
    {
        public int Graphic, Hue;
        public bool Empty => Graphic <= 0;
    }

    /// <summary>The rules of the info bar and the counter bar (TazUO's InfoBarGump and CounterBarGump).</summary>
    public static class HudBars
    {
        // ---- info bar ----

        public const int MaxInfoBarItems = 16, MaxInfoLabelLength = 16;

        /// <summary>TazUO's default info bar: hits, mana, stamina and weight in their usual hues.</summary>
        public const string DefaultInfoBar = "Hits|Hits|33\nMana|Mana|88\nStam|Stamina|53\nWeight|Weight|946";

        public const int HighlightTextColour = 0, HighlightBars = 1;

        /// <summary>The entries in a saved list ("Label|Var|Hue" a line); lines that are not one are left out.</summary>
        public static List<InfoBarItem> ParseInfoBar(string list)
        {
            var items = new List<InfoBarItem>();
            foreach (string line in (list ?? "").Split('\n', '\r'))
            {
                string[] parts = line.Split('|');
                if (parts.Length != 3 || items.Count >= MaxInfoBarItems || !Enum.TryParse(parts[1].Trim(), true, out InfoVar v)
                    || !Enum.IsDefined(typeof(InfoVar), v) || !int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hue))
                    continue;
                string label = parts[0].Trim();
                if (label.Length > MaxInfoLabelLength)
                    label = label.Substring(0, MaxInfoLabelLength).Trim();
                items.Add(new InfoBarItem { Label = label, Var = v, Hue = GameOptions.ClampHue(hue) });
            }
            return items;
        }

        public static string FormatInfoBar(IEnumerable<InfoBarItem> items)
        {
            var lines = new List<string>();
            foreach (InfoBarItem i in items)
                lines.Add((i.Label ?? "").Replace("|", "").Replace("\n", "").Trim() + "|" + i.Var + "|" + i.Hue.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines);
        }

        /// <summary>The value's text, as TazUO's InfoBarGump.GetVarData writes it.</summary>
        public static string InfoText(InfoVar v, InfoStats s)
        {
            switch (v)
            {
                case InfoVar.Hits: return s.Hits + "/" + s.HitsMax;
                case InfoVar.Mana: return s.Mana + "/" + s.ManaMax;
                case InfoVar.Stamina: return s.Stamina + "/" + s.StaminaMax;
                case InfoVar.Weight: return s.Weight + "/" + s.MaxWeight;
                case InfoVar.Followers: return s.Followers + "/" + s.FollowersMax;
                case InfoVar.Gold: return s.Gold.ToString("N0", CultureInfo.InvariantCulture);
                case InfoVar.Damage: return s.DamageMin + "-" + s.DamageMax;
                case InfoVar.Armor: return s.Armor.ToString(CultureInfo.InvariantCulture);
                case InfoVar.Luck: return s.Luck.ToString(CultureInfo.InvariantCulture);
                case InfoVar.FireResist: return s.FireResist.ToString(CultureInfo.InvariantCulture);
                case InfoVar.ColdResist: return s.ColdResist.ToString(CultureInfo.InvariantCulture);
                case InfoVar.PoisonResist: return s.PoisonResist.ToString(CultureInfo.InvariantCulture);
                case InfoVar.EnergyResist: return s.EnergyResist.ToString(CultureInfo.InvariantCulture);
                case InfoVar.StatsCap: return s.StatCap.ToString(CultureInfo.InvariantCulture);
                case InfoVar.TithingPoints: return s.Tithe.ToString(CultureInfo.InvariantCulture);
                case InfoVar.Strength: return s.Str.ToString(CultureInfo.InvariantCulture);
                case InfoVar.Dexterity: return s.Dex.ToString(CultureInfo.InvariantCulture);
                case InfoVar.Intelligence: return s.Int.ToString(CultureInfo.InvariantCulture);
                default: return s.Name ?? "";
            }
        }

        /// <summary>How full a value is, 0-1, for the ones with a maximum; null for the rest (no bar, no warning).</summary>
        public static float? InfoFraction(InfoVar v, InfoStats s)
        {
            (int now, int max) = v == InfoVar.Hits ? (s.Hits, s.HitsMax) : v == InfoVar.Mana ? (s.Mana, s.ManaMax)
                : v == InfoVar.Stamina ? (s.Stamina, s.StaminaMax) : v == InfoVar.Weight ? (s.Weight, s.MaxWeight) : (0, -1);
            if (max < 0)
                return null;
            return max == 0 ? 0f : Math.Max(0f, Math.Min(1f, now / (float)max));
        }

        /// <summary>
        /// 0 fine, 1 a warning (orange), 2 danger (red): hits, mana and stamina at half and at a quarter; weight the other
        /// way round, at 80% and when full.
        /// </summary>
        public static int InfoLevel(InfoVar v, InfoStats s)
        {
            if (!(InfoFraction(v, s) is float f))
                return 0;
            if (v == InfoVar.Weight)
                return f >= 1f ? 2 : f >= 0.8f ? 1 : 0;
            return f <= 0.25f ? 2 : f <= 0.5f ? 1 : 0;
        }

        // ---- counter bar ----

        public const int MinCounterCell = 30, MaxCounterCell = 80, MaxCounterColumns = 20, MaxCounterRows = 5;

        /// <summary>The cells in a saved list ("graphic:hue" a line, "-" for an empty cell).</summary>
        public static List<CounterCell> ParseCounters(string list)
        {
            var cells = new List<CounterCell>();
            foreach (string line in (list ?? "").Split('\n', '\r'))
            {
                if (cells.Count >= MaxCounterColumns * MaxCounterRows)
                    break;
                string t = line.Trim();
                if (t.Length == 0)
                    continue;
                string[] parts = t.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int g) && g > 0 && g <= 0xFFFF
                    && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h >= 0 && h <= 0xFFFF)
                    cells.Add(new CounterCell { Graphic = g, Hue = h });
                else
                    cells.Add(new CounterCell());
            }
            while (cells.Count > 0 && cells[cells.Count - 1].Empty)
                cells.RemoveAt(cells.Count - 1); // empty cells at the end are not kept
            return cells;
        }

        public static string FormatCounters(IEnumerable<CounterCell> cells)
        {
            var lines = new List<string>();
            foreach (CounterCell c in cells)
                lines.Add(c.Empty ? "-" : c.Graphic.ToString(CultureInfo.InvariantCulture) + ":" + c.Hue.ToString(CultureInfo.InvariantCulture));
            return string.Join("\n", lines);
        }

        /// <summary>The count under a cell: whole, or from <paramref name="threshold"/> up in thousands ("12K") with the option on.</summary>
        public static string CounterText(int amount, bool abbreviate, int threshold) =>
            abbreviate && amount >= Math.Max(1000, threshold) ? (amount / 1000).ToString(CultureInfo.InvariantCulture) + "K"
                                                             : amount.ToString(CultureInfo.InvariantCulture);

        /// <summary>The count is drawn in red: the option is on and fewer than <paramref name="below"/> are left.</summary>
        public static bool CounterIsLow(int amount, bool highlight, int below) => highlight && amount < below;

        /// <summary>The bar's size for its cells (a 4-pixel frame, a pixel between cells).</summary>
        public static (int Width, int Height) CounterBarSize(int cell, int columns, int rows) =>
            (8 + columns * cell + (columns - 1), 8 + rows * cell + (rows - 1));

        /// <summary>The cell at a window point, or -1.</summary>
        public static int CounterCellAt(float x, float y, int cell, int columns, int rows)
        {
            if (x < 4 || y < 4)
                return -1;
            int cx = (int)((x - 4) / (cell + 1)), cy = (int)((y - 4) / (cell + 1));
            if (cx >= columns || cy >= rows || x - 4 - cx * (cell + 1) >= cell || y - 4 - cy * (cell + 1) >= cell)
                return -1;
            return cy * columns + cx;
        }
    }
}
