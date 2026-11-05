using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's classic HealthBarGump (HealthBarGump.cs:1681-1945 build, :1947-2273 update, Settings :2298-2323), the
    /// non-party style. Party bars (0x0028 / 0x0029 lines, heal buttons) are not built here.
    /// </summary>
    public sealed partial class HealthBarWindow
    {
        public const int BackgroundNormal = 0x0803, BackgroundWar = 0x0807, BackgroundOther = 0x0804;
        public const int LineRed = 0x0805, LineBlue = 0x0806, LinePoisoned = 0x0808, LineYellowHits = 0x0809;

        /// <summary>The bar's full width in pixels (Update's barW for a non-party bar).</summary>
        public const int BarWidth = 109;

        /// <summary>Settings.Hue_Text and Hue_Text_Renamable: the name over another mobile's bar.</summary>
        public const int NameHue = 0x0386, NameHueRenamable = 0x000E;

        /// <summary>Our own bar's three lines (hits, mana, stamina): red under, blue over (BuildGump :1808-1847).</summary>
        public static readonly (int X, int Y)[] PlayerLines = { (34, 12), (34, 25), (34, 38) };

        /// <summary>Another mobile's single hits line and its name box (BuildGump :1870-1935, StbTextBox 120 wide).</summary>
        public const int OtherLineX = 34, OtherLineY = 38, NameX = 16, NameY = 14, NameWidth = 120;

        /// <summary>The window key: one bar per serial, so opening another for the same mobile replaces it.</summary>
        public static string KeyFor(uint serial) => "healthbar:" + serial.ToString("X8");

        /// <summary>
        /// BaseHealthBarGump.CalculatePercents: current/max as a whole percent (capped at 100), scaled to the bar width
        /// once above 1%. A max of 0 gives 0; a 1% or lower result stays the raw percent (0 or 1 pixels).
        /// </summary>
        public static int Percent(int max, int current, int width)
        {
            if (max > 0)
            {
                max = current * 100 / max;
                if (max > 100)
                    max = 100;
                if (max > 1)
                    max = width * max / 100;
            }
            return max;
        }

        /// <summary>Our own bar's background: war or peace (Background_War / Background_Normal); another mobile's is 0x0804.</summary>
        public static int Background(bool self, bool warMode) => self ? (warMode ? BackgroundWar : BackgroundNormal) : BackgroundOther;

        /// <summary>
        /// TazUO's Notoriety.GetHue with the profile's default hues (Profile.cs:156-162): innocent 0x5A, ally 0x44,
        /// gray and criminal 0x3B2, enemy 0x31, murderer 0x23, invulnerable 0x34; anything else 0.
        /// </summary>
        public static int NotorietyHue(byte notoriety) => notoriety switch
        {
            1 => 0x005A,
            2 => 0x0044,
            3 => 0x03B2,
            4 => 0x03B2,
            5 => 0x0031,
            6 => 0x0023,
            7 => 0x0034,
            _ => 0
        };

        /// <summary>
        /// The background's hue (Update :1968, :2153). In range: none for ourselves, gray or criminal, else the
        /// notoriety hue. Out of range (no mobile) TazUO leaves its initial value, Settings.Background_Normal (0x0803),
        /// as a hue; kept as it draws.
        /// </summary>
        public static int BackgroundHue(bool self, bool inRange, byte notoriety)
        {
            if (!inRange)
                return BackgroundNormal;
            if (self || notoriety == 3 || notoriety == 4)
                return 0;
            return NotorietyHue(notoriety);
        }

        /// <summary>
        /// The hits line's gump: poisoned 0x0808, else yellow (blessed) 0x0809, else blue 0x0806. TazUO also sets hue 63
        /// / 353 on the frame it switches, but resets the bar to hue 0 on the next Update (hpForegroundHue starts at
        /// Hue_Bar_HP_Foreground_Normal each time, :1969, :2252), so the drawn bar is the unhued gump.
        /// </summary>
        public static int HitsLine(bool poisoned, bool yellow) => poisoned ? LinePoisoned : yellow ? LineYellowHits : LineBlue;

        /// <summary>
        /// Where a bar opened by dragging a mobile goes: centered on the press (GameSceneInputHandler.OnMouseDragging
        /// :1326-1334, LClickPosition minus half the 0x0804 gump's size).
        /// </summary>
        public static (float X, float Y) CenteredOn(float x, float y, int width, int height) => (x - (width >> 1), y - (height >> 1));

        /// <summary>What one bar shows this frame, from the world alone.</summary>
        public readonly struct State
        {
            public readonly bool Self, InRange, Renamable, HitsMaxKnown;
            public readonly int Background, BackgroundHue, HitsLine, NameHue;
            public readonly int Hits, Mana, Stamina; // filled widths in pixels
            public readonly string Name;

            public State(bool self, bool inRange, bool renamable, bool hitsMaxKnown, int background, int backgroundHue, int hitsLine,
                         int nameHue, int hits, int mana, int stamina, string name)
            {
                Self = self;
                InRange = inRange;
                Renamable = renamable;
                HitsMaxKnown = hitsMaxKnown;
                Background = background;
                BackgroundHue = backgroundHue;
                HitsLine = hitsLine;
                NameHue = nameHue;
                Hits = hits;
                Mana = mana;
                Stamina = stamina;
                Name = name;
            }

            /// <summary>The red line under the hits shows only once the mobile has a known maximum (Update :2061).</summary>
            public bool ShowsRedLine => InRange && HitsMaxKnown;
        }

        /// <summary>
        /// Update's per-frame state for <paramref name="serial"/>: <paramref name="m"/> null means out of range (bars hidden,
        /// last name kept). Mana and stamina are only filled for our own bar.
        /// </summary>
        public static State Compute(uint serial, uint player, bool warMode, Mobile m, string lastName)
        {
            bool self = serial == player;
            bool inRange = m != null;
            bool renamable = inRange && !self && m.Renamable;
            string name = inRange && !string.IsNullOrEmpty(m.Name) ? m.Name : lastName;
            int hits = inRange ? Percent(m.HitsMax, m.Hits, BarWidth) : 0;
            int mana = inRange && self ? Percent(m.ManaMax, m.Mana, BarWidth) : 0;
            int stam = inRange && self ? Percent(m.StaminaMax, m.Stamina, BarWidth) : 0;
            return new State(self, inRange, renamable, inRange && m.HitsMax > 0, Background(self, warMode),
                BackgroundHue(self, inRange, m?.Notoriety ?? 0), HitsLine(inRange && m.Poisoned, inRange && m.YellowHits),
                renamable ? NameHueRenamable : NameHue, hits, mana, stam, name ?? "");
        }
    }
}
