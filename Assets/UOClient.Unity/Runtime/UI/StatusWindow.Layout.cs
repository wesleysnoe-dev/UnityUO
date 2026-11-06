using UOClient.Core;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project and gumppreview compile this file on their own to check and draw the layout.

    /// <summary>
    /// TazUO's StatusGumpOld (StatusGump.cs:282-631): the classic 0x0802 status scroll. Labels are font 1 in hue 0x0386;
    /// the stat locks cycle up / down / locked on click (ChangeStatLock, 0xBF 0x1A).
    /// </summary>
    public sealed partial class StatusWindow
    {
        public const int Background = 0x0802;
        public const int LabelHue = 0x0386;

        /// <summary>StatusGumpBase's lock gumps (StatusGump.cs:29-31), by lock state 0 up, 1 down, 2 locked.</summary>
        public const int LockUp = 0x0984, LockDown = 0x0986, LockLocked = 0x082C;

        public static int LockGump(byte state) => state switch { 0 => LockUp, 1 => LockDown, 2 => LockLocked, _ => -1 };

        /// <summary>The next lock in TazUO's cycle: (lock + 1) % 3.</summary>
        public static byte NextLock(byte state) => (byte)((state + 1) % 3);

        /// <summary>The locks' x: 28 with gumpartLegacyMUL.uop art, 40 with the old .mul (StatusGump.cs:318); their y per stat.</summary>
        public static int LockX(bool uopGumps) => uopGumps ? 28 : 40;

        public static readonly int[] LockY = { 62, 74, 86 };

        /// <summary>Where a click (barely dragged) turns the status into our health bar: from (244, 112) to the corner (StatusGump.cs:290-291, :109).</summary>
        public const int BarCornerX = 244, BarCornerY = 112;

        public static (int X, int Y) NamePos => (86, 42);

        /// <summary>The buff button (StatusGump.cs:304-315, from CV_5020): 0x7538, pressed / over 0x7539, at (20, 42).</summary>
        public const int BuffButton = 0x7538, BuffButtonPressed = 0x7539, BuffButtonX = 20, BuffButtonY = 42;

        /// <summary>One label: where it goes and what it says.</summary>
        public readonly struct Label
        {
            public readonly int X, Y;
            public readonly string Text;
            public Label(int x, int y, string text) { X = x; Y = y; Text = text; }
        }

        /// <summary>The ten value labels, as StatusGumpOld places them (left column x 86, right column x 171, rows 12 apart).</summary>
        public static Label[] Labels(Mobile p) => new[]
        {
            new Label(86, 62, p.Str.ToString()),
            new Label(86, 74, p.Dex.ToString()),
            new Label(86, 86, p.Int.ToString()),
            new Label(86, 98, p.Female ? "Female" : "Male"),
            new Label(86, 110, p.Armor.ToString()),
            new Label(171, 62, $"{p.Hits}/{p.HitsMax}"),
            new Label(171, 74, $"{p.Mana}/{p.ManaMax}"),
            new Label(171, 86, $"{p.Stamina}/{p.StaminaMax}"),
            new Label(171, 98, p.Gold.ToString()),
            new Label(171, 110, $"{p.Weight}/{p.MaxWeight}"),
        };

        /// <summary>The player's locks by stat (0 str, 1 dex, 2 int).</summary>
        public static byte LockOf(Mobile p, int stat) => stat == 0 ? p.StrLock : stat == 1 ? p.DexLock : p.IntLock;
    }
}
