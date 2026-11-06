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

        // ---- grid containers (TazUO's GridContainer, GridItem, OpenContainer.cs) ----

        /// <summary>
        /// Container gumps TazUO never opens as a grid (OpenContainer.cs, the 0x24 handler's <c>canuse</c> and
        /// <c>isvendor</c> lists, and its 0x091A chessboard check): game boards and vendor / large-container gumps.
        /// </summary>
        public static bool GridNeverOpens(ushort gump)
        {
            int g = gump;
            bool canUse = g == 1009 || g == 1081 || g == 1278 || g == 2417 || (g >= 1060 && g <= 1068) || (g >= 1071 && g <= 1079)
                          || (g >= 1258 && g <= 1270) || (g >= 1282 && g <= 1291);
            bool isVendor = g == 10009 || g == 2330 || (g >= 10060 && g <= 10081) || (g >= 10258 && g <= 10291) || g == 11000
                            || g == 11156 || g == 11415 || g == 11417 || g == 11422 || (g >= 11747 && g <= 11750)
                            || (g >= 11765 && g <= 11770) || (g >= 19800 && g <= 19835) || g == 29724 || g == 40558 || g == 40560
                            || g == 40562 || g == 40586 || g == 49922 || g == 49934 || g == 50138 || (g >= 50153 && g <= 50167)
                            || g == 50250 || g == 50300; // TazUO's "(g >= 50246 && g == 50250)" and "(g >= 50298 && g == 50300)"
            return canUse || isVendor || gump == 0x091A;
        }

        /// <summary>
        /// Whether a container opens as a grid window: TazUO's ContainerStyle.Grid with
        /// GridContainer.ShouldUseOldContainerStyle, which with no per-container choice falls back to
        /// GridContainersDefaultToOldStyleView; corpses follow it too (CorpseContainerStyle's default). Never for
        /// <see cref="GridNeverOpens"/>.
        /// </summary>
        public static bool OpensGrid(bool defaultToOldStyleView, ushort gump) => !defaultToOldStyleView && !GridNeverOpens(gump);

        /// <summary>TazUO's GridItemSize: a cell is <c>round(50 x scale / 100)</c> pixels square (the scale clamped 50-200).</summary>
        public static int GridItemSize(int gridContainersScale) =>
            (int)Math.Round(50 * (Math.Max(MinGridContainersScale, Math.Min(MaxGridContainersScale, gridContainersScale)) / 100f));

        /// <summary>
        /// One side of an item's art in its cell: GridItem.CalculateCenteredDimension. Art (its opaque bounds,
        /// <paramref name="artSize"/>) smaller than the cell keeps its size, or times the scale with ScaleItems; larger, it
        /// is squeezed to the cell, or the cell times the scale with ScaleItems; equal, it fills the cell. Centred: the
        /// offset is half the cell less half the size (both halved first, as TazUO's shifts). Each side on its own.
        /// </summary>
        public static (int Size, int Offset) GridArtSize(int artSize, int cellSize, bool scaleItems, int gridContainersScale)
        {
            float scale = Math.Max(MinGridContainersScale, Math.Min(MaxGridContainersScale, gridContainersScale)) / 100f;
            if (artSize == cellSize)
                return (cellSize, 0);
            int size = artSize < cellSize
                ? (scaleItems ? (int)(artSize * scale) : artSize)
                : (scaleItems ? (int)(cellSize * scale) : cellSize);
            return (size, (cellSize >> 1) - (size >> 1));
        }

        /// <summary>The grid window's background and border alpha: TazUO's <c>ContainerOpacity / 100f</c>, clamped 0-100.</summary>
        public static float GridBackgroundAlpha(int containerOpacity) =>
            Math.Max(MinContainerOpacity, Math.Min(MaxContainerOpacity, containerOpacity)) / 100f;

        /// <summary>TazUO's layers Hair (11), Face (15) and Beard (16): never lootable (Item.IsLootable).</summary>
        public static bool IsBodyPartLayer(int layer) => layer == 11 || layer == 15 || layer == 16;

        /// <summary>
        /// Whether a double-click in a grid window loots the item into the backpack: TazUO's GridItem.OnMouseDoubleClick,
        /// DoubleClickToLootInsideContainers in a corpse only, the item not a container and lootable (not hair, face or
        /// beard), the window not the player's own backpack. Else the item is used.
        /// </summary>
        public static bool GridLootsOnDoubleClick(bool option, bool containerIsCorpse, bool containerIsBackpack, bool itemIsContainer,
                                                  int itemTileLayer, bool haveBackpack) =>
            option && containerIsCorpse && !containerIsBackpack && !itemIsContainer && !IsBodyPartLayer(itemTileLayer) && haveBackpack;

        // TazUO's Constants.BAD_CONTAINER_LAYERS (GameObjects layers 0-31).
        private static readonly bool[] BadContainerLayers =
        {
            false,
            true, true, true, true, true, true, true, true,
            true, true, false, true, true, true, false, false,
            true, true, true, true,
            false,
            true, true, true, false, false, false, false, false,
            false, false,
        };

        /// <summary>
        /// Whether an item is listed in a grid window: GridSlotManager.GetItemsInContainer. In a corpse, what it wears on a
        /// layer outside BAD_CONTAINER_LAYERS (hair, face, beard, the backpack, the mount) is left out
        /// (<paramref name="wornLayer"/>, 0 for none); anywhere, wearable hair, face and beard are.
        /// </summary>
        public static bool GridListsItem(bool containerIsCorpse, int wornLayer, bool wearable, int tileLayer)
        {
            if (containerIsCorpse && wornLayer > 0 && (wornLayer >= BadContainerLayers.Length || !BadContainerLayers[wornLayer]))
                return false;
            return !(wearable && IsBodyPartLayer(tileLayer));
        }

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
