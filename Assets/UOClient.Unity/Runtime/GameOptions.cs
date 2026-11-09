using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace UOClient.Unity
{
    // No UnityEngine here: the test project compiles this file on its own to check the rules.
    /// <summary>
    /// The player's client options (the options window's settings), kept in their own file,
    /// <c>UserSettings/unity-uo-options.json</c> (<see cref="FileName"/>), so the connection settings in
    /// unity-uo-settings.json are never rewritten for them. Only options with working code behind them are here. Every
    /// value is clamped to its range by <see cref="Clamp"/> on load and before it is applied.
    /// </summary>
    [Serializable]
    public sealed partial class GameOptions
    {
        public const string FileName = "unity-uo-options.json";

        public const float MinZoom = 0.25f, MaxZoom = 4f;

        /// <summary>TazUO's Constants.MIN/MAX_CONTAINER_SIZE_PERC (ContainersScale, percent).</summary>
        public const int MinContainersScale = 50, MaxContainersScale = 200;

        /// <summary>TazUO's speech delay slider range (ModernOptionsGump.BuildSpeech, 0-1000).</summary>
        public const int MinSpeechDelay = 0, MaxSpeechDelay = 1000;

        /// <summary>The fixed UI scale's range; 0 (<see cref="AutoUiScale"/>) follows the screen height.</summary>
        public const float MinUiScale = 0.75f, MaxUiScale = 2.5f;

        public const float AutoUiScale = 0f;
        public const float MaxTooltipDelay = 2f;

        /// <summary>The hues a speech colour can take: 0 (the server's default) up to the last entry of hues.mul.</summary>
        public const int MinHue = 0, MaxHue = 2999;

        /// <summary>TazUO's MobileHPShowWhen: always, below full health, or "smart" (the line always, the % text only below full).</summary>
        public const int MobileHPAlways = 0, MobileHPWhenDamaged = 1, MobileHPSmart = 2;

        /// <summary>TazUO's MobileHPType (Profile.cs: 0 = %, 1 = line, 2 = both).</summary>
        public const int MobileHPPercent = 0, MobileHPLine = 1, MobileHPBoth = 2;

        /// <summary>TazUO's corpse open distance slider (ModernOptionsGump.BuildGeneral, 0-5 tiles).</summary>
        public const int MaxAutoOpenCorpseRange = 5;

        /// <summary>TazUO's CorpseOpenOptions: none, not while targeting, not while hidden, both (Core.CorpseAutoOpen).</summary>
        public const int CorpseOpenAlways = 0, CorpseOpenNotTargeting = 1, CorpseOpenNotHiding = 2, CorpseOpenNotTargetingOrHiding = 3;

        /// <summary>TazUO's HealthLineSizeMultiplier slider range (ModernOptionsGump.BuildTazUO, 1-5).</summary>
        public const int MinHealthLineSize = 1, MaxHealthLineSize = 5;

        /// <summary>TazUO's ContainerOpacity slider (ModernOptionsGump.BuildContainers, 0-100).</summary>
        public const int MinContainerOpacity = 0, MaxContainerOpacity = 100;

        /// <summary>TazUO's GridContainersScale slider (ModernOptionsGump.BuildContainers, 50-200 %).</summary>
        public const int MinGridContainersScale = 50, MaxGridContainersScale = 200;

        public bool showNames = true;
        public bool showJournal = true;
        public bool showHud = true;
        public bool autoRoofs = true;
        public float zoom = 1f;

        /// <summary>0: Screen.height / 1000 (the default); otherwise the scale every window and the HUD draw at.</summary>
        public float uiScale = AutoUiScale;

        /// <summary>
        /// TazUO's HighlightGameObjects: characters, corpses and fixtures get the mouse-over outline too (other ground
        /// items always do). On by default here (ObjectHighlight). The cue is the outline only; there is no fill to choose.
        /// </summary>
        public bool highlightGameObjects = true;

        /// <summary>Seconds the mouse rests on something before its tooltip shows.</summary>
        public float tooltipDelay = 0.4f;

        public float soundVolume = 0.8f;
        public float musicVolume = 0.45f;

        /// <summary>TazUO's EnabledCriminalActionQuery (GameSession.CriminalActionQuery): ask before attacking an innocent. On by default.</summary>
        public bool criminalActionQuery = true;

        /// <summary>TazUO's EnabledBeneficialCriminalActionQuery (GameSession.BeneficialCriminalActionQuery): ask before helping a criminal or gray. Off by default.</summary>
        public bool beneficialCriminalActionQuery = false;

        /// <summary>
        /// TazUO's AlwaysRun (PlayerMovement): every step runs, Shift or not. Off by default here (TazUO: on), so walking
        /// stays as it was until chosen.
        /// </summary>
        public bool alwaysRun = false;

        /// <summary>TazUO's AlwaysRunUnlessHidden: with always run on, a hidden player walks (so stealth isn't broken). On, as TazUO.</summary>
        public bool alwaysRunUnlessHidden = true;

        /// <summary>TazUO's ScaleSpeechDelay: overhead speech stays longer for longer lines (<see cref="SpeechSeconds"/>). On, as TazUO.</summary>
        public bool scaleSpeechDelay = true;

        /// <summary>TazUO's SpeechDelay (0-1000, default 100): how long overhead speech stays (<see cref="SpeechSeconds"/>).</summary>
        public int speechDelay = 100;

        /// <summary>TazUO's SaveJournalToFile: every journal line is also appended to Logs/JournalLogs (JournalFile). Off, as TazUO.</summary>
        public bool saveJournalToFile = false;

        /// <summary>TazUO's ContainersScale (percent, 50-200): container gumps and their items draw at this size (ContainerWindow).</summary>
        public int containersScale = 100;

        /// <summary>
        /// TazUO's DoubleClickToLootInsideContainers: a double-click on a plain item in a corpse or a chest on the ground moves
        /// it to the backpack instead of using it (<see cref="LootsOnDoubleClick"/>). Off, as TazUO.
        /// </summary>
        public bool doubleClickToLootInsideContainers = false;

        /// <summary>
        /// TazUO's HoldShiftToSplitStack (<see cref="OpensSplitMenu"/>): on, a stack dragged with Shift asks how many; off, a
        /// plain drag asks and Shift lifts the lot. Off by default, as TazUO and the classic client: lifting a stack asks.
        /// </summary>
        public bool holdShiftToSplitStack = false;

        /// <summary>TazUO's UseTooltip (Tooltip.SetText): off hides the text tooltips of windows (buffs, gump elements, spell icons), not item or mobile properties.</summary>
        public bool useTooltip = true;

        /// <summary>TazUO's BuffBarTime: the time left drawn over each timed buff icon. On by default here (TazUO: off), as this client always drew it.</summary>
        public bool buffBarTime = true;

        /// <summary>TazUO's CastSpellsByOneClick (GlobalSettings.SingleClickIconUse): a single click on a floating spell icon casts it. Off, as TazUO.</summary>
        public bool castSpellsByOneClick = false;

        /// <summary>
        /// TazUO's ShowMobilesHP: the health lines under characters (HudPresenter, DemoHud). On by default here (TazUO: off),
        /// as this client always drew them. What is drawn is <see cref="mobileHPType"/> (<see cref="HealthDisplay"/>).
        /// </summary>
        public bool showMobilesHP = true;

        /// <summary>
        /// TazUO's MobileHPShowWhen (<see cref="HealthDisplay"/>): <see cref="MobileHPAlways"/>,
        /// <see cref="MobileHPWhenDamaged"/> (the default here, TazUO: always), which also keeps this client's own extras, or
        /// <see cref="MobileHPSmart"/>.
        /// </summary>
        public int mobileHPShowWhen = MobileHPWhenDamaged;

        /// <summary>
        /// TazUO's MobileHPType (<see cref="HealthDisplay"/>): the % text over the head, the line under the feet, or both.
        /// <see cref="MobileHPLine"/> by default here, as this client always drew (TazUO: percent).
        /// </summary>
        public int mobileHPType = MobileHPLine;

        /// <summary>
        /// TazUO's ShowTargetIndicator (HealthLinesManager): whoever you fight, last attacked or last targeted keeps a health
        /// line even with <see cref="showMobilesHP"/> off. Off, as TazUO.
        /// </summary>
        public bool showTargetIndicator = false;

        /// <summary>
        /// TazUO's AutoOpenCorpses (Core.CorpseAutoOpen): corpses in range open once each. Off by default here (TazUO: on):
        /// it acts on the world for the player, so only when the player turns it on.
        /// </summary>
        public bool autoOpenCorpses = false;

        /// <summary>TazUO's AutoOpenCorpseRange (0-5 tiles). 2, as TazUO.</summary>
        public int autoOpenCorpseRange = 2;

        /// <summary>TazUO's CorpseOpenOptions (<see cref="CorpseOpenNotTargetingOrHiding"/>, as TazUO).</summary>
        public int corpseOpenOptions = CorpseOpenNotTargetingOrHiding;

        /// <summary>
        /// TazUO's AutoOpenDoors (Core.DoorAutoOpen): stepping or turning to face a closed door opens it. Off by default
        /// here (TazUO: on): it acts on the world for the player, so only when the player turns it on.
        /// </summary>
        public bool autoOpenDoors = false;

        /// <summary>TazUO's AutoOpenDoorsIfHidden: with <see cref="autoOpenDoors"/> on, doors open for a hidden player too. On, as TazUO.</summary>
        public bool autoOpenDoorsIfHidden = true;

        /// <summary>TazUO's HueContainerGumps (ContainerGump): a container's gump takes the container's hue. On, as TazUO.</summary>
        public bool hueContainerGumps = true;

        /// <summary>
        /// TazUO's HoldDownKeyTab (GameSceneInputHandler): war mode while Tab is held, peace when it comes up. Off, as TazUO;
        /// off, Tab toggles war mode on the press, as this client always did (TazUO toggles on the release).
        /// </summary>
        public bool holdDownKeyTab = false;

        /// <summary>TazUO's HealthLineSizeMultiplier (1-5): the health line's width times this (<see cref="HealthLineWidth"/>). 1, as TazUO.</summary>
        public int healthLineSizeMultiplier = 1;

        /// <summary>
        /// TazUO's SpeechHue: the hue plain typed speech goes out in (GameSession.SpeechOptions). 0x3B2, the classic
        /// client's speech colour this client always sent (TazUO's default is 0x2B2).
        /// </summary>
        public int speechHue = 0x3B2;

        /// <summary>TazUO's WhisperHue, EmoteHue, YellHue: typed "; ", ": " and "! " lines go out in these. As TazUO (and the classic client).</summary>
        public int whisperHue = 0x33, emoteHue = 0x21, yellHue = 0x21;

        /// <summary>TazUO's PartyMessageHue: the hue party lines are shown in (PartyManager). 0x44, as TazUO.</summary>
        public int partyMessageHue = 0x44;

        /// <summary>TazUO's IgnoreGuildMessages / IgnoreAllianceMessages: those lines are dropped as they arrive (MessageManager). Off, as TazUO.</summary>
        public bool ignoreGuildMessages = false, ignoreAllianceMessages = false;

        /// <summary>
        /// This client's own: the server's system lines (no speaker object) go into the HUD journal too
        /// (<see cref="JournalShowsLine"/>). On, as it always was; off leaves them to the chat window and the journal file.
        /// </summary>
        public bool showSystemMessagesInJournal = true;

        /// <summary>
        /// TazUO's HighlightContainerWhenSelected (ContainerGump.Update): while the mouse is over a container window, that
        /// container gets the mouse-over outline in the world, or in the window of the container it sits in. Off, as TazUO.
        /// </summary>
        public bool highlightContainerWhenSelected = false;

        /// <summary>
        /// TazUO's GridContainersDefaultToOldStyleView (GridContainer.ShouldUseOldContainerStyle): containers open as the
        /// classic gump. On by default here (TazUO: off, grid first), so containers open as they always did until the
        /// player turns it off; off, new opens are grid windows (<see cref="OpensGrid"/>, GridContainerWindow).
        /// </summary>
        public bool gridContainersDefaultToOldStyleView = true;

        /// <summary>TazUO's ContainerOpacity (0-100): the grid window's background and border alpha. 50, as TazUO. Classic gumps never use it.</summary>
        public int containerOpacity = 50;

        /// <summary>TazUO's GridContainersScale (50-200 %): a grid cell is <see cref="GridItemSize"/> pixels. 100, as TazUO.</summary>
        public int gridContainersScale = 100;

        /// <summary>TazUO's GridContainerScaleItems: the item art in a grid cell scales with it (<see cref="GridArtSize"/>). On, as TazUO.</summary>
        public bool gridContainerScaleItems = true;

        // ---- nameplates (NamePlates; TazUO's Nameplates page and TazUO -> Nameplates) ----

        /// <summary>
        /// TazUO's NameOverheadToggled (NameOverHeadManager.IsPermaToggled): plates over characters (NamePlates). Off, as
        /// TazUO. Not the plain names over heads (<see cref="showNames"/>, TazUO's ShowMobileNameOverhead): with both on, both
        /// are drawn, the plate above the plain name (NamePlates.PlateBottom), as TazUO.
        /// </summary>
        public bool nameOverheadToggled = false;

        /// <summary>TazUO's active NameOverheadOption's character bits (NamePlates.Allowed). TazUO's "Mobiles only" entry.</summary>
        public int nameOverheadFlags = NamePlates.AllMobiles;

        /// <summary>TazUO's NamePlateHealthBar: plates also act as health bars (from the hits the server sent). On, as TazUO.</summary>
        public bool namePlateHealthBar = true;

        /// <summary>TazUO's NamePlateUseNotorietyText: the name in its notoriety colour; off, black or white for contrast. Off, as TazUO.</summary>
        public bool namePlateUseNotorietyText = false;

        /// <summary>TazUO's NamePlateShowMissingHealth: the empty part of a bar drawn dark. On, as TazUO.</summary>
        public bool namePlateShowMissingHealth = true;

        /// <summary>TazUO's NamePlateHealthBarOpacity (0-100). 50, as TazUO.</summary>
        public int namePlateHealthBarOpacity = 50;

        /// <summary>TazUO's NamePlateHideAtFullHealth / NamePlateHideAtFullHealthInWarmode (only while in war mode). Off, as TazUO.</summary>
        public bool namePlateHideAtFullHealth = false, namePlateHideAtFullHealthInWarmode = false;

        /// <summary>TazUO's NamePlateUseFixedWidth / NamePlateFixedWidth (60-300): the name band's width, long names cut with "...". Off, 120.</summary>
        public bool namePlateUseFixedWidth = false;
        public int namePlateFixedWidth = 120;

        /// <summary>TazUO's NamePlateUseFixedHealthBarWidth / NamePlateHealthBarFixedWidth (60-300). Off, 120.</summary>
        public bool namePlateUseFixedHealthBarWidth = false;
        public int namePlateHealthBarFixedWidth = 120;

        /// <summary>TazUO's NamePlateHeight (0-80; 0 = fits the text). 0.</summary>
        public int namePlateHeight = 0;

        /// <summary>TazUO's NamePlateSplitHealthBar: bars in their own rows under the name. Off.</summary>
        public bool namePlateSplitHealthBar = false;

        /// <summary>TazUO's NamePlateHealthBarMode: status colours (0) or one of ten fixed colours (NamePlates.FillNames). Status.</summary>
        public int namePlateHealthBarMode = NamePlates.FillStatus;

        /// <summary>TazUO's NamePlateBackgroundMode (NamePlates.BackgroundModeNames). Fixed colour.</summary>
        public int namePlateBackgroundMode = NamePlates.BackgroundFixed;

        /// <summary>TazUO's NamePlateBorderOpacity and NamePlateOpacity (background), 0-100. 50 and 75, as TazUO.</summary>
        public int namePlateBorderOpacity = 50, namePlateOpacity = 75;

        /// <summary>TazUO's NamePlateBackgroundR / G / B (0-255): the fixed background colour. Black.</summary>
        public int namePlateBackgroundR = 0, namePlateBackgroundG = 0, namePlateBackgroundB = 0;

        /// <summary>TazUO's NamePlateAvoidOverlap: a plate overlapping an earlier one moves down below it. Off.</summary>
        public bool namePlateAvoidOverlap = false;

        public static GameOptions Defaults() => new GameOptions();

        /// <summary>Copies the nameplate options (the fields above) from <paramref name="from"/> into <paramref name="to"/>.</summary>
        public static void CopyNamePlates(GameOptions from, GameOptions to)
        {
            to.nameOverheadToggled = from.nameOverheadToggled;
            to.nameOverheadFlags = from.nameOverheadFlags;
            to.namePlateHealthBar = from.namePlateHealthBar;
            to.namePlateUseNotorietyText = from.namePlateUseNotorietyText;
            to.namePlateShowMissingHealth = from.namePlateShowMissingHealth;
            to.namePlateHealthBarOpacity = from.namePlateHealthBarOpacity;
            to.namePlateHideAtFullHealth = from.namePlateHideAtFullHealth;
            to.namePlateHideAtFullHealthInWarmode = from.namePlateHideAtFullHealthInWarmode;
            to.namePlateUseFixedWidth = from.namePlateUseFixedWidth;
            to.namePlateFixedWidth = from.namePlateFixedWidth;
            to.namePlateUseFixedHealthBarWidth = from.namePlateUseFixedHealthBarWidth;
            to.namePlateHealthBarFixedWidth = from.namePlateHealthBarFixedWidth;
            to.namePlateHeight = from.namePlateHeight;
            to.namePlateSplitHealthBar = from.namePlateSplitHealthBar;
            to.namePlateHealthBarMode = from.namePlateHealthBarMode;
            to.namePlateBackgroundMode = from.namePlateBackgroundMode;
            to.namePlateBorderOpacity = from.namePlateBorderOpacity;
            to.namePlateOpacity = from.namePlateOpacity;
            to.namePlateBackgroundR = from.namePlateBackgroundR;
            to.namePlateBackgroundG = from.namePlateBackgroundG;
            to.namePlateBackgroundB = from.namePlateBackgroundB;
            to.namePlateAvoidOverlap = from.namePlateAvoidOverlap;
        }

        private bool SameNamePlates(GameOptions o) =>
            nameOverheadToggled == o.nameOverheadToggled && nameOverheadFlags == o.nameOverheadFlags
            && namePlateHealthBar == o.namePlateHealthBar && namePlateUseNotorietyText == o.namePlateUseNotorietyText
            && namePlateShowMissingHealth == o.namePlateShowMissingHealth && namePlateHealthBarOpacity == o.namePlateHealthBarOpacity
            && namePlateHideAtFullHealth == o.namePlateHideAtFullHealth
            && namePlateHideAtFullHealthInWarmode == o.namePlateHideAtFullHealthInWarmode
            && namePlateUseFixedWidth == o.namePlateUseFixedWidth && namePlateFixedWidth == o.namePlateFixedWidth
            && namePlateUseFixedHealthBarWidth == o.namePlateUseFixedHealthBarWidth
            && namePlateHealthBarFixedWidth == o.namePlateHealthBarFixedWidth && namePlateHeight == o.namePlateHeight
            && namePlateSplitHealthBar == o.namePlateSplitHealthBar && namePlateHealthBarMode == o.namePlateHealthBarMode
            && namePlateBackgroundMode == o.namePlateBackgroundMode && namePlateBorderOpacity == o.namePlateBorderOpacity
            && namePlateOpacity == o.namePlateOpacity && namePlateBackgroundR == o.namePlateBackgroundR
            && namePlateBackgroundG == o.namePlateBackgroundG && namePlateBackgroundB == o.namePlateBackgroundB
            && namePlateAvoidOverlap == o.namePlateAvoidOverlap;

        public GameOptions Clone() => (GameOptions)MemberwiseClone();

        /// <summary>Brings every value into its range (NaN takes the default); returns this.</summary>
        public GameOptions Clamp()
        {
            var d = Defaults();
            zoom = Range(zoom, MinZoom, MaxZoom, d.zoom);
            uiScale = float.IsNaN(uiScale) || uiScale <= 0 ? AutoUiScale : Range(uiScale, MinUiScale, MaxUiScale, d.uiScale);
            tooltipDelay = Range(tooltipDelay, 0f, MaxTooltipDelay, d.tooltipDelay);
            soundVolume = Range(soundVolume, 0f, 1f, d.soundVolume);
            musicVolume = Range(musicVolume, 0f, 1f, d.musicVolume);
            speechDelay = Math.Max(MinSpeechDelay, Math.Min(MaxSpeechDelay, speechDelay));
            containersScale = Math.Max(MinContainersScale, Math.Min(MaxContainersScale, containersScale));
            mobileHPShowWhen = Math.Max(MobileHPAlways, Math.Min(MobileHPSmart, mobileHPShowWhen));
            mobileHPType = Math.Max(MobileHPPercent, Math.Min(MobileHPBoth, mobileHPType));
            autoOpenCorpseRange = Math.Max(0, Math.Min(MaxAutoOpenCorpseRange, autoOpenCorpseRange));
            corpseOpenOptions = Math.Max(CorpseOpenAlways, Math.Min(CorpseOpenNotTargetingOrHiding, corpseOpenOptions));
            healthLineSizeMultiplier = Math.Max(MinHealthLineSize, Math.Min(MaxHealthLineSize, healthLineSizeMultiplier));
            speechHue = ClampHue(speechHue);
            whisperHue = ClampHue(whisperHue);
            emoteHue = ClampHue(emoteHue);
            yellHue = ClampHue(yellHue);
            partyMessageHue = ClampHue(partyMessageHue);
            containerOpacity = Math.Max(MinContainerOpacity, Math.Min(MaxContainerOpacity, containerOpacity));
            gridContainersScale = Math.Max(MinGridContainersScale, Math.Min(MaxGridContainersScale, gridContainersScale));
            nameOverheadFlags &= NamePlates.MobileFlags;
            namePlateHealthBarOpacity = Percent100(namePlateHealthBarOpacity);
            namePlateBorderOpacity = Percent100(namePlateBorderOpacity);
            namePlateOpacity = Percent100(namePlateOpacity);
            namePlateFixedWidth = Math.Max(NamePlates.MinFixedWidth, Math.Min(NamePlates.MaxFixedWidth, namePlateFixedWidth));
            namePlateHealthBarFixedWidth = Math.Max(NamePlates.MinFixedWidth, Math.Min(NamePlates.MaxFixedWidth, namePlateHealthBarFixedWidth));
            namePlateHeight = Math.Max(0, Math.Min(NamePlates.MaxHeight, namePlateHeight));
            namePlateHealthBarMode = Math.Max(NamePlates.FillStatus, Math.Min(NamePlates.FillLast, namePlateHealthBarMode));
            namePlateBackgroundMode = Math.Max(NamePlates.BackgroundFixed, Math.Min(NamePlates.BackgroundEntityNotoriety, namePlateBackgroundMode));
            namePlateBackgroundR = Math.Max(0, Math.Min(255, namePlateBackgroundR));
            namePlateBackgroundG = Math.Max(0, Math.Min(255, namePlateBackgroundG));
            namePlateBackgroundB = Math.Max(0, Math.Min(255, namePlateBackgroundB));
            return this;
        }

        private static int Percent100(int v) => Math.Max(0, Math.Min(100, v));

        public static int ClampHue(int hue) => Math.Max(MinHue, Math.Min(MaxHue, hue));

        private static float Range(float v, float min, float max, float fallback) =>
            float.IsNaN(v) ? fallback : Math.Max(min, Math.Min(max, v));

        /// <summary>The UI scale to draw at: the fixed one, or (auto) Screen.height / 1000 within the same range.</summary>
        public static float ResolveUiScale(float screenHeight, float uiScale) =>
            uiScale > 0 ? Math.Max(MinUiScale, Math.Min(MaxUiScale, uiScale))
                        : Math.Max(MinUiScale, Math.Min(MaxUiScale, screenHeight / 1000f));

        public bool SameAs(GameOptions o) =>
            o != null && showNames == o.showNames && showJournal == o.showJournal && showHud == o.showHud && autoRoofs == o.autoRoofs
            && zoom == o.zoom && uiScale == o.uiScale && highlightGameObjects == o.highlightGameObjects
            && tooltipDelay == o.tooltipDelay && soundVolume == o.soundVolume && musicVolume == o.musicVolume
            && criminalActionQuery == o.criminalActionQuery && beneficialCriminalActionQuery == o.beneficialCriminalActionQuery
            && alwaysRun == o.alwaysRun && alwaysRunUnlessHidden == o.alwaysRunUnlessHidden
            && scaleSpeechDelay == o.scaleSpeechDelay && speechDelay == o.speechDelay && saveJournalToFile == o.saveJournalToFile
            && containersScale == o.containersScale && doubleClickToLootInsideContainers == o.doubleClickToLootInsideContainers
            && holdShiftToSplitStack == o.holdShiftToSplitStack && useTooltip == o.useTooltip
            && buffBarTime == o.buffBarTime && castSpellsByOneClick == o.castSpellsByOneClick
            && showMobilesHP == o.showMobilesHP && mobileHPShowWhen == o.mobileHPShowWhen
            && healthLineSizeMultiplier == o.healthLineSizeMultiplier
            && speechHue == o.speechHue && whisperHue == o.whisperHue && emoteHue == o.emoteHue && yellHue == o.yellHue
            && partyMessageHue == o.partyMessageHue
            && ignoreGuildMessages == o.ignoreGuildMessages && ignoreAllianceMessages == o.ignoreAllianceMessages
            && showSystemMessagesInJournal == o.showSystemMessagesInJournal
            && highlightContainerWhenSelected == o.highlightContainerWhenSelected
            && mobileHPType == o.mobileHPType && showTargetIndicator == o.showTargetIndicator
            && autoOpenCorpses == o.autoOpenCorpses && autoOpenCorpseRange == o.autoOpenCorpseRange
            && corpseOpenOptions == o.corpseOpenOptions && hueContainerGumps == o.hueContainerGumps
            && holdDownKeyTab == o.holdDownKeyTab
            && autoOpenDoors == o.autoOpenDoors && autoOpenDoorsIfHidden == o.autoOpenDoorsIfHidden
            && gridContainersDefaultToOldStyleView == o.gridContainersDefaultToOldStyleView
            && containerOpacity == o.containerOpacity && gridContainersScale == o.gridContainersScale
            && gridContainerScaleItems == o.gridContainerScaleItems
            && SameNamePlates(o);

        // ---- the file: a flat JSON object; keys this version doesn't know are kept as they were ----

        /// <summary>The options in <paramref name="json"/> (missing keys keep their defaults, bad values too), clamped.</summary>
        public static GameOptions Parse(string json) => Parse(json, out _);

        /// <summary>As <see cref="Parse(string)"/>, also returning the keys it doesn't know, raw, to write back unchanged.</summary>
        public static GameOptions Parse(string json, out List<KeyValuePair<string, string>> unknown)
        {
            var o = Defaults();
            unknown = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrWhiteSpace(json) || !FlatJson.TryRead(json, out var fields))
                return o;
            foreach (var (key, raw) in fields)
            {
                switch (key)
                {
                    case "showNames": Bool(raw, ref o.showNames); break;
                    case "showJournal": Bool(raw, ref o.showJournal); break;
                    case "showHud": Bool(raw, ref o.showHud); break;
                    case "autoRoofs": Bool(raw, ref o.autoRoofs); break;
                    case "highlightGameObjects": Bool(raw, ref o.highlightGameObjects); break;
                    case "zoom": Number(raw, ref o.zoom); break;
                    case "uiScale": Number(raw, ref o.uiScale); break;
                    case "tooltipDelay": Number(raw, ref o.tooltipDelay); break;
                    case "soundVolume": Number(raw, ref o.soundVolume); break;
                    case "musicVolume": Number(raw, ref o.musicVolume); break;
                    case "criminalActionQuery": Bool(raw, ref o.criminalActionQuery); break;
                    case "beneficialCriminalActionQuery": Bool(raw, ref o.beneficialCriminalActionQuery); break;
                    case "alwaysRun": Bool(raw, ref o.alwaysRun); break;
                    case "alwaysRunUnlessHidden": Bool(raw, ref o.alwaysRunUnlessHidden); break;
                    case "scaleSpeechDelay": Bool(raw, ref o.scaleSpeechDelay); break;
                    case "speechDelay": Integer(raw, ref o.speechDelay); break;
                    case "saveJournalToFile": Bool(raw, ref o.saveJournalToFile); break;
                    case "containersScale": Integer(raw, ref o.containersScale); break;
                    case "doubleClickToLootInsideContainers": Bool(raw, ref o.doubleClickToLootInsideContainers); break;
                    case "holdShiftToSplitStack": Bool(raw, ref o.holdShiftToSplitStack); break;
                    case "useTooltip": Bool(raw, ref o.useTooltip); break;
                    case "buffBarTime": Bool(raw, ref o.buffBarTime); break;
                    case "castSpellsByOneClick": Bool(raw, ref o.castSpellsByOneClick); break;
                    case "showMobilesHP": Bool(raw, ref o.showMobilesHP); break;
                    case "mobileHPShowWhen": Integer(raw, ref o.mobileHPShowWhen); break;
                    case "healthLineSizeMultiplier": Integer(raw, ref o.healthLineSizeMultiplier); break;
                    case "speechHue": Integer(raw, ref o.speechHue); break;
                    case "whisperHue": Integer(raw, ref o.whisperHue); break;
                    case "emoteHue": Integer(raw, ref o.emoteHue); break;
                    case "yellHue": Integer(raw, ref o.yellHue); break;
                    case "partyMessageHue": Integer(raw, ref o.partyMessageHue); break;
                    case "ignoreGuildMessages": Bool(raw, ref o.ignoreGuildMessages); break;
                    case "ignoreAllianceMessages": Bool(raw, ref o.ignoreAllianceMessages); break;
                    case "showSystemMessagesInJournal": Bool(raw, ref o.showSystemMessagesInJournal); break;
                    case "highlightContainerWhenSelected": Bool(raw, ref o.highlightContainerWhenSelected); break;
                    case "mobileHPType": Integer(raw, ref o.mobileHPType); break;
                    case "showTargetIndicator": Bool(raw, ref o.showTargetIndicator); break;
                    case "autoOpenCorpses": Bool(raw, ref o.autoOpenCorpses); break;
                    case "autoOpenCorpseRange": Integer(raw, ref o.autoOpenCorpseRange); break;
                    case "corpseOpenOptions": Integer(raw, ref o.corpseOpenOptions); break;
                    case "hueContainerGumps": Bool(raw, ref o.hueContainerGumps); break;
                    case "holdDownKeyTab": Bool(raw, ref o.holdDownKeyTab); break;
                    case "autoOpenDoors": Bool(raw, ref o.autoOpenDoors); break;
                    case "autoOpenDoorsIfHidden": Bool(raw, ref o.autoOpenDoorsIfHidden); break;
                    case "gridContainersDefaultToOldStyleView": Bool(raw, ref o.gridContainersDefaultToOldStyleView); break;
                    case "containerOpacity": Integer(raw, ref o.containerOpacity); break;
                    case "gridContainersScale": Integer(raw, ref o.gridContainersScale); break;
                    case "gridContainerScaleItems": Bool(raw, ref o.gridContainerScaleItems); break;
                    case "nameOverheadToggled": Bool(raw, ref o.nameOverheadToggled); break;
                    case "nameOverheadFlags": Integer(raw, ref o.nameOverheadFlags); break;
                    case "namePlateHealthBar": Bool(raw, ref o.namePlateHealthBar); break;
                    case "namePlateUseNotorietyText": Bool(raw, ref o.namePlateUseNotorietyText); break;
                    case "namePlateShowMissingHealth": Bool(raw, ref o.namePlateShowMissingHealth); break;
                    case "namePlateHealthBarOpacity": Integer(raw, ref o.namePlateHealthBarOpacity); break;
                    case "namePlateHideAtFullHealth": Bool(raw, ref o.namePlateHideAtFullHealth); break;
                    case "namePlateHideAtFullHealthInWarmode": Bool(raw, ref o.namePlateHideAtFullHealthInWarmode); break;
                    case "namePlateUseFixedWidth": Bool(raw, ref o.namePlateUseFixedWidth); break;
                    case "namePlateFixedWidth": Integer(raw, ref o.namePlateFixedWidth); break;
                    case "namePlateUseFixedHealthBarWidth": Bool(raw, ref o.namePlateUseFixedHealthBarWidth); break;
                    case "namePlateHealthBarFixedWidth": Integer(raw, ref o.namePlateHealthBarFixedWidth); break;
                    case "namePlateHeight": Integer(raw, ref o.namePlateHeight); break;
                    case "namePlateSplitHealthBar": Bool(raw, ref o.namePlateSplitHealthBar); break;
                    case "namePlateHealthBarMode": Integer(raw, ref o.namePlateHealthBarMode); break;
                    case "namePlateBackgroundMode": Integer(raw, ref o.namePlateBackgroundMode); break;
                    case "namePlateBorderOpacity": Integer(raw, ref o.namePlateBorderOpacity); break;
                    case "namePlateOpacity": Integer(raw, ref o.namePlateOpacity); break;
                    case "namePlateBackgroundR": Integer(raw, ref o.namePlateBackgroundR); break;
                    case "namePlateBackgroundG": Integer(raw, ref o.namePlateBackgroundG); break;
                    case "namePlateBackgroundB": Integer(raw, ref o.namePlateBackgroundB); break;
                    case "namePlateAvoidOverlap": Bool(raw, ref o.namePlateAvoidOverlap); break;
                    default: unknown.Add(new KeyValuePair<string, string>(key, raw)); break;
                }
            }
            return o.Clamp();
        }

        private static void Bool(string raw, ref bool field)
        {
            if (raw == "true") field = true;
            else if (raw == "false") field = false;
        }

        private static void Number(string raw, ref float field)
        {
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                field = v;
        }

        /// <summary>A whole number; a fraction is rounded, anything out of int's range or not a number is ignored (Clamp then ranges it).</summary>
        private static void Integer(string raw, ref int field)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && !double.IsNaN(v)
                && v >= int.MinValue && v <= int.MaxValue)
                field = (int)Math.Round(v);
        }

        /// <summary>The file's text: these options, then <paramref name="unknown"/> as they were read.</summary>
        public string ToJson(IEnumerable<KeyValuePair<string, string>> unknown = null)
        {
            var sb = new StringBuilder("{\n");
            void Add(string key, string raw) => sb.Append("  \"").Append(key).Append("\": ").Append(raw).Append(",\n");
            string B(bool b) => b ? "true" : "false";
            string F(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);
            Add("showNames", B(showNames));
            Add("showJournal", B(showJournal));
            Add("showHud", B(showHud));
            Add("autoRoofs", B(autoRoofs));
            Add("zoom", F(zoom));
            Add("uiScale", F(uiScale));
            Add("highlightGameObjects", B(highlightGameObjects));
            Add("tooltipDelay", F(tooltipDelay));
            Add("soundVolume", F(soundVolume));
            Add("musicVolume", F(musicVolume));
            Add("criminalActionQuery", B(criminalActionQuery));
            Add("beneficialCriminalActionQuery", B(beneficialCriminalActionQuery));
            Add("alwaysRun", B(alwaysRun));
            Add("alwaysRunUnlessHidden", B(alwaysRunUnlessHidden));
            Add("scaleSpeechDelay", B(scaleSpeechDelay));
            Add("speechDelay", speechDelay.ToString(CultureInfo.InvariantCulture));
            Add("saveJournalToFile", B(saveJournalToFile));
            Add("containersScale", containersScale.ToString(CultureInfo.InvariantCulture));
            Add("doubleClickToLootInsideContainers", B(doubleClickToLootInsideContainers));
            Add("holdShiftToSplitStack", B(holdShiftToSplitStack));
            Add("useTooltip", B(useTooltip));
            Add("buffBarTime", B(buffBarTime));
            Add("castSpellsByOneClick", B(castSpellsByOneClick));
            string I(int i) => i.ToString(CultureInfo.InvariantCulture);
            Add("showMobilesHP", B(showMobilesHP));
            Add("mobileHPShowWhen", I(mobileHPShowWhen));
            Add("healthLineSizeMultiplier", I(healthLineSizeMultiplier));
            Add("speechHue", I(speechHue));
            Add("whisperHue", I(whisperHue));
            Add("emoteHue", I(emoteHue));
            Add("yellHue", I(yellHue));
            Add("partyMessageHue", I(partyMessageHue));
            Add("ignoreGuildMessages", B(ignoreGuildMessages));
            Add("ignoreAllianceMessages", B(ignoreAllianceMessages));
            Add("showSystemMessagesInJournal", B(showSystemMessagesInJournal));
            Add("highlightContainerWhenSelected", B(highlightContainerWhenSelected));
            Add("mobileHPType", I(mobileHPType));
            Add("showTargetIndicator", B(showTargetIndicator));
            Add("autoOpenCorpses", B(autoOpenCorpses));
            Add("autoOpenCorpseRange", I(autoOpenCorpseRange));
            Add("corpseOpenOptions", I(corpseOpenOptions));
            Add("hueContainerGumps", B(hueContainerGumps));
            Add("holdDownKeyTab", B(holdDownKeyTab));
            Add("autoOpenDoors", B(autoOpenDoors));
            Add("autoOpenDoorsIfHidden", B(autoOpenDoorsIfHidden));
            Add("gridContainersDefaultToOldStyleView", B(gridContainersDefaultToOldStyleView));
            Add("containerOpacity", I(containerOpacity));
            Add("gridContainersScale", I(gridContainersScale));
            Add("gridContainerScaleItems", B(gridContainerScaleItems));
            Add("nameOverheadToggled", B(nameOverheadToggled));
            Add("nameOverheadFlags", I(nameOverheadFlags));
            Add("namePlateHealthBar", B(namePlateHealthBar));
            Add("namePlateUseNotorietyText", B(namePlateUseNotorietyText));
            Add("namePlateShowMissingHealth", B(namePlateShowMissingHealth));
            Add("namePlateHealthBarOpacity", I(namePlateHealthBarOpacity));
            Add("namePlateHideAtFullHealth", B(namePlateHideAtFullHealth));
            Add("namePlateHideAtFullHealthInWarmode", B(namePlateHideAtFullHealthInWarmode));
            Add("namePlateUseFixedWidth", B(namePlateUseFixedWidth));
            Add("namePlateFixedWidth", I(namePlateFixedWidth));
            Add("namePlateUseFixedHealthBarWidth", B(namePlateUseFixedHealthBarWidth));
            Add("namePlateHealthBarFixedWidth", I(namePlateHealthBarFixedWidth));
            Add("namePlateHeight", I(namePlateHeight));
            Add("namePlateSplitHealthBar", B(namePlateSplitHealthBar));
            Add("namePlateHealthBarMode", I(namePlateHealthBarMode));
            Add("namePlateBackgroundMode", I(namePlateBackgroundMode));
            Add("namePlateBorderOpacity", I(namePlateBorderOpacity));
            Add("namePlateOpacity", I(namePlateOpacity));
            Add("namePlateBackgroundR", I(namePlateBackgroundR));
            Add("namePlateBackgroundG", I(namePlateBackgroundG));
            Add("namePlateBackgroundB", I(namePlateBackgroundB));
            Add("namePlateAvoidOverlap", B(namePlateAvoidOverlap));
            if (unknown != null)
                foreach (var kv in unknown)
                    Add(FlatJson.Escape(kv.Key), kv.Value);
            sb.Length -= 2; // the last ",\n"
            return sb.Append("\n}\n").ToString();
        }
    }

    /// <summary>
    /// The options window's working copy (TazUO's ModernOptionsGump edits the profile straight away; here edits wait):
    /// controls change <see cref="Draft"/>; <see cref="Apply"/> puts it into the game now, <see cref="Okay"/> applies and
    /// saves, <see cref="Cancel"/> drops it, <see cref="Defaults"/> resets it to the code defaults. Nothing is written
    /// except by <see cref="Okay"/>.
    /// </summary>
    public sealed class OptionsDraft
    {
        private readonly Func<GameOptions> _read;
        private readonly Action<GameOptions> _apply;
        private readonly Action<GameOptions> _save;

        /// <summary><paramref name="read"/> captures the game's options as they are now (keys may have changed them).</summary>
        public OptionsDraft(Func<GameOptions> read, Action<GameOptions> apply, Action<GameOptions> save)
        {
            _read = read;
            _apply = apply;
            _save = save;
            Draft = _read().Clone().Clamp();
        }

        public GameOptions Draft { get; private set; }

        /// <summary>The draft differs from what the game uses now.</summary>
        public bool Changed => !Draft.SameAs(_read());

        public void Apply()
        {
            Draft.Clamp();
            _apply(Draft.Clone());
            Draft = _read().Clone().Clamp();
        }

        public void Okay()
        {
            Apply();
            _save(Draft.Clone());
        }

        public void Cancel() => Draft = _read().Clone().Clamp();

        public void Defaults() => Draft = GameOptions.Defaults();
    }

    /// <summary>A flat JSON object's members as raw value text (strings keep their quotes); nested values are kept whole.</summary>
    internal static class FlatJson
    {
        public static bool TryRead(string s, out List<(string Key, string Raw)> fields)
        {
            fields = new List<(string, string)>();
            int i = 0;
            Skip(s, ref i);
            if (i >= s.Length || s[i] != '{')
                return false;
            i++;
            Skip(s, ref i);
            if (i < s.Length && s[i] == '}')
                return true;
            while (i < s.Length)
            {
                Skip(s, ref i);
                int keyStart = i;
                if (!ScanString(s, ref i))
                    return false;
                string key = Unescape(s.Substring(keyStart + 1, i - keyStart - 2));
                Skip(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    return false;
                i++;
                Skip(s, ref i);
                int valueStart = i;
                if (!ScanValue(s, ref i))
                    return false;
                fields.Add((key, s.Substring(valueStart, i - valueStart).Trim()));
                Skip(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') return true;
                return false;
            }
            return false;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        private static bool ScanString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
                return false;
            for (i++; i < s.Length; i++)
            {
                if (s[i] == '\\') i++;
                else if (s[i] == '"') { i++; return true; }
            }
            return false;
        }

        private static bool ScanValue(string s, ref int i)
        {
            if (i >= s.Length)
                return false;
            if (s[i] == '"')
                return ScanString(s, ref i);
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == '"') { if (!ScanString(s, ref i)) return false; continue; }
                    if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') depth--;
                    i++;
                    if (depth == 0) return true;
                }
                return false;
            }
            int start = i;
            while (i < s.Length && s[i] != ',' && s[i] != '}' && !char.IsWhiteSpace(s[i]))
                i++;
            return i > start;
        }

        private static string Unescape(string s) => s.Replace("\\\"", "\"").Replace("\\\\", "\\");

        public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
