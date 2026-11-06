using System;

namespace UOClient.Unity.UI
{
    // No UnityEngine here: the test project compiles this file on its own to check the layout and text.

    /// <summary>
    /// TazUO's classic BuffGump (BuffGump.cs) and BuffDebuff.Receive (BuffDebuff.cs): the icon art by BuffTable, the four
    /// backgrounds and where each places its icons, the timer text drawn on an icon (Profile.BuffBarTime), the tooltip
    /// (title, description, then the time left), and the fade of a timed icon's last 10 seconds.
    /// </summary>
    public sealed partial class BuffBarWindow
    {
        /// <summary>The backgrounds the button cycles through (OnButtonClick), and the one a new gump opens with (0x7580).</summary>
        public const int FirstBackground = 0x757F, LastBackground = 0x7582, DefaultBackground = 0x7580;

        /// <summary>The direction button: normal, then pressed / over (Button 0x7585, 0x7589, 0x7589).</summary>
        public const int ButtonNormal = 0x7585, ButtonPressed = 0x7589;

        /// <summary>Where a new gump opens: StatusGump's buff button and the macro both use (100, 100).</summary>
        public const int OpenX = 100, OpenY = 100;

        /// <summary>Icons are 31 px apart along the bar (UpdateElements).</summary>
        public const int IconStep = 31;

        /// <summary>BUFF_ICON_START and BUFF_ICON_START_NEW: the server's icon ids map to table rows from these.</summary>
        public const int IconStart = 0x03E9, IconStartNew = 0x0466;

        /// <summary>The next background in the button's cycle (0x757F after 0x7582).</summary>
        public static int NextBackground(int graphic) => graphic + 1 > LastBackground ? FirstBackground : graphic + 1;

        /// <summary>The direction button's place on a background (BuildGump).</summary>
        public static (int X, int Y) ButtonPos(int background) => background switch
        {
            0x7580 => (-2, 36), // left, horizontal
            0x7581 => (34, 78), // right, vertical
            0x7582 => (76, 36), // right, horizontal
            _ => (0, 0),        // 0x757F left, vertical
        };

        /// <summary>The n-th icon's place on a background of the given size (UpdateElements).</summary>
        public static (int X, int Y) IconPos(int background, int n, int width, int height)
        {
            int offset = n * IconStep;
            return background switch
            {
                0x7580 => (26 + offset, 5),
                0x7581 => (5, height - 48 - offset),
                0x7582 => (width - 48 - offset, 5),
                _ => (25, 26 + offset),
            };
        }

        /// <summary>BuffDebuff.Receive's row for an icon id: from 0x466 on, 125 + (id - 0x466); before it, id - 0x3E9.</summary>
        public static int TableRow(int icon) => icon >= IconStartNew ? icon - (IconStartNew - 125) : icon - IconStart;

        /// <summary>
        /// The icon's gump art from TazUO's default BuffTable (no Data/Client/buff.txt), or -1 when TazUO would not show
        /// it (its row is past the table, so Receive drops the packet). A 0 entry is kept: TazUO adds it with no art.
        /// </summary>
        public static int Graphic(int icon)
        {
            int row = TableRow(icon);
            return row >= 0 && row < Table.Length ? Table[row] : -1;
        }

        /// <summary>
        /// The timer text over a timed icon (Update): "+{h}hr" from an hour, "m:ss" from a minute, else "ss" and "s";
        /// "" when untimed or already run out (TazUO stops updating it then).
        /// </summary>
        public static string TimerText(double secondsLeft)
        {
            if (secondsLeft <= 0)
                return "";
            TimeSpan span = TimeSpan.FromSeconds(secondsLeft); // whole seconds, cut down as TimeSpan.FromMilliseconds is
            if (span.Hours > 0)
                return $"+{span.Hours}hr";
            return span.Minutes > 0 ? $"{span.Minutes}:{span.Seconds:00}" : $"{span.Seconds:00}s";
        }

        /// <summary>
        /// The tooltip (Receive's text, then Update's "time_left" line while time is left): the title, the description and
        /// under it, each capitalized as Clilocs.Translate does, inside &lt;left&gt; (ModernUO's third cliloc is always 0, so
        /// TazUO's third line never shows). <paramref name="secondsLeft"/> below 0 means untimed.
        /// </summary>
        public static string TooltipText(string title, string description, double secondsLeft)
        {
            string text = Capitalize(title ?? "");
            if (!string.IsNullOrEmpty(description))
                text += "\n" + Capitalize(description);
            text = $"<left>{text}</left>";
            if (secondsLeft > 0)
            {
                TimeSpan span = TimeSpan.FromSeconds(secondsLeft);
                text += $"\nTime left: {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
            }
            return text;
        }

        private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        /// <summary>
        /// A timed icon's alpha in its last 10 seconds (Update): each frame it moves (10000 - ms left) / 600 toward 60 or
        /// 255, turning at each end, so it pulses faster as it runs out. Returns the new alpha and direction. With 10
        /// seconds or more left (or no timer) it is a new entry's full alpha, so a recast that extends the timer stops
        /// pulsing; once run out it holds where it was.
        /// </summary>
        public static (byte Alpha, bool Decreasing) Fade(byte alpha, bool decreasing, double secondsLeft)
        {
            int ms = (int)(secondsLeft * 1000);
            if (secondsLeft < 0 || ms >= 10000)
                return (255, true);
            if (ms <= 0)
                return (alpha, decreasing);
            int a = alpha, step = (10000 - ms) / 600;
            if (decreasing)
            {
                a -= step;
                if (a <= 60)
                    return (60, false);
            }
            else
            {
                a += step;
                if (a >= 255)
                    return (255, true);
            }
            return ((byte)a, decreasing);
        }

        /// <summary>TazUO's BuffTable._defaultTable (BuffTable.cs), row by row.</summary>
        public static readonly ushort[] Table =
        {
            0x754C, 0x754A, 0x0000, 0x0000, 0x755E, 0x7549, 0x7551, 0x7556, 0x753A, 0x754D, 0x754E, 0x7565, 0x753B, 0x7543,
            0x7544, 0x7546, 0x755C, 0x755F, 0x7566, 0x7554, 0x7540, 0x7568, 0x754F, 0x7550, 0x7553, 0x753E, 0x755D, 0x7563,
            0x7562, 0x753F, 0x7559, 0x7557, 0x754B, 0x753D, 0x7561, 0x7558, 0x755B, 0x7560, 0x7541, 0x7545, 0x7552, 0x7569,
            0x7548, 0x755A, 0x753C, 0x7547, 0x7567, 0x7542, 0x758A, 0x758B, 0x758C, 0x758D, 0x0000, 0x758E, 0x094B, 0x094C,
            0x094D, 0x094E, 0x094F, 0x0950, 0x753E, 0x5011, 0x7590, 0x7591, 0x7592, 0x7593, 0x7594, 0x7595, 0x7596, 0x7598,
            0x7599, 0x759B, 0x759C, 0x759E, 0x759F, 0x75A0, 0x75A1, 0x75A3, 0x75A4, 0x75A5, 0x75A6, 0x75A7, 0x75C0, 0x75C1,
            0x75C2, 0x75C3, 0x75C4, 0x75F2, 0x75F3, 0x75F4, 0x75F5, 0x75F6, 0x75F7, 0x75F8, 0x75F9, 0x75FA, 0x75FB, 0x75FC,
            0x75FD, 0x75FE, 0x75FF, 0x7600, 0x7601, 0x7602, 0x7603, 0x7604, 0x7605, 0x7606, 0x7607, 0x7608, 0x7609, 0x760A,
            0x760B, 0x760C, 0x760D, 0x760E, 0x760F, 0x7610, 0x7611, 0x7612, 0x7613, 0x7614, 0x7615, 0x75C5, 0x75F6, 0x761B,
            // skill masteries
            0x9BC9, 0x9BB5, 0x9BDD, 0x9BC6, 0x9BCC, 0x9BBE, 0x9BBD, 0x9BCB, 0x9BC8, 0x9BBF, 0x9BCD, 0x9BC0, 0x9BCE, 0x9BC1,
            0x9BC7, 0x9BC2, 0x9BB7, 0x9BCA, 0x9BB6, 0x9BB8, 0x9BB9, 0x9BBA, 0x9BBB, 0x9BBC, 0x9BC3, 0x9BC4, 0x9BC5, 0x9BD2,
            0x9BD3, 0x9BD4, 0x9BD5, 0x9BD1, 0x9BD6, 0x9BD7, 0x9BCF, 0x9BD8, 0x9BD9, 0x9BDB, 0x9BDC, 0x9BDA, 0x9BD0, 0x9BDE,
            0x9BDF,
            0xC349, 0xC34D, 0xC34E, 0xC34C, 0xC34B, 0xC34A, 0xC343, 0xC345, 0xC346, 0xC347, 0xC348,
            0x9CDE,
            0x5DE1, 0x5DDF, 0x5DE3, 0x5DE5, 0x5DE4, 0x5DE6, 0x5D51,
            0x0951,
        };
    }
}
