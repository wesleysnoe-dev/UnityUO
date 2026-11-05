using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's classic HealthBarGump (HealthBarGump.cs:1681-1945 build, :1947-2273 update, Settings :2298-2323): the
    /// classic style and the party style (BuildGump :1689-1795) a party member's bar takes.
    /// </summary>
    public sealed partial class HealthBarWindow
    {
        // ---- party style (ShowPartyStyleBar) ----

        /// <summary>Settings.Line_Red_Party and Line_Blue_Party: the party bar's lines.</summary>
        public const int LineRedParty = 0x0028, LineBlueParty = 0x0029;

        /// <summary>The heal (0x0938) and cure (0x0939) buttons, both 0x093A pressed, at (0, 20) and (0, 33).</summary>
        public const int HealButton = 0x0938, CureButton = 0x0939, HealCurePressed = 0x093A;
        public const int HealButtonY = 20, CureButtonY = 33;

        /// <summary>The party bar: 115 x 55 over an invisible 0x0803 (Alpha 0), lines at x 18, 96 wide (Update's barW).</summary>
        public const int PartyWidth = 115, PartyHeight = 55, PartyLineX = 18, PartyBarWidth = 96;

        /// <summary>The party bar's hits, mana and stamina lines.</summary>
        public static readonly int[] PartyLineY = { 20, 33, 45 };

        /// <summary>The party name box: at (0, -2), 120 wide for ourselves and 109 for others.</summary>
        public const int PartyNameX = 0, PartyNameY = -2, PartyNameWidthSelf = 120, PartyNameWidthOther = 109;

        /// <summary>Our own party bar's name, TazLang "self" (language.ini:2476), never replaced by our real name (:2097).</summary>
        public const string PartySelfName = "[* SELF *]";

        /// <summary>What a party style bar shows this frame (TazUO's Update with inparty set).</summary>
        public readonly struct PartyState
        {
            public readonly bool Self, InRange, HitsMaxKnown;
            public readonly int NameHue, Hits, Mana, Stamina; // filled widths in pixels, of PartyBarWidth
            public readonly string Name;

            public PartyState(bool self, bool inRange, bool hitsMaxKnown, int nameHue, int hits, int mana, int stamina, string name)
            {
                Self = self;
                InRange = inRange;
                HitsMaxKnown = hitsMaxKnown;
                NameHue = nameHue;
                Hits = hits;
                Mana = mana;
                Stamina = stamina;
                Name = name;
            }

            /// <summary>Out of range the three bars and the heal / cure buttons hide (:2011-2049); in range they show (:2120-2138).</summary>
            public bool ShowsBarsAndButtons => InRange;

            /// <summary>The red line under the hits shows once the maximum is known (:2061); mana and stamina's always do.</summary>
            public bool ShowsHitsRedLine => InRange && HitsMaxKnown;
        }

        /// <summary>
        /// The party style's Update: everyone's hits, mana and stamina over 96 pixels, the name in the mobile's notoriety
        /// hue (:2141-2144) or Hue_Text 0x0386 out of range (the 912 out-of-range hue lasts one frame, as the classic
        /// style's colors do). Poisoned and yellow hits keep the 0x0029 line: the party style only changes the hue, which
        /// the next Update resets. Our own bar is named <see cref="PartySelfName"/>.
        /// </summary>
        public static PartyState ComputeParty(uint serial, uint player, Mobile m, string lastName)
        {
            bool self = serial == player;
            bool inRange = m != null;
            string name = self ? PartySelfName : inRange && !string.IsNullOrEmpty(m.Name) ? m.Name : lastName;
            int hits = inRange ? Percent(m.HitsMax, m.Hits, PartyBarWidth) : 0;
            int mana = inRange ? Percent(m.ManaMax, m.Mana, PartyBarWidth) : 0;
            int stam = inRange ? Percent(m.StaminaMax, m.Stamina, PartyBarWidth) : 0;
            return new PartyState(self, inRange, inRange && m.HitsMax > 0, inRange ? NotorietyHue(m.Notoriety) : NameHue,
                hits, mana, stam, name ?? "");
        }

        /// <summary>
        /// Whether a bar's name box can be edited to rename the mobile: <see cref="State.Renamable"/> (in view, not us, the
        /// 0x11 flag) with no target cursor up (TextBoxOnMouseUp targets first).
        /// </summary>
        public static bool CanRename(State s, bool targeting) => s.Renamable && !targeting;

        /// <summary>
        /// A classic bar's heal and cure buttons (BuildGump :1887-1911, Update :2134): TazUO's ShouldShowHealButtons with
        /// the profile defaults, which is only ShowHealCureButtonsPets (true; the friends and all-bars options are off).
        /// IsPet is a renamable mobile in view, not us, not in our party; the buttons sit at the party bar's places.
        /// </summary>
        public static bool ShowsPetHealButtons(State s, bool inParty) => s.Renamable && !inParty;

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
