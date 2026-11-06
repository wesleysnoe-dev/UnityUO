// The UO data folder comes from ClientPaths / ClientSettings; there is no uoPath field anymore.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UOClient.ArtPipeline;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;
using UOClient.Net;

namespace UOClient.Unity
{
    /// <summary>
    /// The world demo. Online (default): logs in to the ModernUO server from
    /// UserSettings/uoclient-account.json and plays a character, with server-confirmed movement and
    /// everyone else in view. Offline: walks a local character around the same art.
    /// The map streams in 16x16-tile chunks built on a background thread, from baked ComfyUI art
    /// where it exists, else upscaled original art.
    /// Controls: WASD / arrows to walk (Shift runs), hold right mouse to walk toward the cursor,
    /// scroll to zoom, Enter to talk, T to override time of day, R to toggle roofs, H to hide the HUD,
    /// F3 for frame-time stats.
    /// </summary>
    public sealed class WorldDemo : MonoBehaviour, IGameOptions, ICaptureHost
    {
        public const string CaptureRequestFile = "Library/UOClient/capture-demo";

        [Header("Data")]
        [Tooltip("Folder from `uoclient bake`; relative paths are from the project root. Empty = original art only.")]
        public string bakedFolder = "Exports/comfy_test/baked";

        [Header("Server")]
        [Tooltip("Log in to the server. Off: offline demo. The server and the UO data folder come from UserSettings/unity-uo-settings.json and the -server/-uodata/-overrides arguments.")]
        public bool online = true;
        [Tooltip("Account file (account, password, character); relative to the project root.")]
        public string accountFile = "UserSettings/uoclient-account.json";

        /// <summary>Server, data folder and client overrides in use (the settings file plus launch arguments).</summary>
        public ClientSettings Settings { get; private set; }

        /// <summary>The settings file as it is (no launch arguments), or null if there is none; for the editor windows too.</summary>
        [Obsolete("Use ClientSettingsStore.ReadSettingsFile.")]
        public static ClientSettings ReadSettingsFile(bool orDefaults = true) => ClientSettingsStore.ReadSettingsFile(orDefaults);

        /// <summary>The client overrides for a data set opened by an editor tool, when the settings name a client folder.</summary>
        [Obsolete("Use ClientSettingsStore.ApplyOverrides.")]
        public static void ApplyOverrides(UOData data, ClientSettings s) => ClientSettingsStore.ApplyOverrides(data, s);

        /// <summary>Reads the settings file (writing one with the detected folders on first run), then applies launch arguments.</summary>
        private ClientSettings LoadSettings() => ClientSettingsStore.Load();

        [Header("Offline start")]
        public int map;
        public int centerX = 1467;
        public int centerY = 1671;
        [Tooltip("Art scale when there is no baked folder.")]
        [Range(1, 4)] public int scale = 2;
        public string characterName = "Adventurer";

        [Header("Streaming")]
        [Tooltip("Tiles around the player kept loaded.")]
        [Range(16, 96)] public int loadRadius = 40;

        [Header("View")]
        [Range(0.25f, 4f)] public float zoom = 1f;

        // Data, art, world, camera, HUD, session and player avatar.
        private readonly GameContext _ctx = new GameContext();
        private Func<IsoCompositor> _compositorFactory;
        private CameraRig _cameraRig;
        private int _bakedCount;
        private readonly RoofController _roofs = new RoofController();
        private bool _ready;
        private string _loadError;
        private readonly MinimapUpdater _minimap = new MinimapUpdater();
        private readonly FrameStats _frames = new FrameStats();
        private bool _showStats;

        // Online
        private EntityViews _entities;
        private readonly ChatLine _chatLine = new ChatLine();
        private PlayerMovement _movement;
        private HudPresenter _hudPresenter;
        private WorldInput _input;

        private readonly Lighting _lighting = new Lighting();

        private void Start()
        {
            _ctx.Root = transform;
            _entities = new EntityViews(_ctx) { PlayerNamed = name => characterName = name };
            _movement = new PlayerMovement(_ctx);
            _hudPresenter = new HudPresenter(_ctx, _entities);
            _input = new WorldInput(_ctx, _entities, _tooltip, _chatLine, _lighting, _roofs, () => _ui, () => Hotkeys);
            _effects = new EffectsLayer(MakeEffectView);
            _ctx.Camera = CameraRig.EnsureMainCamera();
            _cameraRig = new CameraRig(_ctx.Camera);
            _ctx.Hud = new DemoHud();
            Lighting.ApplyDefault();
            // The options saved from the options window (read only; with none the code defaults stand).
            if (GameOptionsStore.TryLoad() is GameOptions saved)
                ApplyOptions(saved);

            try
            {
                Settings = LoadSettings();
                _ctx.Data = new UOData(Settings.ResolvedDataPath);
                string overrides = Settings.ResolvedOverridesPath;
                if (overrides != null)
                {
                    var o = new ClientOverrides(overrides, Settings.hdArt);
                    _ctx.Data.UseOverrides(o);
                    Debug.Log($"UOClient: client overrides from {overrides}: {o.ArtCount} art, {o.LandCount} land, {o.GumpCount} gumps, " +
                              $"{o.TileRowCount} tiledata rows, {o.EquipConvCount} equipment conversions{(o.Problems.Count > 0 ? $"; {o.Problems.Count} problems, first: {o.Problems[0]}" : "")}");
                }
                Debug.Log($"UOClient: UO data {Settings.ResolvedDataPath}; server {Settings.Server}");
                _ctx.Shader = Shader.Find("UOClient/WorldSprite");
                if (_ctx.Shader == null)
                    throw new InvalidOperationException("Shader UOClient/WorldSprite not found.");
                SetUpArt();

                // Unattended runs log straight in with the account file; people get the login screen.
                LaunchOptions launch = LaunchOptions.FromCommandLine();
                bool unattended = File.Exists(Path.Combine(ProjectRoot, CaptureRequestFile))
                                  || launch.CaptureFromCommandLine
                                  || launch.AutoLogin;
                // -offline <map> <x> <y>: the offline demo at an exact spot (indoors included), e.g. to check a building.
                if (launch.OfflineAt is (int om, int ox, int oy))
                {
                    StartOfflineAt(om, ox, oy);
                }
                else if (online && !unattended)
                {
                    ShowLoginScreen();
                    string loginShots = Path.Combine(ProjectRoot, LoginCaptureRequestFile);
                    if (File.Exists(loginShots))
                    {
                        File.Delete(loginShots);
                        StartCoroutine(new LoginTour(_ctx, _login).Run());
                    }
                }
                else
                {
                    LoginSettings login = online ? Accounts.ReadLogin(Settings) : null;
                    if (login != null)
                        StartOnline(login);
                    else
                        StartOffline();
                }
            }
            catch (Exception e)
            {
                _loadError = e.Message;
                Debug.LogException(e);
            }

            // Unattended run: a request file (editor), or -uoclient-capture on a player's command line (quits when done).
            string request = Path.Combine(ProjectRoot, CaptureRequestFile);
            bool fromCommandLine = LaunchOptions.FromCommandLine().CaptureFromCommandLine;
            if (File.Exists(request) || fromCommandLine)
            {
                if (File.Exists(request))
                    File.Delete(request);
                StartCoroutine(new CaptureTour(_ctx, this).Run(quitWhenDone: fromCommandLine && !Application.isEditor));
            }
        }

        private void SetUpArt()
        {
            string bakedDir = string.IsNullOrEmpty(bakedFolder) ? null
                : Path.IsPathRooted(bakedFolder) ? bakedFolder : Path.Combine(ProjectRoot, bakedFolder);
            if (bakedDir != null && File.Exists(Path.Combine(bakedDir, "placements.bin")))
            {
                BakedRegion probe = BakedRegion.Open(bakedDir);
                _ctx.ArtScale = probe.Scale;
                _bakedCount = probe.Count;
                // The chunk worker gets its own compositor and baked-art reader (neither is thread-safe).
                _compositorFactory = () =>
                {
                    BakedRegion region = BakedRegion.Open(bakedDir);
                    return new IsoCompositor(_ctx.Data) { Scale = region.Scale, PlacementOverride = region.Get };
                };
            }
            else
            {
                _ctx.ArtScale = scale;
                _compositorFactory = () => new IsoCompositor(_ctx.Data) { Scale = scale };
            }
            _ctx.MobileArt = new MobileArt(_ctx.Data, _ctx.ArtScale);
        }

        private void CreateWorld(int mapIndex, int x, int y)
        {
            _ctx.World?.Dispose();
            _ctx.World = new WorldStreamer(_ctx.Data, _ctx.Root, _ctx.Shader, _ctx.ArtScale, _compositorFactory, mapIndex, x, y) { LoadRadius = loadRadius };
            _ctx.Walk = new Walkability(_ctx.Data, mapIndex);
            _ctx.Hud.Summary = $"streaming {WorldStreamer.ChunkSize}x{WorldStreamer.ChunkSize} chunks, radius {loadRadius}, {_bakedCount} baked placements available, {_ctx.ArtScale}x art";
        }

        // ---- offline ----

        private void StartOffline()
        {
            CreateWorld(map, centerX, centerY);
            Vector2Int start = FindTile(centerX + 2, centerY + 9, underRoof: false);
            int z = _ctx.Walk.TryStep(start.x, start.y, 127) ?? 0;

            int robe = HueApplier.FindClosest(_ctx.Data.Hues, 40, 62, 150);
            int hair = HueApplier.FindClosest(_ctx.Data.Hues, 96, 62, 34);
            MobileLook look = MobileLook.From(_ctx.Data, 400, 0x03EA, new (byte, int, int)[] { (22, 0x1F03, robe), (11, 0x203B, hair) });
            _ctx.Player = new Avatar(_ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, look, start.x, start.y, z, characterName);

            _ctx.Hud.Log("Lord British", "Welcome to Britannia!");
            _ctx.Hud.Log(null, "Offline demo. WASD or hold right mouse to walk, Shift to run. T: time of day, R: roofs, H: HUD, F3: stats.");
        }

        private void StartOfflineAt(int mapIndex, int x, int y)
        {
            CreateWorld(mapIndex, x, y);
            int land = _ctx.Walk.AverageLandZ(x, y);
            int z = _ctx.Walk.TryStep(x, y, land) ?? land; // the ground floor: what is reachable from the land, not the floor above
            MobileLook look = MobileLook.From(_ctx.Data, 400, 0x03EA, new (byte, int, int)[] { (22, 0x1F03, 0x0021), (11, 0x203B, 0x044E) });
            _ctx.Player = new Avatar(_ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, look, x, y, z, characterName);
            _ctx.Hud.Log(null, $"Offline at {x},{y},{z} on map {mapIndex}.");
        }

        // ---- online ----

        private LoginFlow _login;

        // Built on use, so it always sees the current accountFile (as the inline code it replaced did).
        private AccountStore Accounts => new AccountStore(accountFile);

        private void ShowLoginScreen()
        {
            _gumpArt ??= new UI.GumpArt(_ctx.Data);
            _login = new LoginFlow(_ctx, _gumpArt, () => Accounts, Settings, StartOnline, () =>
            {
                EndLoginScreen();
                Quit();
            })
            {
                VideoFrame = () => _loginMedia != null ? _loginMedia.VideoFrame : null,
                OptionsPressed = () => _loginUi?.Open(new UI.OptionsWindow(this)),
            };
            _login.Show();
            StartLoginMedia();
        }

        // ---- login video and music ----

        private LoginMedia _loginMedia;
        private UI.UiManager _loginUi;

        /// <summary>
        /// Starts the login video and music (again after a failed login); already playing, nothing changes. Also the
        /// login screen's own window manager: only the options window opens there, and it goes (unsaved draft and all)
        /// with <see cref="EndLoginScreen"/>.
        /// </summary>
        private void StartLoginMedia()
        {
            _loginUi ??= new UI.UiManager(null, _gumpArt, null);
            if (_loginMedia == null)
                _loginMedia = gameObject.AddComponent<LoginMedia>();
            _loginMedia.MusicVolume = _musicVolume;
            _loginMedia.Begin(_ctx.Data.Directory);
        }

        /// <summary>Entering the world, or quitting: the video and login music stop and are freed, the options window goes.</summary>
        private void EndLoginScreen()
        {
            _loginMedia?.End();
            _loginUi = null;
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---- gumps ----

        private UI.GumpArt _gumpArt;
        private SessionUiBinder _binder;
        private UI.UiManager _ui => _binder?.Ui;
        private UI.ChatLog _chatLog => _binder?.ChatLog;

        /// <summary>Windows the server opens: paperdolls, containers and gumps.</summary>
        private void SetUpUi()
        {
            _gumpArt ??= new UI.GumpArt(_ctx.Data);
            UI.GumpArt art = _gumpArt;
            _hudPresenter.HueColor = hue => art.HueColor(hue, DemoHud.PercentFallbackColor(hue));
            _binder?.Dispose();
            _binder = new SessionUiBinder(_ctx, this, _gumpArt, _input, _cursor, _overheads, _effects, _chatLine, Sound);
            _binder.Attach(_ctx.Session);
            _sound = _binder.Sound; // kept for the next session's binder (the component is added once)
            ApplyAudioOptions(); // the session's Audio exists from here
            ApplyCombatOptions();
            _journalFile.Attach(_ctx.Session.World, () => characterName); // a new log file per session, if saving is on
        }

        /// <summary>Sound effects and music (null until online).</summary>
        public Audio Sound => _binder?.Sound ?? _sound;

        private Audio _sound;

        // ---- effects ----

        private EffectsLayer _effects;

        private EffectView MakeEffectView(GraphicEffect e) =>
            _ctx.World == null ? null : new EffectView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, e, _entities.Locate);

        /// <summary>The height upper floors are cut at right now (null outdoors).</summary>
        public int? CutZ => _roofs.CutZ;

        /// <summary>The paperdoll's buttons.</summary>
        private void OnUiCommand(string command) => _binder?.RunCommand(command);

        /// <summary>Keyboard macros (null until online).</summary>
        public Hotkeys Hotkeys => _binder?.Hotkeys;

        // ---- options the options window changes ----

        public bool ShowNames { get => _ctx.Hud.NamesVisible; set => _ctx.Hud.NamesVisible = value; }
        public bool ShowHud { get => _ctx.Hud.Visible; set => _ctx.Hud.Visible = value; }
        public bool ShowJournal { get => _ctx.Hud.JournalVisible; set => _ctx.Hud.JournalVisible = value; }
        public bool AutoRoofs { get => _roofs.AutoRoofs; set => _roofs.AutoRoofs = value; }
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, GameOptions.MinZoom, GameOptions.MaxZoom); }

        // Kept here until the session's Audio exists, then pushed to it (SetUpUi).
        private float _soundVolume = GameOptions.Defaults().soundVolume, _musicVolume = GameOptions.Defaults().musicVolume;

        // Kept here until a session exists, then pushed to it (SetUpUi): each login makes a new GameSession.
        private bool _criminalActionQuery = GameOptions.Defaults().criminalActionQuery;
        private bool _beneficialCriminalActionQuery = GameOptions.Defaults().beneficialCriminalActionQuery;

        public GameOptions CurrentOptions() => new GameOptions
        {
            showNames = ShowNames,
            showJournal = ShowJournal,
            showHud = ShowHud,
            autoRoofs = AutoRoofs,
            zoom = Zoom,
            uiScale = UI.UiScale.Setting,
            highlightGameObjects = ObjectHighlight.HighlightGameObjects,
            tooltipDelay = _tooltip.Delay,
            soundVolume = Sound != null ? Sound.SoundVolume : _soundVolume,
            musicVolume = Sound != null ? Sound.MusicVolume : _musicVolume,
            criminalActionQuery = _ctx.Session != null ? _ctx.Session.CriminalActionQuery : _criminalActionQuery,
            beneficialCriminalActionQuery = _ctx.Session != null ? _ctx.Session.BeneficialCriminalActionQuery : _beneficialCriminalActionQuery,
            alwaysRun = _movement.AlwaysRun,
            alwaysRunUnlessHidden = _movement.AlwaysRunUnlessHidden,
            scaleSpeechDelay = _overheads.ScaleSpeechDelay,
            speechDelay = _overheads.SpeechDelay,
            saveJournalToFile = _journalFile.Enabled,
            containersScale = UI.ContainerWindow.ScalePercent,
            doubleClickToLootInsideContainers = UI.ContainerWindow.DoubleClickToLoot,
            holdShiftToSplitStack = UI.UiManager.HoldShiftToSplitStack,
            useTooltip = _tooltip.UseTooltip,
            buffBarTime = UI.BuffBarWindow.ShowTimers,
            castSpellsByOneClick = UI.SpellIconWindow.SingleClickIconUse,
            showMobilesHP = _hudPresenter.ShowMobilesHP,
            mobileHPShowWhen = _hudPresenter.MobileHPShowWhen,
            healthLineSizeMultiplier = _ctx.Hud.HealthLineSize,
            speechHue = _speechOptions.Speech,
            whisperHue = _speechOptions.Whisper,
            emoteHue = _speechOptions.Emote,
            yellHue = _speechOptions.Yell,
            partyMessageHue = _speechOptions.Party,
            ignoreGuildMessages = _speechOptions.IgnoreGuildMessages,
            ignoreAllianceMessages = _speechOptions.IgnoreAllianceMessages,
            showSystemMessagesInJournal = _showSystemMessagesInJournal,
            highlightContainerWhenSelected = UI.ContainerWindow.HighlightWhenSelected,
            mobileHPType = _hudPresenter.MobileHPType,
            showTargetIndicator = _hudPresenter.ShowTargetIndicator,
            autoOpenCorpses = _corpseAutoOpen.Enabled,
            autoOpenCorpseRange = _corpseAutoOpen.Range,
            corpseOpenOptions = _corpseAutoOpen.Options,
            hueContainerGumps = UI.ContainerWindow.HueGumps,
            holdDownKeyTab = _input.HoldDownKeyTab,
            autoOpenDoors = _doorAutoOpen.Enabled,
            autoOpenDoorsIfHidden = _doorAutoOpen.IfHidden,
            gridContainersDefaultToOldStyleView = UI.GridContainerWindow.DefaultToOldStyleView,
            containerOpacity = UI.GridContainerWindow.Opacity,
            gridContainersScale = UI.GridContainerWindow.ScalePercent,
            gridContainerScaleItems = UI.GridContainerWindow.ScaleItems,
        };

        public void ApplyOptions(GameOptions options)
        {
            GameOptions o = options.Clone().Clamp();
            ShowNames = o.showNames;
            ShowJournal = o.showJournal;
            ShowHud = o.showHud;
            if (AutoRoofs != o.autoRoofs) // setting it also drops an R-key roof choice: only when it changes
                AutoRoofs = o.autoRoofs;
            Zoom = o.zoom;
            UI.UiScale.Setting = o.uiScale;
            ObjectHighlight.HighlightGameObjects = o.highlightGameObjects;
            _tooltip.Delay = o.tooltipDelay;
            _soundVolume = o.soundVolume;
            _musicVolume = o.musicVolume;
            ApplyAudioOptions();
            _criminalActionQuery = o.criminalActionQuery;
            _beneficialCriminalActionQuery = o.beneficialCriminalActionQuery;
            ApplyCombatOptions();
            _movement.AlwaysRun = o.alwaysRun;
            _movement.AlwaysRunUnlessHidden = o.alwaysRunUnlessHidden;
            _overheads.ScaleSpeechDelay = o.scaleSpeechDelay;
            _overheads.SpeechDelay = o.speechDelay;
            _journalFile.Enabled = o.saveJournalToFile;
            UI.ContainerWindow.ScalePercent = o.containersScale;
            UI.ContainerWindow.DoubleClickToLoot = o.doubleClickToLootInsideContainers;
            UI.UiManager.HoldShiftToSplitStack = o.holdShiftToSplitStack;
            _tooltip.UseTooltip = o.useTooltip;
            UI.BuffBarWindow.ShowTimers = o.buffBarTime;
            UI.SpellIconWindow.SingleClickIconUse = o.castSpellsByOneClick;
            _hudPresenter.ShowMobilesHP = o.showMobilesHP;
            _hudPresenter.MobileHPShowWhen = o.mobileHPShowWhen;
            _ctx.Hud.HealthLineSize = o.healthLineSizeMultiplier;
            // The session reads this same object, so a change reaches the one online now too.
            _speechOptions.Speech = (ushort)o.speechHue;
            _speechOptions.Whisper = (ushort)o.whisperHue;
            _speechOptions.Emote = (ushort)o.emoteHue;
            _speechOptions.Yell = (ushort)o.yellHue;
            _speechOptions.Party = (ushort)o.partyMessageHue;
            _speechOptions.IgnoreGuildMessages = o.ignoreGuildMessages;
            _speechOptions.IgnoreAllianceMessages = o.ignoreAllianceMessages;
            _showSystemMessagesInJournal = o.showSystemMessagesInJournal;
            UI.ContainerWindow.HighlightWhenSelected = o.highlightContainerWhenSelected;
            _hudPresenter.MobileHPType = o.mobileHPType;
            _hudPresenter.ShowTargetIndicator = o.showTargetIndicator;
            _corpseAutoOpen.Enabled = o.autoOpenCorpses;
            _corpseAutoOpen.Range = o.autoOpenCorpseRange;
            _corpseAutoOpen.Options = o.corpseOpenOptions;
            UI.ContainerWindow.HueGumps = o.hueContainerGumps;
            _input.HoldDownKeyTab = o.holdDownKeyTab;
            _doorAutoOpen.Enabled = o.autoOpenDoors;
            _doorAutoOpen.IfHidden = o.autoOpenDoorsIfHidden;
            UI.GridContainerWindow.DefaultToOldStyleView = o.gridContainersDefaultToOldStyleView;
            UI.GridContainerWindow.Opacity = o.containerOpacity;
            UI.GridContainerWindow.ScalePercent = o.gridContainersScale;
            UI.GridContainerWindow.ScaleItems = o.gridContainerScaleItems;
        }

        // The options' AutoOpenCorpses: kept across logins, each new session starts with nothing opened (Tick).
        private readonly Core.CorpseAutoOpen _corpseAutoOpen = new Core.CorpseAutoOpen();

        // The options' AutoOpenDoors / AutoOpenDoorsIfHidden (Tick on each step or turn).
        private readonly Core.DoorAutoOpen _doorAutoOpen = new Core.DoorAutoOpen();
        private Func<ushort, bool> _isDoor;

        // Handed to each new session (SetUpUi); the options change it in place.
        private readonly SpeechSettings _speechOptions = new SpeechSettings();
        private bool _showSystemMessagesInJournal = GameOptions.Defaults().showSystemMessagesInJournal;

        // ---- journal log (the options' SaveJournalToFile) ----

        private readonly JournalFile _journalFile = new JournalFile();

        public void SaveOptions(GameOptions options) => GameOptionsStore.Save(options);

        private void ApplyAudioOptions()
        {
            if (_loginMedia != null)
                _loginMedia.MusicVolume = _musicVolume; // the login music, while it plays
            if (Sound == null)
                return;
            Sound.SoundVolume = _soundVolume;
            Sound.MusicVolume = _musicVolume;
        }

        private void ApplyCombatOptions()
        {
            if (_ctx.Session == null)
                return;
            _ctx.Session.CriminalActionQuery = _criminalActionQuery;
            _ctx.Session.BeneficialCriminalActionQuery = _beneficialCriminalActionQuery;
            _ctx.Session.SpeechOptions = _speechOptions;
        }

        // ---- cursor ----

        private readonly UI.TargetCursor _cursor = new UI.TargetCursor();
        private readonly MultiPlacementGhost _placement = new MultiPlacementGhost();

        // ---- overhead text ----

        private readonly OverheadText _overheads = new OverheadText();

        // ---- tooltips ----

        private readonly UI.HoverTooltip _tooltip = new UI.HoverTooltip();

        private void StartOnline(LoginSettings login)
        {
            if (_ctx.Session != null)
                TearDownSession(); // a session still live (never expected from the login screen): it goes first
            characterName = login.CharacterName;
            _ctx.Session = new GameSession(login);
            // Every handler below is guarded: once the session ends (TearDownSession) none of them runs again.
            _life.Begin(_ctx.Session);
            string cliloc = Path.Combine(_ctx.Data.Directory, "Cliloc.enu");
            if (File.Exists(cliloc))
                _ctx.Session.Cliloc = new Cliloc(cliloc);
            string speech = Path.Combine(_ctx.Data.Directory, "speech.mul");
            if (File.Exists(speech))
            {
                _ctx.Session.SpeechKeywords = new SpeechKeywords(speech);
                Debug.Log($"UOClient: speech.mul loaded ({_ctx.Session.SpeechKeywords.Count} keywords)");
            }
            else
                Debug.LogError($"UOClient: speech.mul not found at {speech}; speech goes out untagged and bankers, stablemasters and vendors will ignore keywords.");

            _ctx.Session.StatusChanged += _life.Guard<string>(s => _ctx.Hud.ConnectionStatus = s);
            _ctx.Session.Entered += _life.Guard(OnEnteredWorld);
            // The server's first region music can come before the login completes: the login music is gone by then.
            _ctx.Session.MusicRequested += _life.Guard<int>(_ => EndLoginScreen());
            SetUpUi();
            _ctx.Session.MoveRejected += _life.Guard<int, int, sbyte, byte>((x, y, z, d) =>
            {
                _ctx.Player?.Teleport(x, y, z);
                _ctx.Player?.Face(d);
            });
            _ctx.Session.World.JournalAdded += _life.Guard<JournalEntry>(j =>
            {
                if (GameOptions.JournalShowsLine(_showSystemMessagesInJournal, j.Serial))
                    _ctx.Hud.Log(j.Speaker, j.Text);
            });
            _entities.Attach(_ctx.Session);
            CustomHouses.Attach(_ctx.Session, _life.Guard<WorldItem>(h => { if (_ctx.World != null) _entities.ShowMulti(h); }),
                                _life.Guard<string>(s => _ctx.Hud.Log(null, s)));

            _ctx.Hud.ConnectionStatus = $"Connecting to {login.Host}:{login.Port}...";
            try
            {
                _ctx.Session.Start();
            }
            catch (Exception e)
            {
                TearDownSession();
                if (login.Interactive && _login != null)
                {
                    _login.Failed($"Could not reach {login.Host}:{login.Port} ({e.Message}).");
                    StartLoginMedia();
                    return;
                }
                Debug.LogWarning($"UOClient: could not reach the server ({e.Message}); running the offline demo.");
                StartOffline();
            }
        }

        private void OnEnteredWorld()
        {
            EndLoginScreen();
            Core.Mobile p = _ctx.Session.World.Player;
            CreateWorld(_ctx.Session.World.Map, p.X, p.Y);
            _entities.SpawnPlayer(p, p.Name ?? characterName);
            if (!string.IsNullOrEmpty(p.Name))
                characterName = p.Name;

            _entities.SyncAll();

            _ctx.Hud.HasStats = true;
            _ctx.Hud.Log(null, $"Connected as {characterName}. Enter to talk, F3 for frame stats.");
        }

        // ---- the session's end ----

        private readonly SessionLifecycle _life = new SessionLifecycle();

        /// <summary>
        /// Frees everything built for the session (its windows and their bindings, entity and effect views, overheads, the
        /// chat line, the world and the player) and disposes it; its guarded handlers go quiet. Returns what the login
        /// screen should say, or null. Safe to call again, or with no session.
        /// </summary>
        private string TearDownSession()
        {
            string message = _life.End();
            _ctx.Session?.Dispose(); // already disposed when it was the lifecycle's; harmless again
            _ctx.Session = null;
            _journalFile.Attach(null, null);
            if (_binder != null)
            {
                _sound = _binder.Sound;
                _binder.Dispose();
                _binder = null; // its windows, chat log and macros go with it
            }
            _sound?.PlayMusic(Audio.StopMusicId); // the region's music; the login music comes back with the login screen
            _entities.Reset();
            _effects.Dispose();
            _overheads.Clear();
            _chatLine.Close();
            _placement.Clear();
            _ctx.Player?.Dispose();
            _ctx.Player = null;
            _ctx.World?.Dispose();
            _ctx.World = null;
            _ctx.Walk = null;
            _ready = false;
            _cursor.PlayerScreen = null;
            _cursor.Update(null, null); // the system pointer again (no target crosshair left behind)
            _ctx.Hud.ConnectionStatus = "";
            _ctx.Hud.HasStats = false;
            _ctx.Hud.WarMode = false;
            _ctx.Hud.DeathScreenUntil = 0;
            _ctx.Hud.TopInset = 0;
            return message;
        }

        /// <summary>The session ended: back to the login screen (its video and music again), saying why.</summary>
        private void ReturnToLogin()
        {
            string message = TearDownSession();
            Debug.Log($"UOClient: session ended ({message ?? "closed by the client"}); back to the login screen.");
            if (_login == null)
                ShowLoginScreen(); // an unattended login has none yet; it never logs in again by itself
            else
                StartLoginMedia(); // stopped once the server's music came
            if (message != null)
                _login.Failed(message);
        }

        // ---- frame loop ----

        private void Update()
        {
            _frames.Add(Time.unscaledDeltaTime);
            _sections.EndFrame(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.F3))
                _showStats = !_showStats;

            _sections.Begin("network");
            _ctx.Session?.Update(Time.realtimeSinceStartupAsDouble);

            // The session ended: dropped before the world (bad password, server down, cancelled), lost in it, or
            // logged out. The world and everything of the session goes, and the login form says why; the player logs
            // in again from there (nothing reconnects by itself).
            if (_life.HasEnded)
                ReturnToLogin();

            if (_ctx.World == null || _ctx.Player == null)
                return;

            if (_ctx.Session != null && _ctx.Session.World.Map != _ctx.World.Map)
            {
                _ctx.World.SetMap(_ctx.Session.World.Map);
                _ctx.Walk = new Walkability(_ctx.Data, _ctx.Session.World.Map);
                _ready = false;
            }

            _sections.Begin("chunks");
            _ctx.World.Update(_ctx.Player.X, _ctx.Player.Y);
            _sections.Begin("minimap");
            _minimap.Update(_ctx.Data, _ctx.World.Map, _ctx.Player.X, _ctx.Player.Y, _ctx.Hud);

            // Show the world once the chunks around the player are in; until then keep loading.
            if (!_ready)
            {
                if (!_ctx.World.IsReadyAround(_ctx.Player.X, _ctx.Player.Y))
                    return;
                _ready = true;
            }

            _sections.Begin("input");
            HandleInput();
            _input.Update();
            _corpseAutoOpen.Tick(_ctx.Session, _ctx.Player.X, _ctx.Player.Y);
            _isDoor ??= g => (_ctx.Data.TileData.GetStatic(g).Flags & TileFlags.Door) != 0;
            _doorAutoOpen.Tick(_ctx.Session, _ctx.Player.X, _ctx.Player.Y, _ctx.Player.Z, _ctx.Player.Direction, _isDoor);
            _ctx.Player.Tick(Time.deltaTime);
            _entities.Tick(Time.deltaTime);

            bool hideUpperMultiParts = _roofs.Apply(_ctx.World, _ctx.Walk, _ctx.Player);

            _sections.Begin("art");
            _ctx.MobileArt.Flush();
            _sections.Begin("sprites");
            _ctx.Player.Render(_ctx.World);
            _entities.Render(hideUpperMultiParts);
            _placement.Update(_ctx.Session, _ctx.Data, _ctx.World, p => new ItemView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, p), _input.MouseTile);
            _effects.Update(_ctx.World);

            _sections.Begin("camera+hud");
            _cameraRig.Follow(_ctx.Player.AnchorPixels, _ctx.World, _ctx.ArtScale, zoom);
            _lighting.Update(_ctx.Session, _ctx.World.ToUnity(_ctx.Player.AnchorPixels), _ctx.ArtScale, _ctx.Hud);
            _hudPresenter.Update();
            _overheads.Update(_ctx.Hud, _entities.DrawnRect, _ctx.Camera);
            // The pointer aims from the player, the origin a right-drag walks from (PlayerMovement); off over the journal.
            Vector3 playerScreen = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(_ctx.Player.AnchorPixels));
            _cursor.PlayerScreen = _ctx.Hud.JournalRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))
                ? (Vector2?)null
                : new Vector2(playerScreen.x, Screen.height - playerScreen.y);
            _cursor.Update(_ctx.Session, _ui);
            _sections.Begin(null);
        }

        private readonly FrameSections _sections = new FrameSections();

        private void HandleInput()
        {
            // Typing in the chat line or a gump's text box: keys are text, not commands.
            if (!UI.UiRouting.WorldTakesKeys(_chatLine.Typing, _ui != null && _ui.HasKeyboardFocus))
                return;

            _input.HandleViewKeys(ref zoom);
            _movement.Update(_ui);
        }

        /// <summary>Screen vector (x right, y down) to a UO direction: right = 1 (east-north), down = 3, left = 5, up = 7.</summary>
        public static int DirectionFromScreen(Vector2 v) => PlayerMovement.DirectionFromScreen(v);

        private void OnGUI()
        {
            if (_loadError != null)
            {
                GUI.Label(new Rect(20, 20, Screen.width - 40, 200), "World demo failed to load:\n" + _loadError);
                return;
            }
            if (_login != null && _ctx.World == null)
            {
                _login.Draw();
                if (_loginUi != null)
                {
                    _loginUi.Scale = UI.UiScale.Current;
                    _loginUi.OnGUI();
                }
                return;
            }
            if (!_ready)
            {
                string what = _ctx.World == null
                    ? (_ctx.Session != null ? _ctx.Hud.ConnectionStatus : "Loading Britannia...")
                    : $"Loading the area... ({_ctx.World.LoadedChunks} chunks)";
                GUI.Label(new Rect(20, 20, 600, 30), what);
                return;
            }

            Vector3 head = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(_ctx.Player.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
            _effects.DrawLightningGUI(_gumpArt, _ctx.Camera, _ctx.World, _ctx.ArtScale);
            _ctx.Hud.Draw(characterName, new Vector2(head.x, Screen.height - head.y));
            if (_ui != null)
            {
                _ui.Scale = UI.UiScale.Current;
                _ui.OnGUI();
                _tooltip.OnGUI(_ctx.Session, _ui);
            }
            _chatLine.OnGUI(_ctx.Session, _ui);

            if (_showStats)
            {
                string lastHitch = _sections.Hitches.Count > 0 ? "\nlast hitch: " + _sections.Hitches[_sections.Hitches.Count - 1] : "";
                GUI.Label(new Rect(Screen.width - 620, Screen.height - 90, 610, 80),
                    $"{_frames.Summary()}\nchunks {_ctx.World.LoadedChunks} loaded, {_ctx.World.PendingChunks} pending, worker {_ctx.World.WorkerMs / Math.Max(1, _ctx.World.ChunksBuilt):F1} ms/chunk; " +
                    $"art {_ctx.MobileArt.Built} built, {_ctx.MobileArt.PendingRequests} pending{lastHitch}");
            }
        }

        /// <summary>Nearest walkable tile to (x, y) whose indoor state matches; spirals outward.</summary>
        private Vector2Int FindTile(int x, int y, bool underRoof)
        {
            for (int r = 0; r < 24; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                            continue;
                        int tx = x + dx, ty = y + dy;
                        int? z = _ctx.Walk.TryStep(tx, ty, 127);
                        if (z.HasValue && _ctx.Walk.IsUnderRoof(tx, ty, z.Value) == underRoof)
                            return new Vector2Int(tx, ty);
                    }
            return new Vector2Int(x, y);
        }

        public const string LoginCaptureRequestFile = "Library/UOClient/capture-login";

        // ---- what the capture tour reads ----

        bool ICaptureHost.Ready => _ready;
        string ICaptureHost.LoadError => _loadError;
        int? ICaptureHost.CutZ => CutZ;
        bool ICaptureHost.TryStep(int dir, bool run) => _movement.TryStep(dir, run);
        void ICaptureHost.RunCommand(string cmd) => OnUiCommand(cmd);
        FrameStats ICaptureHost.Frames => _frames;
        FrameSections ICaptureHost.Sections => _sections;
        EntityViews ICaptureHost.Entities => _entities;
        EffectsLayer ICaptureHost.Effects => _effects;
        OverheadText ICaptureHost.Overheads => _overheads;
        UI.HoverTooltip ICaptureHost.Hover => _tooltip;
        SessionUiBinder ICaptureHost.Ui => _binder;

        /// <summary>Written next to a player build: where the project (baked art, account file) lives on this machine.</summary>
        [Obsolete("Use ClientPaths.PathsFileName.")]
        public const string PathsFileName = ClientPaths.PathsFileName;

        /// <summary>See <see cref="ClientPaths.ProjectRoot"/>.</summary>
        private static string ProjectRoot => ClientPaths.ProjectRoot;

        private void OnDestroy()
        {
            _journalFile.Dispose();
            _binder?.Dispose();
            _life.End();
            _ctx.Session?.Dispose();
            _ctx.Player?.Dispose();
            _entities?.Dispose();
            _ctx.World?.Dispose();
            _gumpArt?.Dispose();
            _ctx.MobileArt?.Dispose();
            _ctx.Hud?.Dispose();
            _ctx.Data?.Dispose();
        }
    }
}
