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

        /// <summary>
        /// Whether a character's health line is drawn: TazUO's HealthLinesManager.Draw, ShowMobilesHP and MobileHPShowWhen
        /// (always, or only below full health; no line without a known maximum). <paramref name="alsoWhenFull"/> is this
        /// client's own extra for "when damaged", kept from before the option: whoever the player fights, and the player in
        /// war mode, show a full line too (TazUO's "&lt;100%" hides them).
        /// </summary>
        public static bool ShowsHealthLine(bool showMobilesHP, int showWhen, int hits, int hitsMax, bool alsoWhenFull)
        {
            if (!showMobilesHP || hitsMax <= 0)
                return false;
            return showWhen != MobileHPWhenDamaged || hits < hitsMax || alsoWhenFull;
        }

        /// <summary>
        /// What a character shows of its health: TazUO's HealthLinesManager.Draw. With ShowMobilesHP off, nothing, except
        /// that with ShowTargetIndicator on a target keeps its line (TazUO draws its 0x756x target-indicator gumps there;
        /// this client draws the line). With it on, <see cref="ShowsHealthLine"/> decides whether anything shows; then the
        /// % text for MobileHPType percent or both, not on a ghost, not at 0 % (HitsPercentage != 0), and with "smart" only
        /// below full; the line for MobileHPType line or both, and for a target always (TazUO's forceDraw). A target
        /// (<paramref name="isTarget"/>) is whoever we fight, last attacked or last targeted.
        /// </summary>
        public static (bool Line, bool Percent) HealthDisplay(bool showMobilesHP, int hpType, int showWhen, int hits, int hitsMax,
                                                              bool isDead, bool alsoWhenFull, bool isTarget, bool showTargetIndicator)
        {
            if (hitsMax <= 0)
                return (false, false);
            if (!showMobilesHP)
                return (showTargetIndicator && isTarget, false);
            if (!ShowsHealthLine(true, showWhen, hits, hitsMax, alsoWhenFull))
                return (false, false);
            bool percent = hpType != MobileHPLine && !isDead && (showWhen != MobileHPSmart || hits != hitsMax)
                           && HitsPercent(hits, hitsMax) != 0;
            bool line = hpType != MobileHPPercent || isTarget;
            return (line, percent);
        }

        /// <summary>TazUO's HitsPercentage (MathHelper.PercentageOf, clamped 0-100): hits x 100 / max, rounded down.</summary>
        public static int HitsPercent(int hits, int hitsMax) =>
            hits <= 0 || hitsMax <= 0 ? 0 : Math.Min(100, hits * 100 / hitsMax);

        /// <summary>The % text's hue, as TazUO's Entity.UpdateHits: red below 30, orange below 50, yellow below 80, else green.</summary>
        public static int HitsPercentHue(int percent) =>
            percent < 30 ? 0x0021 : percent < 50 ? 0x0030 : percent < 80 ? 0x0058 : 0x0044;

        /// <summary>The % text as TazUO draws it: <c>[57%]</c>.</summary>
        public static string HitsPercentText(int percent) => "[" + percent + "%]";

        /// <summary>TazUO's ContainerGump CORPSES_GUMP: a corpse's gump is never hued (it gets the eye instead).</summary>
        public const ushort CorpseGump = 0x0009;

        /// <summary>
        /// The hue a container's gump is drawn in: TazUO's ContainerGump, <c>_gumpPicContainer.Hue = item.Hue</c> with
        /// HueContainerGumps on, the corpse gump excepted; 0 (the art's own colours) otherwise.
        /// </summary>
        public static ushort ContainerGumpHue(bool hueContainerGumps, ushort gump, ushort containerHue) =>
            hueContainerGumps && gump != CorpseGump ? containerHue : (ushort)0;

        /// <summary>
        /// The war mode Tab asks for, or null for none: TazUO's GameSceneInputHandler with HoldDownKeyTab, the press asks for
        /// war once (only if not in war already) and the release asks for peace if the press was the one holding it
        /// (<paramref name="holding"/>, kept by the caller). Without it the press toggles, as this client always did.
        /// </summary>
        public static bool? TabWarMode(bool holdDownKeyTab, bool pressed, bool released, bool warMode, ref bool holding)
        {
            if (pressed)
            {
                if (!holdDownKeyTab)
                    return !warMode;
                if (holding)
                    return null;
                holding = true;
                return warMode ? (bool?)null : true;
            }
            if (released && holding)
            {
                holding = false;
                return false;
            }
            return null;
        }

        /// <summary>The health line's width in UI pixels: this client's 44 times TazUO's HealthLineSizeMultiplier (clamped 1-5).</summary>
        public static float HealthLineWidth(int multiplier) =>
            44f * Math.Max(MinHealthLineSize, Math.Min(MaxHealthLineSize, multiplier));

        /// <summary>
        /// Whether a journal line goes into the HUD journal: everything, unless <see cref="showSystemMessagesInJournal"/> is
        /// off and the line is the server's own (serial 0 or 0xFFFFFFFF, no object said it). Speech, labels and party lines stay.
        /// </summary>
        public static bool JournalShowsLine(bool showSystemMessages, uint serial) =>
            showSystemMessages || (serial != 0 && serial != 0xFFFFFFFF);

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
