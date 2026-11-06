using UOClient.Core;
using UOClient.Net;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's StatusGumpModern (StatusGump.cs:634-1445), the status AddStatusGump opens from CV_308Z (AoS) on in the
    /// default Standard style: the 0x2A6C background with the name, stats and their locks, hits / stamina / mana over
    /// their maxima, stat cap, luck, weight, damage, gold, followers and the five resists. Labels are font 1 in hue 0x0386;
    /// the stat locks cycle up / down / locked on click (ChangeStatLock, 0xBF 0x1A).
    /// <para>Our 7.0.23.1 is below CV_70300 (7.0.30.0), so TazUO lays out the plain (non-extended) set, not the one
    /// with hit / defence chance, lower mana / reagent cost, swing speed and casting (0x11 version 6's extra block,
    /// which ModernUO sends but we neither read nor need for this client).</para>
    /// </summary>
    public sealed partial class StatusWindow
    {
        /// <summary>StatusGumpModern.Settings.Graphic_Background, Hue_Text and Hue_CharacterName.</summary>
        public const int Background = 0x2A6C;
        public const int LabelHue = 0x0386;

        /// <summary>CV_308Z (3.0.8z): StatusGumpModern rather than StatusGumpOld (AddStatusGump, StatusGump.cs:179).</summary>
        public static readonly ClientVersion ModernFrom = new ClientVersion(3, 0, 8, 'z');

        /// <summary>CV_70300 (7.0.30.0): the extended status (useExtendedStatus, StatusGump.cs:643).</summary>
        public static readonly ClientVersion ExtendedFrom = new ClientVersion(7, 0, 30, 0);

        /// <summary>CV_5020 (5.0.2.0): the buff button (StatusGump.cs:665).</summary>
        public static readonly ClientVersion BuffButtonFrom = new ClientVersion(5, 0, 2, 0);

        public static bool IsModern(ClientVersion v) => v >= ModernFrom;
        public static bool IsExtended(ClientVersion v) => v >= ExtendedFrom;
        public static bool HasBuffButton(ClientVersion v) => v >= BuffButtonFrom;

        /// <summary>StatusGumpModern.Settings' lock gumps, by lock state 0 up, 1 down, 2 locked.</summary>
        public const int LockUp = 0x0984, LockDown = 0x0986, LockLocked = 0x082C;

        public static int LockGump(byte state) => state switch { 0 => LockUp, 1 => LockDown, 2 => LockLocked, _ => -1 };

        /// <summary>The next lock in TazUO's cycle: (lock + 1) % 3.</summary>
        public static byte NextLock(byte state) => (byte)((state + 1) % 3);

        /// <summary>The locks' x (40; 28 only in the extended status) and their y per stat (StatusGump.cs:679-724).</summary>
        public const int LockX = 40;

        public static readonly int[] LockY = { 76, 102, 132 };

        /// <summary>
        /// Where a click (barely dragged) turns the status into our health bar: from (389, 152) on, where StatusGumpModern
        /// puts its 16x16 minimize hit box (StatusGump.cs:649-650, :1429-1444; StatusGumpBase.OnMouseUp :109).
        /// </summary>
        public const int BarCornerX = 389, BarCornerY = 152;

        /// <summary>The name: centred in 320 pixels from (58, 50) (StatusGump.cs:653-662).</summary>
        public const int NameX = 58, NameY = 50, NameWidth = 320;

        /// <summary>The buff button (StatusGump.cs:665-676, from CV_5020): 0x7538, pressed / over 0x7539, at (40, 50).</summary>
        public const int BuffButton = 0x7538, BuffButtonPressed = 0x7539, BuffButtonX = 40, BuffButtonY = 50;

        /// <summary>One label: where it goes, what it says, and the width it is centred in (0: left-aligned at X).</summary>
        public readonly struct Label
        {
            public readonly int X, Y, Width;
            public readonly string Text;
            public Label(int x, int y, string text, int width = 0) { X = x; Y = y; Text = text; Width = width; }

            /// <summary>Where text <paramref name="textWidth"/> wide starts: X, or centred in Width as TS_CENTER does.</summary>
            public int TextX(int textWidth) => Width > 0 ? X + (Width - textWidth) / 2 : X;
        }

        /// <summary>One of the 1-pixel 0xFF383838 rules between a current value and its maximum.</summary>
        public readonly struct Rule
        {
            public readonly int X, Y, Width;
            public Rule(int x, int y, int width) { X = x; Y = y; Width = width; }
        }

        /// <summary>The rules' colour (TazUO's Line 0xFF383838).</summary>
        public const uint RuleArgb = 0xFF383838;

        /// <summary>
        /// The value labels as StatusGumpModern places them without the extended status: str / dex / int at x 88;
        /// hits, stamina and mana over their maxima centred in 40 at x 141; stat cap and luck at x 220; weight over its
        /// maximum centred in 40 at x 210; damage, gold and followers at x 280; the resists at x 354. Physical resist is
        /// the 0x11 armour field (TazUO's PhysicalResistance). Followers reads "n/max" once Update refreshes it.
        /// </summary>
        public static Label[] Labels(Mobile p) => new[]
        {
            new Label(88, 77, p.Str.ToString()),
            new Label(88, 105, p.Dex.ToString()),
            new Label(88, 133, p.Int.ToString()),
            new Label(141, 70, p.Hits.ToString(), 40),
            new Label(141, 83, p.HitsMax.ToString(), 40),
            new Label(141, 98, p.Stamina.ToString(), 40),
            new Label(141, 111, p.StaminaMax.ToString(), 40),
            new Label(141, 126, p.Mana.ToString(), 40),
            new Label(141, 139, p.ManaMax.ToString(), 40),
            new Label(220, 77, p.StatCap.ToString()),
            new Label(220, 105, p.Luck.ToString()),
            new Label(210, 126, p.Weight.ToString(), 40),
            new Label(210, 139, p.MaxWeight.ToString(), 40),
            new Label(280, 77, $"{p.DamageMin}-{p.DamageMax}"),
            new Label(280, 105, p.Gold.ToString()),
            new Label(280, 133, $"{p.Followers}/{p.FollowersMax}"),
            new Label(354, 76, p.Armor.ToString()),
            new Label(354, 92, p.FireResist.ToString()),
            new Label(354, 106, p.ColdResist.ToString()),
            new Label(354, 120, p.PoisonResist.ToString()),
            new Label(354, 134, p.EnergyResist.ToString()),
        };

        /// <summary>The rules under hits, stamina and mana (x 146 to 185) and under weight (x 216 to 250) (StatusGump.cs:905-939, :1023-1035).</summary>
        public static readonly Rule[] Rules =
        {
            new Rule(146, 138, 39), new Rule(146, 110, 39), new Rule(146, 82, 39), new Rule(216, 138, 34),
        };

        /// <summary>The player's locks by stat (0 str, 1 dex, 2 int).</summary>
        public static byte LockOf(Mobile p, int stat) => stat == 0 ? p.StrLock : stat == 1 ? p.DexLock : p.IntLock;
    }
}
