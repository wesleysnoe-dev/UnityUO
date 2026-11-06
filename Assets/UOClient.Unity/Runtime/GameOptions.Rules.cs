using System;
using System.Globalization;
using System.Text;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>The behaviour behind the options, as TazUO decides it, kept apart from the code that uses it so it can be tested.</summary>
    public sealed partial class GameOptions
    {
        /// <summary>
        /// Seconds a line of overhead speech stays: TazUO's MessageManager.CalculateTimeToLive. Scaled: 4 s per "line"
        /// (1 up to 66 characters, 2 up to 99, else 3) times delay / 100, the delay at least 10. Not scaled:
        /// <c>(5497558140000 * delay) &gt;&gt; 32 &gt;&gt; 5</c> ms, which is delay x 40 ms.
        /// </summary>
        public static float SpeechSeconds(int textLength, bool scale, int delay)
        {
            long ms;
            if (scale)
            {
                int d = Math.Max(10, delay);
                int lines = textLength > 99 ? 3 : textLength > 66 ? 2 : 1;
                ms = (long)(4000 * lines * d / 100.0f);
            }
            else
            {
                long d = (5497558140000L * delay) >> 32 >> 5;
                ms = (d >> 31) + d;
            }
            return Math.Max(0, ms) / 1000f;
        }

        /// <summary>
        /// Whether lifting a stack asks how many first: TazUO's GameActions.PickUp,
        /// <c>HoldShiftToSplitStack == HotKeys.IsPressed(SplitStackId)</c> (the split key is Shift).
        /// </summary>
        public static bool OpensSplitMenu(bool holdShiftToSplitStack, bool shiftHeld) => holdShiftToSplitStack == shiftHeld;

        /// <summary>
        /// Whether a double-click on an item in a container window loots it into the backpack instead of using it:
        /// TazUO's ItemGump.OnMouseDoubleClick: the option on, Ctrl not held, the item not a container and holding nothing,
        /// and its outermost container an item in the world (a corpse, a chest), not something worn (the backpack, the bank).
        /// </summary>
        public static bool LootsOnDoubleClick(bool option, bool ctrlHeld, bool itemIsContainer, bool itemHasContents,
                                              bool outermostIsWorldItem, bool haveBackpack) =>
            option && !ctrlHeld && !itemIsContainer && !itemHasContents && outermostIsWorldItem && haveBackpack;

        /// <summary>
        /// Whether a tooltip shows: TazUO's UseTooltip gates Tooltip.SetText only, the text tooltips of window parts (a buff,
        /// a gump element, a spell icon); item and mobile property tooltips show either way.
        /// </summary>
        public static bool ShowsTooltip(bool useTooltip, bool isTextTooltip) => useTooltip || !isTextTooltip;

        /// <summary>The chess and backgammon boards keep their size (TazUO's ContainerGump.GetScale).</summary>
        public static bool KeepsSize(ushort gump) => gump == 0x091A || gump == 0x092E;

        /// <summary>The size a container gump draws at, from <see cref="containersScale"/> (percent), clamped to TazUO's range.</summary>
        public static float ContainerScale(int percent, ushort gump) =>
            KeepsSize(gump) ? 1f : Math.Max(MinContainersScale, Math.Min(MaxContainersScale, percent)) / 100f;

        /// <summary>TazUO's journal log name (JournalManager.CreateWriter): <c>yyyy_MM_dd_HH_mm_ss_{name}_journal.txt</c>, the name made safe.</summary>
        public static string JournalFileName(DateTime opened, string characterName)
        {
            var name = new StringBuilder();
            foreach (char c in string.IsNullOrWhiteSpace(characterName) ? "Unknown" : characterName.Trim())
                name.Append(Array.IndexOf(InvalidFileChars, c) >= 0 || char.IsControl(c) ? '_' : c);
            return opened.ToString("yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture) + "_" + name + "_journal.txt";
        }

        // Path.GetInvalidFileNameChars differs by platform; these are Windows' (the strictest).
        private static readonly char[] InvalidFileChars = { '"', '<', '>', '|', ':', '*', '?', '\\', '/' };

        /// <summary>One journal line as TazUO's JournalManager writes it: <c>[time]  name: text</c>, or <c>[time]  text</c> with no name.</summary>
        public static string JournalLine(DateTime time, string name, string text)
        {
            string stamp = "[" + time.ToString("G", CultureInfo.CurrentCulture) + "]  ";
            return string.IsNullOrEmpty(name) ? stamp + text : stamp + name + ": " + text;
        }
    }
}
