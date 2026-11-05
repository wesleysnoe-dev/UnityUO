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
        public static ClientSettings ReadSettingsFile(bool orDefaults = true) => ClientSettingsStore.ReadSettingsFile(orDefaults);

        /// <summary>The client overrides for a data set opened by an editor tool, when the settings name a client folder.</summary>
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
                        StartCoroutine(LoginTour());
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
                StartCoroutine(CaptureTour(quitWhenDone: fromCommandLine && !Application.isEditor));
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
            _login = new LoginFlow(_ctx, _gumpArt, () => Accounts, Settings, StartOnline, Quit);
            _login.Show();
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
            _binder?.Dispose();
            _binder = new SessionUiBinder(_ctx, this, _gumpArt, _input, _cursor, _overheads, _effects, _chatLine, Sound);
            _binder.LogoutConfirmed += () => _quitAfterLogout = true;
            _binder.Attach(_ctx.Session);
        }

        /// <summary>Sound effects and music (null until online).</summary>
        public Audio Sound => _binder?.Sound;

        // ---- effects ----

        private EffectsLayer _effects;

        private EffectView MakeEffectView(GraphicEffect e) =>
            _ctx.World == null ? null : new EffectView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, e, _entities.Locate);

        private bool _quitAfterLogout;

        /// <summary>The height upper floors are cut at right now (null outdoors).</summary>
        public int? CutZ => _roofs.CutZ;

        /// <summary>The paperdoll's buttons.</summary>
        private void OnUiCommand(string command) => _binder.RunCommand(command);

        /// <summary>Keyboard macros (null until online).</summary>
        public Hotkeys Hotkeys => _binder?.Hotkeys;

        // ---- options the options window changes ----

        public bool ShowNames { get => _ctx.Hud.NamesVisible; set => _ctx.Hud.NamesVisible = value; }
        public bool ShowHud { get => _ctx.Hud.Visible; set => _ctx.Hud.Visible = value; }
        public bool ShowJournal { get => _ctx.Hud.JournalVisible; set => _ctx.Hud.JournalVisible = value; }
        public bool AutoRoofs { get => _roofs.AutoRoofs; set => _roofs.AutoRoofs = value; }
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, 0.25f, 4f); }

        // ---- cursor ----

        private readonly UI.TargetCursor _cursor = new UI.TargetCursor();
        private readonly MultiPlacementGhost _placement = new MultiPlacementGhost();

        // ---- overhead text ----

        private readonly OverheadText _overheads = new OverheadText();

        // ---- tooltips ----

        private readonly UI.HoverTooltip _tooltip = new UI.HoverTooltip();

        private void StartOnline(LoginSettings login)
        {
            characterName = login.CharacterName;
            _ctx.Session = new GameSession(login);
            string cliloc = Path.Combine(_ctx.Data.Directory, "Cliloc.enu");
            if (File.Exists(cliloc))
                _ctx.Session.Cliloc = new Cliloc(cliloc);

            _ctx.Session.StatusChanged += s => _ctx.Hud.ConnectionStatus = s;
            _ctx.Session.Entered += OnEnteredWorld;
            SetUpUi();
            _ctx.Session.MoveRejected += (x, y, z, d) =>
            {
                _ctx.Player?.Teleport(x, y, z);
                _ctx.Player?.Face(d);
            };
            _ctx.Session.World.JournalAdded += j => _ctx.Hud.Log(j.Speaker, j.Text);
            _entities.Attach(_ctx.Session);
            CustomHouses.Attach(_ctx.Session, h => { if (_ctx.World != null) _entities.ShowMulti(h); }, s => _ctx.Hud.Log(null, s));

            _ctx.Hud.ConnectionStatus = $"Connecting to {login.Host}:{login.Port}â€¦";
            try
            {
                _ctx.Session.Start();
            }
            catch (Exception e)
            {
                _ctx.Session.Dispose();
                _ctx.Session = null;
                _ctx.Hud.ConnectionStatus = "";
                if (login.Interactive && _login != null)
                {
                    _login.Failed($"Could not reach {login.Host}:{login.Port} ({e.Message}).");
                    return;
                }
                Debug.LogWarning($"UOClient: could not reach the server ({e.Message}); running the offline demo.");
                StartOffline();
            }
        }

        private void OnEnteredWorld()
        {
            Core.Mobile p = _ctx.Session.World.Player;
            CreateWorld(_ctx.Session.World.Map, p.X, p.Y);
            _entities.SpawnPlayer(p, p.Name ?? characterName);
            if (!string.IsNullOrEmpty(p.Name))
                characterName = p.Name;

            _entities.SyncAll();

            _ctx.Hud.HasStats = true;
            _ctx.Hud.Log(null, $"Connected as {characterName}. Enter to talk, F3 for frame stats.");
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

            // Dropped before reaching the world (bad password, server down, cancelled): back to the login form.
            if (_login != null && _ctx.World == null && _ctx.Session != null && _ctx.Session.State == SessionState.Disconnected)
            {
                _login.Failed(_ctx.Session.Status);
                _ctx.Session.Dispose();
                _ctx.Session = null;
            }
            // Logged out: the server saved us and closed the connection.
            if (_quitAfterLogout && _ctx.Session != null && _ctx.Session.State == SessionState.Disconnected)
                Quit();

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
            _cursor.Update(_ctx.Session, _ui);
            _sections.Begin(null);
        }

        private readonly FrameSections _sections = new FrameSections();

        private void HandleInput()
        {
            // Typing in the chat line or a gump's text box: keys are text, not commands.
            if (_chatLine.Typing || (_ui != null && _ui.HasKeyboardFocus))
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
                return;
            }
            if (!_ready)
            {
                string what = _ctx.World == null
                    ? (_ctx.Session != null ? _ctx.Hud.ConnectionStatus : "Loading Britanniaâ€¦")
                    : $"Loading the areaâ€¦ ({_ctx.World.LoadedChunks} chunks)";
                GUI.Label(new Rect(20, 20, 600, 30), what);
                return;
            }

            Vector3 head = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(_ctx.Player.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
            _effects.DrawLightningGUI(_gumpArt, _ctx.Camera, _ctx.World, _ctx.ArtScale);
            _ctx.Hud.Draw(characterName, new Vector2(head.x, Screen.height - head.y));
            if (_ui != null)
            {
                _ui.Scale = Mathf.Clamp(Screen.height / 1000f, 0.75f, 2.5f);
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

        /// <summary>
        /// Unattended check: waits for the world, takes a screenshot, then runs a long route across
        /// several chunk boundaries while recording frame times, and writes a report.
        /// </summary>
        private IEnumerator CaptureTour(bool quitWhenDone)
        {
            string dir = Path.Combine(ProjectRoot, "Library", "UOClient");
            Directory.CreateDirectory(dir);
            float waitUntil = Time.realtimeSinceStartup + 30f;
            while (!_ready && Time.realtimeSinceStartup < waitUntil)
                yield return null;
            if (!_ready)
            {
                File.WriteAllText(Path.Combine(dir, "demo_capture_done.txt"), "not ready: " + (_loadError ?? _ctx.Hud.ConnectionStatus));
                if (quitWhenDone)
                    Application.Quit();
                yield break;
            }
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "stream_1_start.png"));
            yield return WaitFrames(10);
            string startCut = $"start {_ctx.Player.X},{_ctx.Player.Y},{_ctx.Player.Z}: upper floors cut at {(CutZ.HasValue ? CutZ.Value.ToString() : "none (outdoors)")}";

            // Run a long route: keep heading one way, turning when blocked, so the player crosses chunk edges.
            int rejected = 0;
            Action<int, int, sbyte, byte> onReject = (x, y, z, d) => rejected++;
            if (_ctx.Session != null)
                _ctx.Session.MoveRejected += onReject;
            int startX = _ctx.Player.X, startY = _ctx.Player.Y;
            int[] preference = { 2, 3, 1, 4, 0, 5, 7, 6 }; // east first, then around
            int heading = 0, steps = 0, stuckFor = 0;
            _frames.Reset();
            _sections.Hitches.Clear();
            float until = Time.realtimeSinceStartup + 25f;
            while (steps < 60 && Time.realtimeSinceStartup < until)
            {
                int dir2 = preference[heading % preference.Length];
                bool moved = _movement.TryStep(dir2, run: true);
                if (moved && _ctx.Player.IsMoving)
                {
                    steps++;
                    stuckFor = 0;
                }
                else if (!_ctx.Player.IsMoving && ++stuckFor > 20)
                {
                    heading++;
                    stuckFor = 0;
                }
                yield return null;
            }
            string frameReport = _frames.Summary();
            string hitchReport = _sections.Hitches.Count == 0 ? "none" : "\n  " + string.Join("\n  ", _sections.Hitches);
            yield return WaitFrames(30);
            if (_ctx.Session != null)
                _ctx.Session.MoveRejected -= onReject;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "stream_2_after_run.png"));
            yield return WaitFrames(10);

            // Windows: paperdoll, backpack and the server's help gump (uses are paced by the session).
            string uiReport = "offline: no windows";
            if (_ctx.Session != null && _ui != null)
            {
                _ctx.Session.OpenPaperdoll();
                _ctx.Session.OpenBackpack();
                _ctx.Session.RequestHelp();
                float uiUntil = Time.realtimeSinceStartup + 8f;
                bool HasAll() => _ui.Windows.Any(w => w is UI.PaperdollWindow) && _ui.Windows.Any(w => w is UI.ContainerWindow)
                                 && _ui.Windows.Any(w => w is UI.ServerGumpWindow);
                while (!HasAll() && Time.realtimeSinceStartup < uiUntil)
                    yield return null;
                yield return WaitFrames(30);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_1_windows.png"));
                yield return WaitFrames(10);
                uiReport = $"{_ui.Windows.Count} windows: " + string.Join(", ", _ui.Windows.Select(w => $"{w.Key} at {w.Rect.x:F0},{w.Rect.y:F0} {w.Rect.width:F0}x{w.Rect.height:F0}")) +
                           $"; backpack 0x{_ctx.Session.World.Worn(_ctx.Session.World.Player, 21):X8}, {_ctx.Session.World.OpenContainers.Count} containers open, " +
                           $"{_ctx.Session.World.Contained.Count} contained items; use delay {_ctx.Session.UseDelay}s; last journal: " +
                           string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Select(j => j.Text));

                // Second set: the paperdoll's buttons, war mode, and a skill that asks for a target.
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (!(w is UI.PaperdollWindow) && w.CanClose)
                        _ui.Close(w);
                OnUiCommand("skills");
                OnUiCommand("status");
                OnUiCommand("options");
                _ctx.Session.SetWarMode(true);
                _ctx.Session.UseSkill(1); // Anatomy
                float targetUntil = Time.realtimeSinceStartup + 4f;
                while (_ctx.Session.Target == null && Time.realtimeSinceStartup < targetUntil)
                    yield return null;
                bool gotCursor = _ctx.Session.Target != null;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_2_buttons.png"));
                yield return WaitFrames(10);
                if (gotCursor)
                    _ctx.Session.TargetObject(_ctx.Session.World.PlayerSerial);
                float warUntil = Time.realtimeSinceStartup + 2f;
                while (!_ctx.Session.WarMode && Time.realtimeSinceStartup < warUntil)
                    yield return null;
                bool warOn = _ctx.Session.WarMode;
                _ctx.Session.Say("Testing overhead speech");
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_3_overheads.png"));
                yield return WaitFrames(10);
                _ctx.Session.SetWarMode(false);

                // Third set: a vendor's context menu, then its buy list (nothing is bought).
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (w.CanClose) _ui.Close(w);
                string shopReport = "no vendor in range";
                var candidates = _ctx.Session.World.Mobiles.Values
                    .Where(v => (v.Notoriety == 1 || v.Notoriety == 7) && v.IsHuman && v.Serial != _ctx.Session.World.PlayerSerial
                                && !string.IsNullOrEmpty(v.Name) && Math.Max(Math.Abs(v.X - _ctx.Player.X), Math.Abs(v.Y - _ctx.Player.Y)) <= 12)
                    .OrderBy(v => Math.Max(Math.Abs(v.X - _ctx.Player.X), Math.Abs(v.Y - _ctx.Player.Y)))
                    .Take(8).ToList();
                Core.ContextMenu menu = null;
                Core.ShopList shop = null;
                Action<Core.ContextMenu> onMenu = cm => menu = cm;
                Action<Core.ShopList> onShop = s => shop = s;
                _ctx.Session.ContextMenuOpened += onMenu;
                _ctx.Session.ShopOpened += onShop;
                Core.Mobile vendor = null;
                var tried = new List<string>();
                foreach (Core.Mobile c in candidates)
                {
                    menu = null;
                    _ctx.Session.RequestContextMenu(c.Serial);
                    float menuUntil = Time.realtimeSinceStartup + 3f;
                    while (menu == null && Time.realtimeSinceStartup < menuUntil)
                        yield return null;
                    tried.Add($"{c.Name}: [{(menu == null ? "no menu" : string.Join(", ", menu.Entries.Select(e => e.Text)))}]");
                    vendor = c;
                    if (menu != null && menu.Entries.Any(e => e.Cliloc == 3006103))
                        break;
                    if (_ui.Find("context") is UI.UiWindow stale)
                        _ui.Close(stale);
                }
                if (vendor != null)
                {
                    yield return WaitFrames(10);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_4_context.png"));
                    yield return WaitFrames(10);
                    shopReport = string.Join("; ", tried) + $" -> {vendor.Name} ({Math.Max(Math.Abs(vendor.X - _ctx.Player.X), Math.Abs(vendor.Y - _ctx.Player.Y))} tiles): ";
                    if (menu != null)
                    {
                        shopReport += "menu [" + string.Join(", ", menu.Entries.Select(e => e.Text + (e.Disabled ? " (off)" : ""))) + "]";
                        Core.ContextMenuEntry buy = menu.Entries.FirstOrDefault(e => e.Cliloc == 3006103);
                        if (buy.Text != null && !buy.Disabled)
                        {
                            if (_ui.Find("context") is UI.UiWindow cw)
                                _ui.Close(cw);
                            _ctx.Session.ChooseContextMenu(vendor.Serial, buy.Index);
                            float shopUntil = Time.realtimeSinceStartup + 4f;
                            while (shop == null && Time.realtimeSinceStartup < shopUntil)
                                yield return null;
                            yield return WaitFrames(30);
                            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_5_shop.png"));
                            yield return WaitFrames(10);
                            shopReport += shop == null ? "; buy list never came"
                                : $"; buy list of {shop.Items.Count}: " + string.Join(", ", shop.Items.Take(6).Select(it => $"{it.Name} {it.Price}gp x{it.Amount}"));

                            // Buy the cheapest thing (the test character's own gold), check the pack and the gold, then sell it back.
                            ShopItem cheap = shop?.Items.Where(it => it.Price > 0 && it.Price <= 25).OrderBy(it => it.Price).FirstOrDefault();
                            uint pack = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                            if (cheap != null && pack != 0)
                            {
                                // Everything in the pack, counted by amount: a vendor's display graphic can differ from the item it sells.
                                int CountInPack() => _ctx.Session.World.ItemsIn(pack).Where(i => i.Graphic != 0x0EED).Sum(i => Math.Max(1, i.Amount)); // not the gold
                                int goldBefore = _ctx.Session.World.Player.Gold, countBefore = CountInPack();
                                bool closed = false;
                                Action<uint> onClosed = _ => closed = true;
                                _ctx.Session.ShopClosed += onClosed;
                                _ctx.Session.Buy(vendor.Serial, new[] { (cheap.Serial, 1) });
                                foreach (UI.UiWindow w in _ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                    _ui.Close(w);
                                float buyUntil = Time.realtimeSinceStartup + 5f;
                                while ((CountInPack() == countBefore || _ctx.Session.World.Player.Gold == goldBefore) && Time.realtimeSinceStartup < buyUntil)
                                    yield return null;
                                int countAfter = CountInPack(), goldAfter = _ctx.Session.World.Player.Gold;
                                // What came back after the order (ids of the trade, container and status packets).
                                string replies = string.Join(" ", _ctx.Session.RecentPackets.Reverse().Take(60).Reverse()
                                    .Where(pk => pk[0] == 0x3B || pk[0] == 0x25 || pk[0] == 0x3C || pk[0] == 0x11 || pk[0] == 0xF3 || pk[0] == 0x1A)
                                    .Select(pk => pk[0] == 0x25 ? $"25(0x{(pk[1] << 24 | pk[2] << 16 | pk[3] << 8 | pk[4]):X8} g{pk[5] << 8 | pk[6]:X4} in 0x{(pk[15] << 24 | pk[16] << 16 | pk[17] << 8 | pk[18]):X8})" : $"{pk[0]:X2}"));
                                shopReport += $"\n  packets after the order: {replies}";
                                shopReport += $"\n  bought 1 {cheap.Name} for {cheap.Price}gp: pack {countBefore} -> {countAfter}, gold {goldBefore} -> {goldAfter}, " +
                                              $"vendor closed the trade: {closed}";
                                _ctx.Session.ShopClosed -= onClosed;

                                // Sell it back: the vendor's Sell entry opens our sell list.
                                shop = null;
                                menu = null;
                                _ctx.Session.RequestContextMenu(vendor.Serial);
                                float m2 = Time.realtimeSinceStartup + 4f;
                                while (menu == null && Time.realtimeSinceStartup < m2)
                                    yield return null;
                                Core.ContextMenuEntry sell = menu?.Entries.FirstOrDefault(e => e.Cliloc == 3006104) ?? default;
                                if (_ui.Find("context") is UI.UiWindow cw2)
                                    _ui.Close(cw2);
                                if (sell.Text != null && !sell.Disabled)
                                {
                                    _ctx.Session.ChooseContextMenu(vendor.Serial, sell.Index);
                                    float s2 = Time.realtimeSinceStartup + 4f;
                                    while ((shop == null || !shop.Selling) && Time.realtimeSinceStartup < s2)
                                        yield return null;
                                    yield return WaitFrames(20);
                                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_5b_sell.png"));
                                    yield return WaitFrames(10);
                                    ShopItem mine = shop?.Items.FirstOrDefault(it => string.Equals(it.Name, cheap.Name, StringComparison.OrdinalIgnoreCase)) ?? shop?.Items.FirstOrDefault(it => it.Graphic == cheap.Graphic);
                                    if (mine != null)
                                    {
                                        int g2 = _ctx.Session.World.Player.Gold, c2 = CountInPack();
                                        _ctx.Session.Sell(vendor.Serial, new[] { (mine.Serial, 1) });
                                        foreach (UI.UiWindow w in _ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                            _ui.Close(w);
                                        float sellUntil = Time.realtimeSinceStartup + 5f;
                                        while ((CountInPack() == c2 || _ctx.Session.World.Player.Gold == g2) && Time.realtimeSinceStartup < sellUntil)
                                            yield return null;
                                        shopReport += $"\n  sold it back for {mine.Price}gp: pack {c2} -> {CountInPack()}, gold {g2} -> {_ctx.Session.World.Player.Gold}";
                                    }
                                    else
                                    {
                                        shopReport += $"\n  sell list ({shop?.Items.Count ?? 0} items) has no {cheap.Name}" + (shop == null ? " (no sell list came)" : "");
                                    }
                                }
                                else
                                {
                                    shopReport += "\n  no Sell entry on this vendor";
                                }
                            }
                        }
                    }
                    else
                    {
                        shopReport += "no context menu";
                    }
                }
                _ctx.Session.ContextMenuOpened -= onMenu;
                _ctx.Session.ShopOpened -= onShop;
                uiReport += "\nshop: " + shopReport;

                // Fourth set: effects (injected locally: a fireball across the street, a sparkle on us, lightning) and a tooltip.
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (w.CanClose) _ui.Close(w);
                uint me = _ctx.Session.World.PlayerSerial;
                byte[] Effect(byte type, uint src, uint dst, int graphic, int sx, int sy, int sz, int tx, int ty, int tz, int duration, bool explode) =>
                    new PacketWriter(0x70, 28).Byte(type).UInt32(src).UInt32(dst).UInt16(graphic)
                        .UInt16(sx).UInt16(sy).Byte((byte)(sbyte)sz).UInt16(tx).UInt16(ty).Byte((byte)(sbyte)tz)
                        .Byte(7).Byte(duration).UInt16(0).Byte(1).Byte(explode ? 1 : 0).ToArray();
                int ax = _ctx.Player.X, ay = _ctx.Player.Y, az = _ctx.Player.Z;

                // A small house set down next to us (local only; removed again after the picture).
                const uint fakeHouse = 0x4FFFFFF0;
                _ctx.Session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(2).UInt32(fakeHouse).UInt16(0x64).Byte(0)
                    .UInt16(1).UInt16(1).UInt16(ax + 7).UInt16(ay - 1).Byte((byte)(sbyte)az).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);
                yield return new WaitForSecondsRealtime(1.0f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_7_house.png"));
                yield return WaitFrames(10);
                int houseParts = _entities.MultiParts(fakeHouse);
                _ctx.Session.Inject(new PacketWriter(0x1D, 5).UInt32(fakeHouse).ToArray(), false);
                uiReport += $"\nhouse: {houseParts} parts drawn, {(_entities.HasMulti(fakeHouse) ? "not removed" : "removed")}";
                _ctx.Session.Inject(Effect(0, 0, 0, 0x36D4, ax - 6, ay - 4, az, ax + 4, ay + 3, az, 0, true), false);   // fireball
                _ctx.Session.Inject(Effect(3, me, me, 0x376A, ax, ay, az, ax, ay, az, 40, false), false);              // sparkles
                _ctx.Session.Inject(Effect(1, 0, 0, 0, ax + 2, ay - 2, az, ax + 2, ay - 2, az, 0, false), false);      // lightning
                yield return new WaitForSecondsRealtime(0.25f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_6_effects.png"));
                yield return WaitFrames(10);
                int effectsLive = _effects.Count;

                string tooltipReport = "no backpack items";
                uint packSerial = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                ContainedItem first = packSerial == 0 ? null : _ctx.Session.World.ItemsIn(packSerial).FirstOrDefault();
                if (first != null)
                {
                    float tipUntil = Time.realtimeSinceStartup + 4f;
                    PropertyList props = null;
                    while ((props = _ctx.Session.GetProperties(first.Serial)) == null && Time.realtimeSinceStartup < tipUntil)
                        yield return null;
                    tooltipReport = props == null ? $"no reply for 0x{first.Serial:X}" : $"0x{first.Serial:X}: " + string.Join(" / ", props.Lines);
                }
                // A footstep-like sound through the same path the server's 0x54 uses.
                _ctx.Session.Inject(new PacketWriter(0x54, 12).Byte(1).UInt16(0x42).UInt16(0).UInt16(_ctx.Player.X + 1).UInt16(_ctx.Player.Y).UInt16(_ctx.Player.Z).ToArray(), false);
                yield return WaitFrames(5);
                // Fifth set: a paperdoll item's tooltip (it must sit above the paperdoll), the chat window and the world map.
                string paperdollTip = "no paperdoll";
                _ctx.Session.OpenPaperdoll();
                float pdUntil = Time.realtimeSinceStartup + 4f;
                while (!_ui.Windows.Any(w => w is UI.PaperdollWindow) && Time.realtimeSinceStartup < pdUntil)
                    yield return null;
                if (_ui.Windows.FirstOrDefault(w => w is UI.PaperdollWindow) is UI.UiWindow pd && _ctx.Session.World.Player != null)
                {
                    EquippedItem worn = _ctx.Session.World.Player.Equipment.Values.FirstOrDefault(eq => eq.Layer != 11 && eq.Layer != 21 && eq.Layer != 16);
                    _tooltip.ForceHover(worn.Serial, pd.Rect.position + new Vector2(60, 120)); // over the paperdoll's body
                    float tipUntil2 = Time.realtimeSinceStartup + 4f;
                    while (_ctx.Session.GetProperties(worn.Serial) == null && Time.realtimeSinceStartup < tipUntil2)
                        yield return null;
                    yield return new WaitForSecondsRealtime(0.6f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_8_paperdoll_tooltip.png"));
                    yield return WaitFrames(10);
                    paperdollTip = $"0x{worn.Serial:X} layer {worn.Layer}: {string.Join(" / ", _ctx.Session.GetProperties(worn.Serial)?.Lines ?? new List<string>())}";
                    _tooltip.ClearForced();
                    _ui.Close(pd);
                }
                OnUiCommand("worldchat");
                OnUiCommand("worldmap");
                float mapUntil = Time.realtimeSinceStartup + 30f;
                yield return new WaitForSecondsRealtime(2f);
                while (Time.realtimeSinceStartup < mapUntil && !UI.WorldMapWindow.Ready(_ctx.Session.World.Map))
                    yield return null;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_9_chat_map.png"));
                yield return WaitFrames(10);
                // Sixth set: moving an item inside the backpack, then to the ground and back.
                string dragReport = "no item to move";
                uint myPack = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                ContainedItem moving = myPack == 0 ? null : _ctx.Session.World.ItemsIn(myPack).FirstOrDefault(i => i.Graphic != 0x0EED); // not the gold
                if (moving != null)
                {
                    uint serial = moving.Serial;
                    int ox = moving.X, oy = moving.Y, nx = ox < 100 ? ox + 30 : ox - 30;
                    ContainedItem Now() => _ctx.Session.World.ItemsIn(myPack).FirstOrDefault(i => i.Serial == serial);
                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                    _ctx.Session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    _ctx.Session.DropInto(serial, myPack, nx, oy);
                    float d1 = Time.realtimeSinceStartup + 3f;
                    while ((Now()?.X ?? ox) == ox && Time.realtimeSinceStartup < d1)
                        yield return null;
                    dragReport = $"0x{moving.Graphic:X4} moved in the pack {ox},{oy} -> {Now()?.X},{Now()?.Y}";

                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay

                    _ctx.Session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    // A free tile next to us (a wall or stall would refuse it).
                    var free = new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) }.Select(o => (X: _ctx.Player.X + o.Item1, Y: _ctx.Player.Y + o.Item2))
                        .FirstOrDefault(c => _ctx.Walk.TryStep(c.X, c.Y, _ctx.Player.Z) != null);
                    if (free.X == 0) free = (_ctx.Player.X + 1, _ctx.Player.Y);
                    _ctx.Session.DropOnGround(serial, free.X, free.Y, _ctx.Walk.SurfaceZ(free.X, free.Y, _ctx.Player.Z));
                    float d2 = Time.realtimeSinceStartup + 3f;
                    while (!_entities.HasItem(serial) && Time.realtimeSinceStartup < d2)
                        yield return null;
                    bool onGround = _entities.HasItem(serial) && _ctx.Session.World.Items.ContainsKey(serial);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                    _ctx.Session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    _ctx.Session.DropInto(serial, myPack, ox, oy);
                    float d3 = Time.realtimeSinceStartup + 3f;
                    while (Now() == null && Time.realtimeSinceStartup < d3)
                        yield return null;
                    dragReport += $"; dropped at our feet: {(onGround ? "drawn in the world" : "never appeared")}; back in the pack: {(Now() != null ? $"yes at {Now().X},{Now().Y}" : "NO")}";
                }

                // Seventh set: the shard's own gumps ([groups, and an offer from a chat link; wait a little for one).
                string gumpReport = "";
                float linkUntil = Time.realtimeSinceStartup + 20f;
                while (!_chatLog.Lines.Any(l => l.Entry.Link != null) && Time.realtimeSinceStartup < linkUntil)
                    yield return null;
                foreach (string command in new[] { "[groups", _chatLog.Lines.Select(l => l.Entry.Link).LastOrDefault(l => l != null) is string link ? "[" + link : null })
                {
                    if (command == null)
                        continue;
                    int before = _ui.Windows.Count(w => w is UI.ServerGumpWindow);
                    ServerGump got = null;
                    Action<ServerGump> onGump = g => got = g;
                    _ctx.Session.World.GumpOpened += onGump;
                    _ctx.Session.Say(command);
                    float gUntil = Time.realtimeSinceStartup + 8f;
                    while (got == null && Time.realtimeSinceStartup < gUntil)
                        yield return null;
                    _ctx.Session.World.GumpOpened -= onGump;
                    yield return WaitFrames(20);
                    string shot = command.StartsWith("[groups") ? "ui_10_groups.png" : "ui_11_link.png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, shot));
                    yield return WaitFrames(10);
                    var win = got == null ? null : _ui.Windows.OfType<UI.ServerGumpWindow>().FirstOrDefault(w => w.Gump == got);
                    string reply = string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Where(j => j.Speaker == null).Select(j => j.Text));
                    gumpReport += $"\n  {command}: " + (got == null ? $"no gump; server said: {reply}" :
                        $"gump 0x{got.TypeId:X} {got.Commands.Count} commands, {got.Lines.Length} lines, window {(win != null ? $"{win.Rect.width:F0}x{win.Rect.height:F0}" : "not opened")}; " +
                        $"kinds: {string.Join(" ", got.Commands.GroupBy(c => c.Name).Select(g => $"{g.Key}x{g.Count()}"))}");
                    if (win != null)
                        _ui.Close(win); // answers with button 0, as right-clicking would
                }
                uiReport += $"\nitems: {dragReport}\ngumps:{gumpReport}";

                // Eighth set: macros, through the same Run a key press uses.
                string macroReport = "";
                foreach (string action in new[] { "skill Hiding", "bandageself" })
                {
                    int journalBefore = _ctx.Session.World.Journal.Count;
                    yield return new WaitForSecondsRealtime(1.2f);
                    Hotkeys.Run(_ctx.Session, action);
                    float mUntil = Time.realtimeSinceStartup + 6f;
                    while (Time.realtimeSinceStartup < mUntil)
                    {
                        Hotkeys.Update(_ctx.Session); // answers the bandage's cursor when it comes
                        if (_ctx.Session.World.Journal.Skip(journalBefore).Any(j => j.Speaker == null || j.Serial == _ctx.Session.World.PlayerSerial))
                            break;
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                    macroReport += $"\n  {action}: " + string.Join(" | ", _ctx.Session.World.Journal.Skip(journalBefore)
                        .Where(j => j.Speaker == null || j.Serial == _ctx.Session.World.PlayerSerial).Select(j => j.Text).Take(3));
                }
                uiReport += $"\nmacros:{macroReport}";

                var byChannel = _chatLog.Lines.GroupBy(l => l.Channel).Select(g => $"{g.Key} {g.Count()}");
                uiReport += $"\npaperdoll tooltip: {paperdollTip}" +
                            $"\nchat: {_chatLog.Lines.Count} lines ({string.Join(", ", byChannel)}), {_chatLog.Lines.Count(l => l.Entry.Link != null)} with links" +
                            $"{(_chatLog.Lines.FirstOrDefault(l => l.Entry.Link != null) is var linked && linked.Entry.Link != null ? $", e.g. \"{linked.Entry.Text}\" -> [{linked.Entry.Link}" : "")}" +
                            $"\nworld map: {(UI.WorldMapWindow.Ready(_ctx.Session.World.Map) ? "drawn" : "not ready")}; top bar {(_ui.Find("topbar") != null ? "open" : "missing")}";

                uiReport += $"\neffects: {effectsLive} running after 0.25 s\ntooltip: {tooltipReport}" +
                            $"\naudio: {Sound.SoundsPlayed} sounds played, music {(Sound.MusicPlaying ?? "none")}";
                uiReport += $"\nbuttons: {_ui.Windows.Count} windows ({string.Join(", ", _ui.Windows.Select(w => w.Key))}); {_ctx.Session.Skills.Count} skills; " +
                            $"war mode {(warOn ? "on" : "never confirmed")}; anatomy cursor {(gotCursor ? "received and answered" : "never came")}; " +
                            $"{_overheads.Count} overheads; last journal: " +
                            string.Join(" | ", _ctx.Session.World.Journal.Skip(Math.Max(0, _ctx.Session.World.Journal.Count - 3)).Select(j => j.Text));
            }

            File.WriteAllText(Path.Combine(dir, "demo_capture_done.txt"),
                $"{(Application.isEditor ? "editor" : "player")} {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}\n" +
                $"{(_ctx.Session != null ? "online: " + _ctx.Session.Status : "offline")}\n" +
                $"{startCut}\n" +
                $"route: {steps} steps from {startX},{startY} to {_ctx.Player.X},{_ctx.Player.Y}, {rejected} rejected\n" +
                $"frames during the run: {frameReport}\n" +
                $"hitches: {hitchReport}\n" +
                $"chunks: {_ctx.World.LoadedChunks} loaded, {_ctx.World.PendingChunks} pending, {_ctx.World.ChunksBuilt} built, worker {_ctx.World.WorkerMs / Math.Max(1, _ctx.World.ChunksBuilt):F1} ms/chunk\n" +
                $"character/item art: {_ctx.MobileArt.Built} built off-thread, {_ctx.MobileArt.WorkerMs / Math.Max(1, _ctx.MobileArt.Built):F1} ms each, {_ctx.MobileArt.PendingRequests} pending\n" +
                $"{_entities.Others.Count} mobiles, {_entities.ItemCount} items drawn\n" +
                $"slow packets: {(_ctx.Session == null || _ctx.Session.SlowPackets.Count == 0 ? "none" : string.Join(", ", _ctx.Session.SlowPackets))}\n" +
                $"bodies: {_entities.BodyReport()}\n" +
                $"multis: {_entities.MultiCount} ({_entities.MultiPartCount} parts; ids {string.Join(", ", _ctx.Session?.World.Items.Values.Where(i => i.IsMulti).Select(i => $"0x{i.Graphic:X}").Distinct() ?? Array.Empty<string>())})\n" +
                $"ui: {uiReport}");

            if (quitWhenDone)
                Application.Quit();
        }

        public const string LoginCaptureRequestFile = "Library/UOClient/capture-login";

        /// <summary>Screenshots of the login form, character list and creation page (nothing is created), then back out.</summary>
        private IEnumerator LoginTour()
        {
            string dir = Path.Combine(ProjectRoot, "Library", "UOClient");
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_1_form.png"));
            yield return WaitFrames(10);
            _login.Screen.LoginPressed(_login.Screen);
            float until = Time.realtimeSinceStartup + 10f;
            while (_login.Screen.Current != UI.LoginScreen.Page.Characters && Time.realtimeSinceStartup < until)
                yield return null;
            yield return WaitFrames(10);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_2_characters.png"));
            yield return WaitFrames(10);
            string report = $"page after login: {_login.Screen.Current}; status: {_login.Screen.Status}";
            if (_login.Screen.Current == UI.LoginScreen.Page.Characters)
            {
                _login.Screen.Current = UI.LoginScreen.Page.Create;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_3_create.png"));
                yield return WaitFrames(10);
                report += $"; characters [{string.Join(", ", _ctx.Session.Characters)}]";
            }
            _ctx.Session?.Dispose();
            _ctx.Session = null;
            _login.Failed("");
            File.WriteAllText(Path.Combine(dir, "login_capture_done.txt"), report);
        }

        private static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

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
        public const string PathsFileName = ClientPaths.PathsFileName;

        /// <summary>See <see cref="ClientPaths.ProjectRoot"/>.</summary>
        private static string ProjectRoot => ClientPaths.ProjectRoot;

        private void OnDestroy()
        {
            _binder?.Dispose();
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
