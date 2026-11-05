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
    public sealed class WorldDemo : MonoBehaviour
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

        private static string SettingsPath => Path.Combine(ProjectRoot, "UserSettings", ClientSettings.FileName);

        /// <summary>The settings file as it is (no launch arguments), or null if there is none; for the editor windows too.</summary>
        public static ClientSettings ReadSettingsFile(bool orDefaults = true)
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonUtility.FromJson<ClientSettings>(File.ReadAllText(SettingsPath));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: {SettingsPath} is unreadable ({e.Message}); using defaults.");
            }
            return orDefaults ? new ClientSettings() : null;
        }

        /// <summary>The client overrides for a data set opened by an editor tool, when the settings name a client folder.</summary>
        public static void ApplyOverrides(UOData data, ClientSettings s)
        {
            string dir = s.ResolvedOverridesPath;
            if (dir != null)
                data.UseOverrides(new ClientOverrides(dir, s.hdArt));
        }

        /// <summary>Reads the settings file (writing one with the detected folders on first run), then applies launch arguments.</summary>
        private ClientSettings LoadSettings()
        {
            ClientSettings s = ReadSettingsFile(orDefaults: false);
            if (s == null)
            {
                s = new ClientSettings();
                s.uoDataPath = s.ResolvedDataPath;
                s.overridesPath = s.ResolvedOverridesPath ?? "none";
                SaveSettings(s);
            }
            return s.ApplyArguments(Environment.GetCommandLineArgs());
        }

        private void SaveSettings(ClientSettings s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonUtility.ToJson(s, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not write {SettingsPath} ({e.Message}).");
            }
        }

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

        private UOData _data;
        private Shader _shader;
        private Func<IsoCompositor> _compositorFactory;
        private WorldStreamer _world;
        private MobileArt _mobileArt;
        private Avatar _avatar;
        private Walkability _walk;
        private Camera _camera;
        private DemoHud _hud;
        private int _artScale, _bakedCount;
        private bool _roofsForced, _manualRoofs = true;
        private bool _ready;
        private string _loadError;
        private Task<DemoHud.RadarData> _radarTask;
        private readonly FrameStats _frames = new FrameStats();
        private bool _showStats;

        // Online
        private GameSession _session;
        private readonly Dictionary<uint, Avatar> _others = new Dictionary<uint, Avatar>();
        private readonly Dictionary<uint, int> _otherLook = new Dictionary<uint, int>();
        private readonly Dictionary<uint, ItemView> _items = new Dictionary<uint, ItemView>();
        private int _playerLook = -1;
        private bool _typing;
        private string _chat = "";

        // Time of day: index into Times; -1 follows the server's light level.
        private int _timeOverride = -1;
        private TimeOfDay _timeCurrent = Times[0];

        private static readonly TimeOfDay[] Times =
        {
            new TimeOfDay("Day", new Color(1.00f, 1.00f, 1.00f), Color.black, 0.10f),
            new TimeOfDay("Golden hour", new Color(1.08f, 0.94f, 0.78f), new Color(0.12f, 0.08f, 0.02f), 0.22f),
            new TimeOfDay("Dusk", new Color(0.62f, 0.56f, 0.76f), new Color(0.55f, 0.38f, 0.18f), 0.38f),
            new TimeOfDay("Night", new Color(0.30f, 0.34f, 0.54f), new Color(0.95f, 0.68f, 0.34f), 0.55f)
        };

        private void Start()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                _camera = camGo.AddComponent<Camera>();
            }
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
            // Depth keys span roughly +-5000 units around the depth origin (see DepthKey).
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 20000f;
            _hud = new DemoHud();
            ApplyLighting(Times[0]);

            try
            {
                Settings = LoadSettings();
                _data = new UOData(Settings.ResolvedDataPath);
                string overrides = Settings.ResolvedOverridesPath;
                if (overrides != null)
                {
                    var o = new ClientOverrides(overrides, Settings.hdArt);
                    _data.UseOverrides(o);
                    Debug.Log($"UOClient: client overrides from {overrides}: {o.ArtCount} art, {o.LandCount} land, {o.GumpCount} gumps, " +
                              $"{o.TileRowCount} tiledata rows, {o.EquipConvCount} equipment conversions{(o.Problems.Count > 0 ? $"; {o.Problems.Count} problems, first: {o.Problems[0]}" : "")}");
                }
                Debug.Log($"UOClient: UO data {Settings.ResolvedDataPath}; server {Settings.Server}");
                _shader = Shader.Find("UOClient/WorldSprite");
                if (_shader == null)
                    throw new InvalidOperationException("Shader UOClient/WorldSprite not found.");
                SetUpArt();

                // Unattended runs log straight in with the account file; people get the login screen.
                bool unattended = File.Exists(Path.Combine(ProjectRoot, CaptureRequestFile))
                                  || Environment.GetCommandLineArgs().Contains("-uoclient-capture")
                                  || Environment.GetCommandLineArgs().Contains("-uoclient-autologin");
                // -offline <map> <x> <y>: the offline demo at an exact spot (indoors included), e.g. to check a building.
                string[] cl = Environment.GetCommandLineArgs();
                int at = Array.IndexOf(cl, "-offline");
                if (at >= 0 && at + 3 < cl.Length && int.TryParse(cl[at + 1], out int om) && int.TryParse(cl[at + 2], out int ox) && int.TryParse(cl[at + 3], out int oy))
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
                    LoginSettings login = online ? ReadAccount() : null;
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
            bool fromCommandLine = Environment.GetCommandLineArgs().Contains("-uoclient-capture");
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
                _artScale = probe.Scale;
                _bakedCount = probe.Count;
                // The chunk worker gets its own compositor and baked-art reader (neither is thread-safe).
                _compositorFactory = () =>
                {
                    BakedRegion region = BakedRegion.Open(bakedDir);
                    return new IsoCompositor(_data) { Scale = region.Scale, PlacementOverride = region.Get };
                };
            }
            else
            {
                _artScale = scale;
                _compositorFactory = () => new IsoCompositor(_data) { Scale = scale };
            }
            _mobileArt = new MobileArt(_data, _artScale);
        }

        private void CreateWorld(int mapIndex, int x, int y)
        {
            _world?.Dispose();
            _world = new WorldStreamer(_data, transform, _shader, _artScale, _compositorFactory, mapIndex, x, y) { LoadRadius = loadRadius };
            _walk = new Walkability(_data, mapIndex);
            _hud.Summary = $"streaming {WorldStreamer.ChunkSize}x{WorldStreamer.ChunkSize} chunks, radius {loadRadius}, {_bakedCount} baked placements available, {_artScale}x art";
        }

        // ---- offline ----

        private void StartOffline()
        {
            CreateWorld(map, centerX, centerY);
            Vector2Int start = FindTile(centerX + 2, centerY + 9, underRoof: false);
            int z = _walk.TryStep(start.x, start.y, 127) ?? 0;

            int robe = HueApplier.FindClosest(_data.Hues, 40, 62, 150);
            int hair = HueApplier.FindClosest(_data.Hues, 96, 62, 34);
            MobileLook look = MobileLook.From(_data, 400, 0x03EA, new (byte, int, int)[] { (22, 0x1F03, robe), (11, 0x203B, hair) });
            _avatar = new Avatar(_mobileArt, transform, _shader, _artScale, look, start.x, start.y, z, characterName);

            _hud.Log("Lord British", "Welcome to Britannia!");
            _hud.Log(null, "Offline demo. WASD or hold right mouse to walk, Shift to run. T: time of day, R: roofs, H: HUD, F3: stats.");
        }

        private void StartOfflineAt(int mapIndex, int x, int y)
        {
            CreateWorld(mapIndex, x, y);
            int land = _walk.AverageLandZ(x, y);
            int z = _walk.TryStep(x, y, land) ?? land; // the ground floor: what is reachable from the land, not the floor above
            MobileLook look = MobileLook.From(_data, 400, 0x03EA, new (byte, int, int)[] { (22, 0x1F03, 0x0021), (11, 0x203B, 0x044E) });
            _avatar = new Avatar(_mobileArt, transform, _shader, _artScale, look, x, y, z, characterName);
            _hud.Log(null, $"Offline at {x},{y},{z} on map {mapIndex}.");
        }

        // ---- online ----

        private UI.LoginScreen _login;

        private string AccountPath => AccountPathFor(Settings.host);

        /// <summary>
        /// Remembered logins are kept per server: the local test server uses the test account file, any other
        /// shard its own file. So the local test account is never offered to (or sent to) the live shard.
        /// </summary>
        private string AccountPathFor(string host)
        {
            string local = Path.IsPathRooted(accountFile) ? accountFile : Path.Combine(ProjectRoot, accountFile);
            if (host == "127.0.0.1" || host == "localhost" || host == "::1")
                return local;
            string safe = new string(host.Select(ch => char.IsLetterOrDigit(ch) || ch == '.' || ch == '-' ? ch : '_').ToArray());
            return Path.Combine(Path.GetDirectoryName(local), $"uoclient-account-{safe}.json");
        }

        private void ShowLoginScreen()
        {
            _gumpArt ??= new UI.GumpArt(_data);
            _login = new UI.LoginScreen(_gumpArt, Path.Combine(ProjectRoot, "Tools", "ComfyRestyle", "style", "mockup_scene.png"));
            _login.Host = Settings.host;
            _login.Port = Settings.port;
            if (File.Exists(AccountPath))
            {
                AccountFile a = JsonUtility.FromJson<AccountFile>(File.ReadAllText(AccountPath));
                _login.Account = a.account;
                _login.Password = a.password;
                _login.RememberPassword = !string.IsNullOrEmpty(a.password);
            }
            _login.QuitPressed = Quit;
            _login.RememberedFor = host =>
            {
                string path = AccountPathFor(host);
                if (host.Length == 0 || !File.Exists(path))
                    return ("", "");
                AccountFile a = JsonUtility.FromJson<AccountFile>(File.ReadAllText(path));
                return (a.account ?? "", a.password ?? "");
            };
            _login.LoginPressed = screen =>
            {
                SaveAccount(screen);
                // A server typed on the login screen becomes the setting (unless -server chose it for this run).
                if ((screen.Host != Settings.host || screen.Port != Settings.port) && !ServerFromArguments)
                {
                    Settings.host = screen.Host;
                    Settings.port = screen.Port;
                    ClientSettings onDisk = File.Exists(SettingsPath) ? JsonUtility.FromJson<ClientSettings>(File.ReadAllText(SettingsPath)) : Settings;
                    onDisk.host = screen.Host;
                    onDisk.port = screen.Port;
                    SaveSettings(onDisk);
                }
                var login = new LoginSettings
                {
                    Host = screen.Host, Port = screen.Port, Account = screen.Account, Password = screen.Password,
                    Interactive = true,
                    Version = ReadAccountVersion()
                };
                StartOnline(login);
                if (_session != null)
                    _login.Attach(_session);
            };
        }

        /// <summary>Keeps what was typed for next time (the password only when asked; the file is git-ignored).</summary>
        private void SaveAccount(UI.LoginScreen screen)
        {
            try
            {
                string path = AccountPathFor(screen.Host); // the server being logged in to, which may have just been typed
                AccountFile a = File.Exists(path) ? JsonUtility.FromJson<AccountFile>(File.ReadAllText(path)) : new AccountFile();
                a.account = screen.Account;
                a.password = screen.RememberPassword ? screen.Password : "";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(a, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UOClient: could not save the account file ({e.Message}).");
            }
        }

        private ClientVersion ReadAccountVersion()
        {
            if (!File.Exists(AccountPath))
                return ClientVersion.Default;
            AccountFile a = JsonUtility.FromJson<AccountFile>(File.ReadAllText(AccountPath));
            return string.IsNullOrEmpty(a.clientVersion) ? ClientVersion.Default : ClientVersion.Parse(a.clientVersion);
        }

        private static bool ServerFromArguments =>
            Environment.GetCommandLineArgs().Any(a => a.TrimStart('-').Equals("server", StringComparison.OrdinalIgnoreCase));

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private LoginSettings ReadAccount()
        {
            // Unattended runs only log in with an account remembered for this server (never the test account elsewhere).
            string path = AccountPathFor(Settings.host);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"UOClient: no account file for {Settings.Server} at {path}; running the offline demo.");
                return null;
            }
            AccountFile a = JsonUtility.FromJson<AccountFile>(File.ReadAllText(path));
            return new LoginSettings
            {
                Host = Settings.host,
                Port = Settings.port,
                Account = a.account,
                Password = a.password,
                CharacterName = string.IsNullOrEmpty(a.character) ? "Adventurer" : a.character,
                StartCity = string.IsNullOrEmpty(a.startCity) ? "Britain" : a.startCity,
                Version = string.IsNullOrEmpty(a.clientVersion) ? ClientVersion.Default : ClientVersion.Parse(a.clientVersion)
            };
        }

        // ---- gumps ----

        private UI.GumpArt _gumpArt;
        private UI.UiManager _ui;
        private float _lastWorldClick;
        private Vector2 _lastWorldClickAt;

        /// <summary>Windows the server opens: paperdolls, containers and gumps.</summary>
        private void SetUpUi()
        {
            _gumpArt ??= new UI.GumpArt(_data);
            _ui = new UI.UiManager(_session, _gumpArt, _session.Cliloc)
            {
                // Released over the world: onto what is under the mouse, as in the classic client.
                DropOutside = (item, gui) => DropInWorld(item, new Vector2(gui.x, Screen.height - gui.y))
            };
            _session.World.PaperdollOpened += (serial, title) =>
            {
                UI.PaperdollWindow w = _ui.Open(new UI.PaperdollWindow(serial, title));
                w.SetTitle(title);
            };
            _session.World.ContainerOpened += c => _ui.Open(new UI.ContainerWindow(c.Serial, c.Gump));
            _session.World.GumpOpened += g => _ui.Open(new UI.ServerGumpWindow(g));
            _session.LiftRejected += reason =>
            {
                _ui.CancelDrag();
                _hud.Log(null, reason switch
                {
                    1 => "That is too far away.",
                    2 => "That is out of sight.",
                    3 => "That item does not belong to you.",
                    4 => "You are already holding an item.",
                    _ => "You cannot pick that up."
                });
            };

            _ui.Command = OnUiCommand;
            _chatLog = new UI.ChatLog();
            _session.World.JournalAdded += _chatLog.Add;
            _ui.Open(new UI.TopBarWindow());
            _hud.TopInset = UI.TopBarWindow.Height + 4;
            Hotkeys = new Hotkeys(ProjectRoot) { Log = s => _hud.Log(null, s), Command = OnUiCommand };
            _session.TargetRequested += _ => UpdateCursor();
            _session.TargetEnded += UpdateCursor;
            _session.WarModeChanged += war => _hud.WarMode = war;
            _session.Damaged += (serial, amount) => AddOverhead(serial, amount.ToString(), new Color(1f, 0.25f, 0.2f), 18, 1.6f, true);
            _session.World.JournalAdded += j =>
            {
                // Speech and labels appear over whoever said them (system messages only in the journal).
                // Said, emoted, whispered, yelled, cast or a click label: overhead. Guild, alliance, broadcast: journal only.
                bool local = j.Type == 0 || j.Type == 2 || (j.Type >= 6 && j.Type <= 10);
                if (local && j.Serial != 0xFFFFFFFF && j.Serial != 0)
                    AddOverhead(j.Serial, j.Text, _gumpArt.HueColor(j.Hue, new Color(0.95f, 0.95f, 0.9f)), 14, 3f + j.Text.Length * 0.05f, false);
            };
            _session.PlayerDied += () => _hud.Log(null, "You are dead.");

            _session.ContextMenuOpened += menu =>
            {
                Vector2 m = Input.mousePosition;
                _ui.Open(new UI.ContextMenuWindow(menu, new Vector2(m.x / _ui.Scale, (Screen.height - m.y) / _ui.Scale)));
            };
            _session.ShopOpened += list => _ui.Open(new UI.ShopWindow(list));
            _session.ShopClosed += vendor =>
            {
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (w is UI.ShopWindow s && s.Vendor == vendor)
                        _ui.Close(w);
            };
            _session.PromptRequested += _ => _ui.Open(new UI.PromptWindow());
            _session.SpellbookContent += (book, first, bits) => _ui.Open(new UI.SpellbookWindow(book, first, bits));
            _session.EffectStarted += StartEffect;

            if (_camera.GetComponent<AudioListener>() == null && FindAnyObjectByType<AudioListener>() == null)
                _camera.gameObject.AddComponent<AudioListener>();
            if (Sound == null)
            {
                Sound = gameObject.AddComponent<Audio>();
                Sound.Init(_data);
            }
            _session.SoundPlayed += (id, x, y, z) =>
            {
                if (_avatar != null)
                    Sound.PlaySound(id, x - _avatar.X, y - _avatar.Y);
            };
            _session.MusicRequested += id => Sound.PlayMusic(id);
        }

        /// <summary>Sound effects and music (null until online).</summary>
        public Audio Sound { get; private set; }

        // ---- effects ----

        private readonly List<EffectView> _effects = new List<EffectView>();

        private void StartEffect(GraphicEffect e)
        {
            if (_world == null || _effects.Count > 64)
                return; // a storm of effects should not cost more than the fight it decorates
            _effects.Add(new EffectView(_data, _mobileArt, transform, _shader, _artScale, e, LocateSerial));
        }

        /// <summary>Tile position of a mobile we draw (the player included), or of a ground item.</summary>
        private Vector3Int? LocateSerial(uint serial)
        {
            if (_session != null && serial == _session.World.PlayerSerial && _avatar != null)
                return new Vector3Int(_avatar.X, _avatar.Y, _avatar.Z);
            if (_others.TryGetValue(serial, out Avatar a))
                return new Vector3Int(a.X, a.Y, a.Z);
            if (_session != null && _session.World.Items.TryGetValue(serial, out WorldItem item))
                return new Vector3Int(item.X, item.Y, item.Z);
            return null;
        }

        private void UpdateEffects()
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                EffectView v = _effects[i];
                if (v.Done)
                {
                    GraphicEffect e = v.Effect;
                    if (e.Type == GraphicEffect.Moving && e.Explode)
                        StartEffect(new GraphicEffect
                        {
                            Type = GraphicEffect.AtLocation, Graphic = 0x36CB, Hue = e.Hue,
                            SourceX = (int)v.Position.x, SourceY = (int)v.Position.y, SourceZ = (int)v.Position.z
                        });
                    v.Dispose();
                    _effects.RemoveAt(i);
                    continue;
                }
                v.Render(_world);
            }
        }

        /// <summary>Lightning bolts are gump frames (0x4E20..) dropped onto the target.</summary>
        private void DrawLightning()
        {
            foreach (EffectView v in _effects)
            {
                if (!v.IsLightning)
                    continue;
                UI.UiImage img = _gumpArt.Gump(0x4E20 + v.LightningFrame, v.Effect.Hue);
                if (img == null)
                    continue;
                // Bottom-center on the target's feet, sized as world art (project the top too, to get the scale).
                Vector3 p = v.Position;
                var feetPx = new Vector2((p.x - p.y) * 22f, (p.x + p.y) * 22f + 22f - p.z * 4f) * _artScale;
                Vector2 feet = _world.ToUnity(feetPx), top = _world.ToUnity(feetPx - new Vector2(0, img.Height * _artScale));
                Vector3 s = _camera.WorldToScreenPoint(new Vector3(feet.x, feet.y, 0));
                Vector3 t = _camera.WorldToScreenPoint(new Vector3(top.x, top.y, 0));
                float h = Mathf.Abs(t.y - s.y), w = h * img.Width / Mathf.Max(1, img.Height);
                GUI.DrawTexture(new Rect(s.x - w / 2, Screen.height - s.y - h, w, h), img.Texture);
            }
        }

        private bool _quitAfterLogout;

        /// <summary>The height upper floors are cut at right now (null outdoors).</summary>
        public int? CutZ { get; private set; }
        private UI.ChatLog _chatLog;

        /// <summary>The paperdoll's buttons.</summary>
        private void OnUiCommand(string command)
        {
            switch (command)
            {
                case "help": _session.RequestHelp(); break;
                case "options": _ui.Open(new UI.OptionsWindow(this)); break;
                case "logout":
                    _ui.Open(new UI.ConfirmWindow("Log out?", "Quit the game and return to the desktop?", () =>
                    {
                        _quitAfterLogout = true;
                        _session.Logout();
                    }));
                    break;
                case "journal": _hud.JournalVisible = !_hud.JournalVisible; break;
                case "skills": _ui.Open(new UI.SkillsWindow()); break;
                case "chat": _typing = true; _chat = ""; break;
                case "war": _session.SetWarMode(!_session.WarMode); break;
                case "status": _ui.Open(new UI.StatusWindow()); break;
                // The top bar's own buttons.
                case "minimap": _hud.MinimapVisible = !_hud.MinimapVisible; break;
                case "paperdoll": _session.OpenPaperdoll(); break;
                case "backpack":
                    if (!_session.OpenBackpack())
                        _hud.Log(null, "You have no backpack.");
                    break;
                case "worldchat":
                    if (_ui.Find("worldchat") is UI.UiWindow chatWindow)
                        _ui.Close(chatWindow);
                    else
                        _ui.Open(new UI.WorldChatWindow(_chatLog, hue => _gumpArt.HueColor((int)hue, new Color(0.92f, 0.9f, 0.84f))));
                    break;
                case "worldmap":
                    if (_ui.Find("worldmap") is UI.UiWindow mapWindow)
                        _ui.Close(mapWindow);
                    else
                        _ui.Open(new UI.WorldMapWindow(_data, Path.Combine(ProjectRoot, "Library", "UOClient")));
                    break;
                case "groups": _session.Say("[groups"); break; // the server's Group Finder, as the UO Smallz client's button does
            }
        }

        /// <summary>Keyboard macros (null until online).</summary>
        public Hotkeys Hotkeys { get; private set; }

        // ---- options the options window changes ----

        public bool ShowNames { get => _hud.NamesVisible; set => _hud.NamesVisible = value; }
        public bool ShowHud { get => _hud.Visible; set => _hud.Visible = value; }
        public bool ShowJournal { get => _hud.JournalVisible; set => _hud.JournalVisible = value; }
        public bool AutoRoofs { get => !_roofsForced; set { _roofsForced = !value; _manualRoofs = true; } }
        public float Zoom { get => zoom; set => zoom = Mathf.Clamp(value, 0.25f, 4f); }

        // ---- cursor ----

        private Texture2D _crossNeutral, _crossHarmful, _crossBeneficial;

        /// <summary>A crosshair while the server wants a target: white neutral, red harmful, blue beneficial.</summary>
        private void UpdateCursor()
        {
            if (_ui != null && _ui.Dragging != null)
                return;
            TargetRequest t = _session?.Target;
            Texture2D tex = null;
            if (t != null)
            {
                _crossNeutral ??= Crosshair(new Color(0.95f, 0.95f, 0.95f));
                _crossHarmful ??= Crosshair(new Color(1f, 0.3f, 0.25f));
                _crossBeneficial ??= Crosshair(new Color(0.4f, 0.7f, 1f));
                tex = t.Flags == 1 ? _crossHarmful : t.Flags == 2 ? _crossBeneficial : _crossNeutral;
            }
            if (_cursorSet && tex == _cursorTex)
                return;
            _cursorSet = true;
            _cursorTex = tex;
            if (tex == null)
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            else
                Cursor.SetCursor(tex, new Vector2(16, 16), CursorMode.ForceSoftware);
        }

        private bool _cursorSet;
        private Texture2D _cursorTex;

        private static Texture2D Crosshair(Color color)
        {
            var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            var dark = new Color(0, 0, 0, 0.9f);
            for (int y = 0; y < 32; y++)
                for (int x = 0; x < 32; x++)
                {
                    int dx = x - 16, dy = y - 16;
                    bool ring = Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - 9) < 1.2f;
                    bool cross = (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) > 3 && Mathf.Abs(dy) < 15) || (Mathf.Abs(dy) <= 1 && Mathf.Abs(dx) > 3 && Mathf.Abs(dx) < 15);
                    bool edge = (Mathf.Abs(dx) == 2 && Mathf.Abs(dy) > 3 && Mathf.Abs(dy) < 15) || (Mathf.Abs(dy) == 2 && Mathf.Abs(dx) > 3 && Mathf.Abs(dx) < 15);
                    tex.SetPixel(x, y, ring || cross ? color : edge ? dark : clear);
                }
            tex.Apply();
            return tex;
        }

        // ---- overhead text ----

        private readonly List<Overhead> _overheads = new List<Overhead>();

        private sealed class Overhead
        {
            public uint Serial;
            public string Text;
            public Color Color;
            public int Size;
            public float Born, Life;
            public bool Rises; // damage numbers float upward
        }

        private void AddOverhead(uint serial, string text, Color color, int size, float life, bool rises)
        {
            // Classic clients keep a few lines per speaker; older ones scroll off.
            int count = 0;
            for (int i = _overheads.Count - 1; i >= 0; i--)
                if (_overheads[i].Serial == serial && !_overheads[i].Rises && ++count >= 3)
                    _overheads.RemoveAt(i);
            _overheads.Add(new Overhead { Serial = serial, Text = text, Color = color, Size = size, Born = Time.realtimeSinceStartup, Life = life, Rises = rises });
        }

        /// <summary>Where overhead text for a serial goes on screen (top-left origin), or null if it's not drawn.</summary>
        private Vector2? OverheadAnchor(uint serial)
        {
            Rect r;
            if (_session != null && serial == _session.World.PlayerSerial)
                r = _avatar.LastRect;
            else if (_others.TryGetValue(serial, out Avatar a) && a.Visible)
                r = a.LastRect;
            else if (_items.TryGetValue(serial, out ItemView v))
                r = v.LastRect;
            else
                return null;
            if (r.width <= 0)
                return null;
            Vector3 s = _camera.WorldToScreenPoint(new Vector3(r.center.x, r.yMax, 0));
            return new Vector2(s.x, Screen.height - s.y - 18);
        }

        private void UpdateOverheads()
        {
            _hud.Overheads.Clear();
            float now = Time.realtimeSinceStartup;
            _overheads.RemoveAll(o => now - o.Born > o.Life);
            // Newest first: the HUD stacks each speaker's lines upward from the head as it measures them.
            for (int i = _overheads.Count - 1; i >= 0; i--)
            {
                Overhead o = _overheads[i];
                Vector2? anchor = OverheadAnchor(o.Serial);
                if (anchor == null)
                    continue;
                Vector2 p = anchor.Value;
                if (o.Rises)
                    p.y -= (now - o.Born) * 40f;
                float fade = Mathf.Clamp01((o.Life - (now - o.Born)) / 0.5f);
                Color c = o.Color;
                c.a = fade;
                _hud.Overheads.Add((o.Text, p, c, o.Size, o.Rises ? 0u : o.Serial));
            }
        }

        // ---- tooltips ----

        private uint _hoverSerial, _forcedHover;
        private Vector2 _forcedHoverAt;
        private float _hoverSince;
        private GUIStyle _tooltipStyle;
        private Texture2D _tooltipBack;

        /// <summary>What the mouse rests on: an item in a window, else whatever is drawn under it in the world.</summary>
        private void UpdateHover()
        {
            uint serial = 0;
            Vector2 mouse = Input.mousePosition;
            if (_ui.IsOverUi(mouse))
            {
                var p = new Vector2(mouse.x / _ui.Scale, (Screen.height - mouse.y) / _ui.Scale);
                for (int i = _ui.Windows.Count - 1; i >= 0; i--)
                    if (_ui.Windows[i].Rect.Contains(p))
                    {
                        serial = _ui.Windows[i].Hovered;
                        break;
                    }
            }
            else if (_ui.Dragging == null)
            {
                serial = PickAt(mouse);
            }
            if (_forcedHover != 0)
                serial = _forcedHover; // the capture tour (no real mouse)
            if (serial != _hoverSerial)
            {
                _hoverSerial = serial;
                _hoverSince = Time.realtimeSinceStartup;
            }
        }

        private void DrawTooltip()
        {
            if (_session == null || _hoverSerial == 0 || Time.realtimeSinceStartup - _hoverSince < 0.4f || _ui.Dragging != null)
                return;
            PropertyList props = _session.GetProperties(_hoverSerial);
            if (props == null || props.Lines.Count == 0)
                return;
            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 14, alignment = TextAnchor.UpperCenter, padding = new RectOffset(8, 8, 6, 6) };
                _tooltipStyle.normal.textColor = new Color(0.93f, 0.91f, 0.84f);
                _tooltipBack = new Texture2D(1, 1);
                _tooltipBack.SetPixel(0, 0, new Color(0.05f, 0.04f, 0.03f, 0.88f));
                _tooltipBack.Apply();
            }
            var sb = new StringBuilder();
            for (int i = 0; i < props.Lines.Count; i++)
            {
                string line = UI.GumpHtml.ToRichText(props.Lines[i], out _);
                sb.Append(i == 0 ? $"<color=#ffd27a><b>{line}</b></color>" : line);
                if (i < props.Lines.Count - 1)
                    sb.Append('\n');
            }
            var content = new GUIContent(sb.ToString());
            float s = _ui.Scale;
            Matrix4x4 saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float width = Mathf.Min(320, _tooltipStyle.CalcSize(content).x + 4);
            float height = _tooltipStyle.CalcHeight(content, width);
            Vector2 m = _forcedHover != 0 ? _forcedHoverAt : Event.current.mousePosition / s;
            var r = new Rect(m.x + 18, m.y + 18, width, height);
            if (r.xMax > Screen.width / s) r.x = m.x - width - 8;
            if (r.yMax > Screen.height / s) r.y = m.y - height - 8;
            // Windows always draw above plain controls, so over a paperdoll or backpack the tooltip has to be
            // a window too, kept in front.
            GUI.Window(TooltipWindowId, r, _ =>
            {
                var local = new Rect(0, 0, r.width, r.height);
                GUI.DrawTexture(local, _tooltipBack);
                GUI.Label(local, content, _tooltipStyle);
            }, GUIContent.none, GUIStyle.none);
            GUI.BringWindowToFront(TooltipWindowId);
            GUI.matrix = saved;
        }

        private const int TooltipWindowId = 0x7EEF;

        // ---- input ----

        /// <summary>
        /// P paperdoll, B backpack, F1 help, Tab war mode, Escape cancels a target. Clicks in the world: single
        /// click names, double-click uses (attacks in war mode), and with a target cursor up, a click targets.
        /// </summary>
        private void HandleUiInput()
        {
            if (_ui == null || _session == null || _typing || _ui.HasKeyboardFocus)
                return;
            Hotkeys?.Update(_session);
            UpdateHover();
            if (Input.GetKeyDown(KeyCode.P))
                _session.OpenPaperdoll();
            if (Input.GetKeyDown(KeyCode.B) && !_session.OpenBackpack())
                _hud.Log(null, "You have no backpack.");
            if (Input.GetKeyDown(KeyCode.F1))
                _session.RequestHelp();
            if (Input.GetKeyDown(KeyCode.Tab))
                _session.SetWarMode(!_session.WarMode);
            if (Input.GetKeyDown(KeyCode.K))
                _ui.Open(new UI.SkillsWindow());
            if (Input.GetKeyDown(KeyCode.C))
                _ui.Open(new UI.StatusWindow());
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (_session.Target != null)
                    _session.CancelTarget();
                else if (_ui.Windows.LastOrDefault(w => w.CanClose) is UI.UiWindow top)
                    _ui.Close(top); // the topmost window that can close (not the top bar)
            }

            if (!Input.GetMouseButtonDown(0) || _ui.IsOverUi(Input.mousePosition) || _hud.JournalRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
                return;
            Vector2 mouse = Input.mousePosition;
            uint picked = PickAt(mouse);
            UI.UiWindow popup = _ui.Find("context");
            if (popup != null)
                _ui.Close(popup);

            // Shift+click: the context menu (vendors' Buy/Sell, pets' commands, ...).
            if (_session.Target == null && picked != 0 && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
            {
                _session.RequestContextMenu(picked);
                return;
            }

            // Target cursor: whatever is under the mouse, or the ground there.
            if (_session.Target != null)
            {
                if (picked != 0)
                {
                    _session.TargetObject(picked);
                }
                else if (_session.Target.AllowGround)
                {
                    Vector3 wp = _camera.ScreenToWorldPoint(new Vector3(mouse.x, mouse.y, 0));
                    Vector2Int tile = _world.TileAt(_world.FromUnity(new Vector2(wp.x, wp.y)), _avatar.Z);
                    _session.TargetGround(tile.x, tile.y, _walk.SurfaceZ(tile.x, tile.y, _avatar.Z));
                }
                else
                {
                    _hud.Log(null, "Target an object.");
                }
                return;
            }

            bool doubleClick = Time.realtimeSinceStartup - _lastWorldClick < 0.35f && (mouse - _lastWorldClickAt).sqrMagnitude < 64;
            _lastWorldClick = doubleClick ? 0 : Time.realtimeSinceStartup;
            _lastWorldClickAt = mouse;
            if (picked == 0)
                return;

            if (!doubleClick)
            {
                _session.Look(picked); // the server answers with the name over it
                return;
            }
            if (picked == _session.World.PlayerSerial)
                _session.OpenPaperdoll();
            else if (_session.WarMode && _session.World.Mobiles.ContainsKey(picked))
                _session.Attack(picked);
            else
                _session.Use(picked);
        }

        /// <summary>The nearest character or ground item under a screen point (by drawn rectangle), or 0.</summary>
        private uint PickAt(Vector2 screen)
        {
            Vector3 p = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0));
            uint best = 0;
            float bestDepth = float.MaxValue;
            void Consider(uint serial, Rect r, float depth)
            {
                if (r.width > 0 && r.Contains(new Vector2(p.x, p.y)) && depth < bestDepth)
                {
                    best = serial;
                    bestDepth = depth;
                }
            }
            if (_avatar.Visible)
                Consider(_session.World.PlayerSerial, _avatar.LastRect, _avatar.LastDepth);
            foreach (KeyValuePair<uint, Avatar> kv in _others)
                if (kv.Value.Visible)
                    Consider(kv.Key, kv.Value.LastRect, kv.Value.LastDepth);
            foreach (KeyValuePair<uint, ItemView> kv in _items)
                Consider(kv.Key, kv.Value.LastRect, kv.Value.LastDepth);
            return best;
        }

        [Serializable]
        private sealed class AccountFile
        {
            // The server is not here: it is a setting (unity-uo-settings.json / -server), so switching shards
            // never means editing credentials.
            public string account = "", password = "", character = "", startCity = "", clientVersion = "";
        }

        private void StartOnline(LoginSettings login)
        {
            characterName = login.CharacterName;
            _session = new GameSession(login);
            string cliloc = Path.Combine(_data.Directory, "Cliloc.enu");
            if (File.Exists(cliloc))
                _session.Cliloc = new Cliloc(cliloc);

            _session.StatusChanged += s => _hud.ConnectionStatus = s;
            _session.Entered += OnEnteredWorld;
            SetUpUi();
            _session.MoveRejected += (x, y, z, d) =>
            {
                _avatar?.Teleport(x, y, z);
                _avatar?.Face(d);
            };
            _session.World.JournalAdded += j => _hud.Log(j.Speaker, j.Text);
            _session.World.MobileUpdated += OnMobileUpdated;
            _session.World.MobileMoved += OnMobileMoved;
            _session.World.EntityRemoved += OnEntityRemoved;
            _session.World.ItemUpdated += OnItemUpdated;

            _hud.ConnectionStatus = $"Connecting to {login.Host}:{login.Port}â€¦";
            try
            {
                _session.Start();
            }
            catch (Exception e)
            {
                _session.Dispose();
                _session = null;
                _hud.ConnectionStatus = "";
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
            Core.Mobile p = _session.World.Player;
            CreateWorld(_session.World.Map, p.X, p.Y);
            _avatar?.Dispose();
            _avatar = new Avatar(_mobileArt, transform, _shader, _artScale, LookOf(p), p.X, p.Y, p.Z, p.Name ?? characterName);
            _avatar.Face(p.Direction);
            _playerLook = p.AppearanceVersion;
            if (!string.IsNullOrEmpty(p.Name))
                characterName = p.Name;

            foreach (Core.Mobile m in _session.World.Mobiles.Values)
                OnMobileUpdated(m);
            foreach (WorldItem i in _session.World.Items.Values)
                OnItemUpdated(i);

            _hud.HasStats = true;
            _hud.Log(null, $"Connected as {characterName}. Enter to talk, F3 for frame stats.");
        }

        private void OnMobileUpdated(Core.Mobile m)
        {
            if (_world == null)
                return;
            if (m.Serial == _session.World.PlayerSerial)
            {
                if (_avatar == null)
                    return;
                if (m.Name != null)
                    characterName = m.Name;
                if (m.AppearanceVersion != _playerLook)
                {
                    _playerLook = m.AppearanceVersion;
                    _avatar.SetLook(LookOf(m));
                }
                // A server-side position change (teleport, resync) overrides prediction.
                if (!_avatar.IsMoving && (m.X != _avatar.X || m.Y != _avatar.Y))
                    _avatar.Teleport(m.X, m.Y, m.Z);
                return;
            }

            if (!_others.TryGetValue(m.Serial, out Avatar a))
            {
                a = new Avatar(_mobileArt, transform, _shader, _artScale, LookOf(m), m.X, m.Y, m.Z, m.Name ?? $"Mobile 0x{m.Serial:X}");
                a.Face(m.Direction);
                _others[m.Serial] = a;
                _otherLook[m.Serial] = m.AppearanceVersion;
                return;
            }
            if (_otherLook[m.Serial] != m.AppearanceVersion)
            {
                _otherLook[m.Serial] = m.AppearanceVersion;
                a.SetLook(LookOf(m));
            }
            a.MoveTo(m.X, m.Y, m.Z, m.Direction, m.Running);
        }

        private void OnMobileMoved(Core.Mobile m)
        {
            if (_others.TryGetValue(m.Serial, out Avatar a))
            {
                if (_otherLook[m.Serial] != m.AppearanceVersion)
                {
                    _otherLook[m.Serial] = m.AppearanceVersion;
                    a.SetLook(LookOf(m));
                }
                a.MoveTo(m.X, m.Y, m.Z, m.Direction, m.Running);
            }
            else
            {
                OnMobileUpdated(m);
            }
        }

        private void OnEntityRemoved(uint serial)
        {
            if (_others.TryGetValue(serial, out Avatar a))
            {
                a.Dispose();
                _others.Remove(serial);
                _otherLook.Remove(serial);
            }
            if (_items.TryGetValue(serial, out ItemView v))
            {
                v.Dispose();
                _items.Remove(serial);
            }
            RemoveMulti(serial);
        }

        /// <summary>
        /// A dragged item released over the world at a screen point (bottom-left origin): onto a character there
        /// (giving it, or loading a pack animal), into an item there (a chest, a stack), else onto that tile.
        /// The server decides; what it refuses bounces back.
        /// </summary>
        private void DropInWorld(UI.DraggedItem item, Vector2 screen)
        {
            uint under = PickAt(screen);
            if (under != 0 && under != item.Serial)
            {
                _session.DropInto(item.Serial, under, 0xFFFF, 0xFFFF); // a mobile or item serial; the server sorts out which
                return;
            }
            Vector3 wp = _camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0));
            Vector2Int tile = _world.TileAt(_world.FromUnity(new Vector2(wp.x, wp.y)), _avatar.Z);
            _session.DropOnGround(item.Serial, tile.x, tile.y, _walk.SurfaceZ(tile.x, tile.y, _avatar.Z));
        }

        /// <summary>Art the classic clients never draw for world items (ClassicUO's no-draw list).</summary>
        private static bool IsNoDraw(ushort graphic) =>
            graphic == 0x0001 || graphic == 0x21BC || graphic == 0x9E4C || graphic == 0x9E64 || graphic == 0x9E65 || graphic == 0x9E7D;

        // Houses and boats: one view per fixed part, and the parts join the walkable statics.
        private readonly Dictionary<uint, List<ItemView>> _multis = new Dictionary<uint, List<ItemView>>();

        private void ShowMulti(WorldItem multi)
        {
            RemoveMulti(multi.Serial);
            var views = new List<ItemView>();
            foreach (MultiComponent part in _data.Multis.Get(multi.Graphic))
            {
                if (!part.Fixed || part.Graphic <= 1) // placeholders and the invisible anchor
                    continue;
                var piece = new WorldItem
                {
                    Graphic = part.Graphic, X = multi.X + part.X, Y = multi.Y + part.Y,
                    Z = (sbyte)Mathf.Clamp(multi.Z + part.Z, -128, 127), Hue = multi.Hue
                };
                views.Add(new ItemView(_data, _mobileArt, transform, _shader, _artScale, piece));
                _walk?.AddMultiPart(multi.Serial, piece.X, piece.Y, piece.Z, piece.Graphic);
            }
            _multis[multi.Serial] = views;
        }

        private void RemoveMulti(uint serial)
        {
            if (!_multis.TryGetValue(serial, out List<ItemView> views))
                return;
            foreach (ItemView v in views)
                v.Dispose();
            _multis.Remove(serial);
            _walk?.RemoveMulti(serial);
        }

        /// <summary>Inside a house, its upper floors and roof step aside like the map's roofs do.</summary>
        private void RenderMultis(bool hideAbove)
        {
            int cut = _avatar.Z + 16;
            foreach (List<ItemView> views in _multis.Values)
                foreach (ItemView v in views)
                {
                    v.Hidden = hideAbove && v.Z >= cut;
                    v.Render(_world);
                }
        }

        private void OnItemUpdated(WorldItem item)
        {
            if (_world == null)
                return;
            if (item.IsMulti)
            {
                if (_items.TryGetValue(item.Serial, out ItemView was))
                {
                    was.Dispose();
                    _items.Remove(item.Serial);
                }
                ShowMulti(item);
                return;
            }
            RemoveMulti(item.Serial);
            // Server-side markers nobody should see: the "no draw" tiles ModernUO uses for its EffectItems
            // (every spell and potion target spot gets one) and for invisible blockers.
            if (IsNoDraw(item.Graphic))
            {
                if (_items.TryGetValue(item.Serial, out ItemView hidden))
                {
                    hidden.Dispose();
                    _items.Remove(item.Serial);
                }
                return;
            }
            if (_items.TryGetValue(item.Serial, out ItemView v))
                v.Dispose();
            _items[item.Serial] = new ItemView(_data, _mobileArt, transform, _shader, _artScale, item);
        }

        private MobileLook LookOf(Core.Mobile m)
        {
            return MobileLook.From(_data, m.Body, m.Hue, m.Equipment.Values.Select(e => (e.Layer, (int)e.Graphic, (int)e.Hue)));
        }

        // ---- frame loop ----

        private void Update()
        {
            _frames.Add(Time.unscaledDeltaTime);
            _sections.EndFrame(Time.unscaledDeltaTime);
            if (Input.GetKeyDown(KeyCode.F3))
                _showStats = !_showStats;

            _sections.Begin("network");
            _session?.Update(Time.realtimeSinceStartupAsDouble);

            // Dropped before reaching the world (bad password, server down, cancelled): back to the login form.
            if (_login != null && _world == null && _session != null && _session.State == SessionState.Disconnected)
            {
                _login.Failed(_session.Status);
                _session.Dispose();
                _session = null;
            }
            // Logged out: the server saved us and closed the connection.
            if (_quitAfterLogout && _session != null && _session.State == SessionState.Disconnected)
                Quit();

            if (_world == null || _avatar == null)
                return;

            if (_session != null && _session.World.Map != _world.Map)
            {
                _world.SetMap(_session.World.Map);
                _walk = new Walkability(_data, _session.World.Map);
                _ready = false;
            }

            _sections.Begin("chunks");
            _world.Update(_avatar.X, _avatar.Y);
            _sections.Begin("minimap");
            UpdateMinimap();

            // Show the world once the chunks around the player are in; until then keep loading.
            if (!_ready)
            {
                if (!_world.IsReadyAround(_avatar.X, _avatar.Y))
                    return;
                _ready = true;
            }

            _sections.Begin("input");
            HandleInput();
            HandleUiInput();
            _avatar.Tick(Time.deltaTime);
            foreach (Avatar a in _others.Values)
                a.Tick(Time.deltaTime);

            bool under = _walk.IsUnderRoof(_avatar.X, _avatar.Y, _avatar.Z);
            _world.SetRoofsVisible(_roofsForced ? _manualRoofs : !under);
            // Upper floors: indoors, everything from the floor over our head up is cut away (unless R shows roofs).
            int? overhead = _roofsForced && _manualRoofs ? null : _walk.OverheadZ(_avatar.X, _avatar.Y, _avatar.Z);
            Shader.SetGlobalFloat("_UOCutZ", overhead.HasValue ? MeshBuilder.CutHeight(overhead.Value) : 0f);
            CutZ = overhead;

            _sections.Begin("art");
            _mobileArt.Flush();
            _sections.Begin("sprites");
            _avatar.Render(_world);
            foreach (Avatar a in _others.Values)
                a.Render(_world);
            foreach (ItemView v in _items.Values)
                v.Render(_world);
            RenderMultis(_roofsForced ? !_manualRoofs : under);
            UpdateEffects();

            _sections.Begin("camera+hud");
            UpdateCamera();
            UpdateLighting();
            UpdateHud();
            UpdateOverheads();
            UpdateCursor();
            _sections.Begin(null);
        }

        private readonly FrameSections _sections = new FrameSections();

        /// <summary>Recomputes the minimap grid on a background thread when the player nears its edge.</summary>
        private void UpdateMinimap()
        {
            if (_radarTask != null)
            {
                if (!_radarTask.IsCompleted)
                    return;
                if (_radarTask.Status == TaskStatus.RanToCompletion)
                    _hud.SetRadar(_radarTask.Result);
                _radarTask = null;
            }

            bool far = Math.Max(Math.Abs(_avatar.X - _hud.RadarCenterX), Math.Abs(_avatar.Y - _hud.RadarCenterY)) > 40;
            if (_hud.RadarCenterX == int.MinValue || far)
            {
                int m = _world.Map, x = _avatar.X, y = _avatar.Y;
                _radarTask = Task.Run(() => DemoHud.ComputeRadar(_data, m, x, y));
            }
        }

        private void HandleInput()
        {
            // Typing in the chat line or a gump's text box: keys are text, not commands.
            if (_typing || (_ui != null && _ui.HasKeyboardFocus))
                return;

            if (Input.GetKeyDown(KeyCode.T))
            {
                _timeOverride = _timeOverride + 1 >= Times.Length ? (_session != null ? -1 : 0) : _timeOverride + 1;
                _hud.Log(null, _timeOverride < 0 ? "Light follows the server's time of day." : $"Light set to {Times[_timeOverride].Name.ToLowerInvariant()}.");
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                // First press takes over from the automatic indoor check; each press flips.
                _roofsForced = true;
                _manualRoofs = !_world.RoofsVisible;
                _hud.Log(null, _manualRoofs ? "Roofs shown." : "Roofs hidden.");
            }
            if (Input.GetKeyDown(KeyCode.H))
                _hud.Visible = !_hud.Visible;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && (_ui == null || !_ui.IsOverUi(Input.mousePosition))) // windows (chat, world map) scroll themselves
                zoom =Mathf.Clamp(zoom * (wheel > 0 ? 1.12f : 1f / 1.12f), 0.25f, 4f);

            // Screen-space intent: keyboard, or right mouse toward the cursor (far cursor = run, like the classic client).
            Vector2 intent = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) intent.y -= 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) intent.y += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) intent.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) intent.x += 1;
            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            // Right mouse walks only over the world; over a window it closes the window instead.
            if (intent == Vector2.zero && Input.GetMouseButton(1) && (_ui == null || !_ui.IsOverUi(Input.mousePosition)))
            {
                Vector3 player = _camera.WorldToScreenPoint(_world.ToUnity(_avatar.AnchorPixels));
                intent = new Vector2(Input.mousePosition.x - player.x, -(Input.mousePosition.y - player.y));
                if (intent.magnitude < 12f)
                    return;
                run |= intent.magnitude > Screen.height * 0.25f;
            }
            if (intent == Vector2.zero)
                return;

            TryStep(DirectionFromScreen(intent), run);
        }

        /// <summary>
        /// One step (or a turn) in a direction, predicted locally and, online, sent to the server.
        /// Online, steps are paced on real time at the server's rate (ModernUO throttles anything faster),
        /// and each one starts the moment the previous one is due to end, so walking never pauses.
        /// </summary>
        private bool TryStep(int direction, bool run)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            bool canStep = _session == null ? !_avatar.IsMoving : now >= _nextStepAt - 0.004 && _session.PendingSteps < 3;
            if (!canStep)
                return false;

            if (_session != null && direction != _avatar.Direction)
            {
                // The server turns first and moves on the next request.
                if (_session.RequestStep(direction, run))
                    _avatar.Face(direction);
                return true;
            }

            Vector2Int d = Avatar.Delta(direction);
            int nx = _avatar.X + d.x, ny = _avatar.Y + d.y;
            int? z = _walk.TryStep(nx, ny, _avatar.Z);
            if (!z.HasValue)
            {
                _avatar.Face(direction);
                return false;
            }
            if (_session != null)
            {
                if (!_session.RequestStep(direction, run))
                    return false;
                _session.ApplyPredictedStep(nx, ny, (sbyte)z.Value, direction);
                // Never faster than one step per server delay, measured from when the last step was due.
                _nextStepAt = Math.Max(now, _nextStepAt) + _avatar.StepTime(run);
            }
            _avatar.BeginStep(direction, z.Value, run);
            return true;
        }

        private double _nextStepAt;

        /// <summary>Screen vector (x right, y down) to a UO direction: right = 1 (east-north), down = 3, left = 5, up = 7.</summary>
        public static int DirectionFromScreen(Vector2 v)
        {
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            int octant = Mathf.RoundToInt(angle / 45f);
            return ((octant + 1) % 8 + 8) % 8;
        }

        private void UpdateCamera()
        {
            _camera.orthographicSize = Screen.height / 2f / zoom;
            // Center on the body (about 45 UO pixels above the feet), then drop the view a little so the
            // character sits above the journal panel.
            Vector2 target = _world.ToUnity(_avatar.AnchorPixels) + new Vector2(0, 45 * _artScale - Screen.height * 0.12f / zoom);
            Vector3 pos = _camera.transform.position;
            Vector2 smoothed = Vector2.Lerp(new Vector2(pos.x, pos.y), target, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            if ((new Vector2(pos.x, pos.y) - target).sqrMagnitude > 4_000_000f || pos.z > -1)
                smoothed = target;
            _camera.transform.position = new Vector3(Mathf.Round(smoothed.x), Mathf.Round(smoothed.y), -10000f);
        }

        private void UpdateLighting()
        {
            TimeOfDay target;
            if (_timeOverride >= 0)
            {
                target = Times[_timeOverride];
            }
            else if (_session != null)
            {
                // Server light: 0 = full day, ~26 and above = deep night.
                float night = Mathf.Clamp01((_session.World.GlobalLight - 2) / 22f);
                target = night < 0.5f
                    ? TimeOfDay.Lerp(Times[0], Times[2], night * 2f)
                    : TimeOfDay.Lerp(Times[2], Times[3], night * 2f - 1f);
                target = new TimeOfDay(night < 0.15f ? "Day" : night < 0.6f ? "Dusk" : "Night", target.Ambient, target.Light, target.Vignette);
            }
            else
            {
                target = Times[0];
            }

            _timeCurrent = TimeOfDay.Lerp(_timeCurrent, target, 1f - Mathf.Exp(-Time.deltaTime * 2f));
            ApplyLighting(_timeCurrent);
            _hud.TimeName = target.Name + (_timeOverride < 0 && _session != null ? $" (server light {_session.World.GlobalLight})" : "");
            _hud.Vignette = _timeCurrent.Vignette;

            Vector2 light = _world.ToUnity(_avatar.AnchorPixels) + new Vector2(0, 30 * _artScale);
            Shader.SetGlobalVector("_UOLightPos", new Vector4(light.x, light.y, 170f * _artScale, 0));
        }

        private static void ApplyLighting(TimeOfDay t)
        {
            Shader.SetGlobalVector("_UOAmbient", t.Ambient);
            Shader.SetGlobalVector("_UOLightColor", t.Light);
        }

        private void UpdateHud()
        {
            _hud.UpdatePlayer(_avatar.X, _avatar.Y, _avatar.IsMoving, Time.deltaTime);
            _hud.Labels.Clear();
            if (_session == null)
                return;

            // Connection text only matters while not playing (connecting, or after a disconnect).
            _hud.ConnectionStatus = _session.State == SessionState.InWorld ? "" : _session.Status;

            Core.Mobile p = _session.World.Player;
            if (p != null && p.HitsMax > 0)
            {
                _hud.Hits = p.Hits;
                _hud.HitsMax = p.HitsMax;
                _hud.Mana = p.Mana;
                _hud.ManaMax = Math.Max(p.ManaMax, 1);
                _hud.Stamina = p.Stamina;
                _hud.StaminaMax = Math.Max(p.StaminaMax, 1);
            }

            foreach (KeyValuePair<uint, Avatar> kv in _others)
            {
                if (!_session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) || string.IsNullOrEmpty(m.Name))
                    continue;
                Vector3 s = _camera.WorldToScreenPoint(_world.ToUnity(kv.Value.AnchorPixels - new Vector2(0, 75 * _artScale)));
                if (s.x < -50 || s.x > Screen.width + 50 || s.y < -50 || s.y > Screen.height + 50)
                    continue;
                _hud.Labels.Add((m.Name, new Vector2(s.x, Screen.height - s.y), DemoHud.NotorietyColor(m.Notoriety)));
            }

            // Health bars under the feet: whoever we fight, anyone hurt, and ourselves in war mode or when hurt.
            _hud.HealthBars.Clear();
            void Bar(Core.Mobile m, Avatar a)
            {
                if (m == null || a == null || !a.Visible || m.HitsMax <= 0)
                    return;
                bool show = m.Serial == _session.Combatant || m.Hits < m.HitsMax
                            || (m.Serial == _session.World.PlayerSerial && _session.WarMode);
                if (!show)
                    return;
                Vector3 s = _camera.WorldToScreenPoint(_world.ToUnity(a.AnchorPixels + new Vector2(0, 10 * _artScale)));
                _hud.HealthBars.Add((new Vector2(s.x, Screen.height - s.y), m.Hits / (float)m.HitsMax, DemoHud.NotorietyColor(m.Notoriety)));
            }
            Bar(p, _avatar);
            foreach (KeyValuePair<uint, Avatar> kv in _others)
                if (_session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m))
                    Bar(m, kv.Value);
        }

        private void OnGUI()
        {
            if (_loadError != null)
            {
                GUI.Label(new Rect(20, 20, Screen.width - 40, 200), "World demo failed to load:\n" + _loadError);
                return;
            }
            if (_login != null && _world == null)
            {
                _login.Draw();
                return;
            }
            if (!_ready)
            {
                string what = _world == null
                    ? (_session != null ? _hud.ConnectionStatus : "Loading Britanniaâ€¦")
                    : $"Loading the areaâ€¦ ({_world.LoadedChunks} chunks)";
                GUI.Label(new Rect(20, 20, 600, 30), what);
                return;
            }

            Vector3 head = _camera.WorldToScreenPoint(_world.ToUnity(_avatar.AnchorPixels - new Vector2(0, 75 * _artScale)));
            DrawLightning();
            _hud.Draw(characterName, new Vector2(head.x, Screen.height - head.y));
            if (_ui != null)
            {
                _ui.Scale = Mathf.Clamp(Screen.height / 1000f, 0.75f, 2.5f);
                _ui.OnGUI();
                DrawTooltip();
            }
            DrawChat();

            if (_showStats)
            {
                string lastHitch = _sections.Hitches.Count > 0 ? "\nlast hitch: " + _sections.Hitches[_sections.Hitches.Count - 1] : "";
                GUI.Label(new Rect(Screen.width - 620, Screen.height - 90, 610, 80),
                    $"{_frames.Summary()}\nchunks {_world.LoadedChunks} loaded, {_world.PendingChunks} pending, worker {_world.WorkerMs / Math.Max(1, _world.ChunksBuilt):F1} ms/chunk; " +
                    $"art {_mobileArt.Built} built, {_mobileArt.PendingRequests} pending{lastHitch}");
            }
        }

        /// <summary>Enter opens a speech line; Enter again sends it, Escape cancels.</summary>
        private void DrawChat()
        {
            if (_session == null)
                return;
            Event e = Event.current;
            // A window's text box (the chat window, a prompt) has the keyboard: its Enter is its own.
            bool otherField = !_typing && _ui != null && _ui.HasKeyboardFocus;
            if (!otherField && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                if (_typing && _chat.Trim().Length > 0)
                    _session.Say(_chat.Trim());
                _typing = !_typing;
                _chat = "";
                e.Use();
            }
            else if (_typing && e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                _typing = false;
                _chat = "";
                e.Use();
            }

            if (!_typing)
                return;
            float s = Mathf.Clamp(Screen.height / 1000f, 0.75f, 2.5f);
            GUI.SetNextControlName("uo-chat");
            _chat = GUI.TextField(new Rect(Screen.width * 0.5f - 300 * s, Screen.height - 250 * s, 600 * s, 26 * s), _chat, 200);
            GUI.FocusControl("uo-chat");
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
                        int? z = _walk.TryStep(tx, ty, 127);
                        if (z.HasValue && _walk.IsUnderRoof(tx, ty, z.Value) == underRoof)
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
                File.WriteAllText(Path.Combine(dir, "demo_capture_done.txt"), "not ready: " + (_loadError ?? _hud.ConnectionStatus));
                if (quitWhenDone)
                    Application.Quit();
                yield break;
            }
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "stream_1_start.png"));
            yield return WaitFrames(10);
            string startCut = $"start {_avatar.X},{_avatar.Y},{_avatar.Z}: upper floors cut at {(CutZ.HasValue ? CutZ.Value.ToString() : "none (outdoors)")}";

            // Run a long route: keep heading one way, turning when blocked, so the player crosses chunk edges.
            int rejected = 0;
            Action<int, int, sbyte, byte> onReject = (x, y, z, d) => rejected++;
            if (_session != null)
                _session.MoveRejected += onReject;
            int startX = _avatar.X, startY = _avatar.Y;
            int[] preference = { 2, 3, 1, 4, 0, 5, 7, 6 }; // east first, then around
            int heading = 0, steps = 0, stuckFor = 0;
            _frames.Reset();
            _sections.Hitches.Clear();
            float until = Time.realtimeSinceStartup + 25f;
            while (steps < 60 && Time.realtimeSinceStartup < until)
            {
                int dir2 = preference[heading % preference.Length];
                bool moved = TryStep(dir2, run: true);
                if (moved && _avatar.IsMoving)
                {
                    steps++;
                    stuckFor = 0;
                }
                else if (!_avatar.IsMoving && ++stuckFor > 20)
                {
                    heading++;
                    stuckFor = 0;
                }
                yield return null;
            }
            string frameReport = _frames.Summary();
            string hitchReport = _sections.Hitches.Count == 0 ? "none" : "\n  " + string.Join("\n  ", _sections.Hitches);
            yield return WaitFrames(30);
            if (_session != null)
                _session.MoveRejected -= onReject;
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "stream_2_after_run.png"));
            yield return WaitFrames(10);

            // Windows: paperdoll, backpack and the server's help gump (uses are paced by the session).
            string uiReport = "offline: no windows";
            if (_session != null && _ui != null)
            {
                _session.OpenPaperdoll();
                _session.OpenBackpack();
                _session.RequestHelp();
                float uiUntil = Time.realtimeSinceStartup + 8f;
                bool HasAll() => _ui.Windows.Any(w => w is UI.PaperdollWindow) && _ui.Windows.Any(w => w is UI.ContainerWindow)
                                 && _ui.Windows.Any(w => w is UI.ServerGumpWindow);
                while (!HasAll() && Time.realtimeSinceStartup < uiUntil)
                    yield return null;
                yield return WaitFrames(30);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_1_windows.png"));
                yield return WaitFrames(10);
                uiReport = $"{_ui.Windows.Count} windows: " + string.Join(", ", _ui.Windows.Select(w => $"{w.Key} at {w.Rect.x:F0},{w.Rect.y:F0} {w.Rect.width:F0}x{w.Rect.height:F0}")) +
                           $"; backpack 0x{_session.World.Worn(_session.World.Player, 21):X8}, {_session.World.OpenContainers.Count} containers open, " +
                           $"{_session.World.Contained.Count} contained items; use delay {_session.UseDelay}s; last journal: " +
                           string.Join(" | ", _session.World.Journal.Skip(Math.Max(0, _session.World.Journal.Count - 3)).Select(j => j.Text));

                // Second set: the paperdoll's buttons, war mode, and a skill that asks for a target.
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (!(w is UI.PaperdollWindow) && w.CanClose)
                        _ui.Close(w);
                OnUiCommand("skills");
                OnUiCommand("status");
                OnUiCommand("options");
                _session.SetWarMode(true);
                _session.UseSkill(1); // Anatomy
                float targetUntil = Time.realtimeSinceStartup + 4f;
                while (_session.Target == null && Time.realtimeSinceStartup < targetUntil)
                    yield return null;
                bool gotCursor = _session.Target != null;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_2_buttons.png"));
                yield return WaitFrames(10);
                if (gotCursor)
                    _session.TargetObject(_session.World.PlayerSerial);
                float warUntil = Time.realtimeSinceStartup + 2f;
                while (!_session.WarMode && Time.realtimeSinceStartup < warUntil)
                    yield return null;
                bool warOn = _session.WarMode;
                _session.Say("Testing overhead speech");
                yield return new WaitForSecondsRealtime(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_3_overheads.png"));
                yield return WaitFrames(10);
                _session.SetWarMode(false);

                // Third set: a vendor's context menu, then its buy list (nothing is bought).
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (w.CanClose) _ui.Close(w);
                string shopReport = "no vendor in range";
                var candidates = _session.World.Mobiles.Values
                    .Where(v => (v.Notoriety == 1 || v.Notoriety == 7) && v.IsHuman && v.Serial != _session.World.PlayerSerial
                                && !string.IsNullOrEmpty(v.Name) && Math.Max(Math.Abs(v.X - _avatar.X), Math.Abs(v.Y - _avatar.Y)) <= 12)
                    .OrderBy(v => Math.Max(Math.Abs(v.X - _avatar.X), Math.Abs(v.Y - _avatar.Y)))
                    .Take(8).ToList();
                Core.ContextMenu menu = null;
                Core.ShopList shop = null;
                Action<Core.ContextMenu> onMenu = cm => menu = cm;
                Action<Core.ShopList> onShop = s => shop = s;
                _session.ContextMenuOpened += onMenu;
                _session.ShopOpened += onShop;
                Core.Mobile vendor = null;
                var tried = new List<string>();
                foreach (Core.Mobile c in candidates)
                {
                    menu = null;
                    _session.RequestContextMenu(c.Serial);
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
                    shopReport = string.Join("; ", tried) + $" -> {vendor.Name} ({Math.Max(Math.Abs(vendor.X - _avatar.X), Math.Abs(vendor.Y - _avatar.Y))} tiles): ";
                    if (menu != null)
                    {
                        shopReport += "menu [" + string.Join(", ", menu.Entries.Select(e => e.Text + (e.Disabled ? " (off)" : ""))) + "]";
                        Core.ContextMenuEntry buy = menu.Entries.FirstOrDefault(e => e.Cliloc == 3006103);
                        if (buy.Text != null && !buy.Disabled)
                        {
                            if (_ui.Find("context") is UI.UiWindow cw)
                                _ui.Close(cw);
                            _session.ChooseContextMenu(vendor.Serial, buy.Index);
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
                            uint pack = _session.World.Worn(_session.World.Player, 21);
                            if (cheap != null && pack != 0)
                            {
                                // Everything in the pack, counted by amount: a vendor's display graphic can differ from the item it sells.
                                int CountInPack() => _session.World.ItemsIn(pack).Where(i => i.Graphic != 0x0EED).Sum(i => Math.Max(1, i.Amount)); // not the gold
                                int goldBefore = _session.World.Player.Gold, countBefore = CountInPack();
                                bool closed = false;
                                Action<uint> onClosed = _ => closed = true;
                                _session.ShopClosed += onClosed;
                                _session.Buy(vendor.Serial, new[] { (cheap.Serial, 1) });
                                foreach (UI.UiWindow w in _ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                    _ui.Close(w);
                                float buyUntil = Time.realtimeSinceStartup + 5f;
                                while ((CountInPack() == countBefore || _session.World.Player.Gold == goldBefore) && Time.realtimeSinceStartup < buyUntil)
                                    yield return null;
                                int countAfter = CountInPack(), goldAfter = _session.World.Player.Gold;
                                // What came back after the order (ids of the trade, container and status packets).
                                string replies = string.Join(" ", _session.RecentPackets.Reverse().Take(60).Reverse()
                                    .Where(pk => pk[0] == 0x3B || pk[0] == 0x25 || pk[0] == 0x3C || pk[0] == 0x11 || pk[0] == 0xF3 || pk[0] == 0x1A)
                                    .Select(pk => pk[0] == 0x25 ? $"25(0x{(pk[1] << 24 | pk[2] << 16 | pk[3] << 8 | pk[4]):X8} g{pk[5] << 8 | pk[6]:X4} in 0x{(pk[15] << 24 | pk[16] << 16 | pk[17] << 8 | pk[18]):X8})" : $"{pk[0]:X2}"));
                                shopReport += $"\n  packets after the order: {replies}";
                                shopReport += $"\n  bought 1 {cheap.Name} for {cheap.Price}gp: pack {countBefore} -> {countAfter}, gold {goldBefore} -> {goldAfter}, " +
                                              $"vendor closed the trade: {closed}";
                                _session.ShopClosed -= onClosed;

                                // Sell it back: the vendor's Sell entry opens our sell list.
                                shop = null;
                                menu = null;
                                _session.RequestContextMenu(vendor.Serial);
                                float m2 = Time.realtimeSinceStartup + 4f;
                                while (menu == null && Time.realtimeSinceStartup < m2)
                                    yield return null;
                                Core.ContextMenuEntry sell = menu?.Entries.FirstOrDefault(e => e.Cliloc == 3006104) ?? default;
                                if (_ui.Find("context") is UI.UiWindow cw2)
                                    _ui.Close(cw2);
                                if (sell.Text != null && !sell.Disabled)
                                {
                                    _session.ChooseContextMenu(vendor.Serial, sell.Index);
                                    float s2 = Time.realtimeSinceStartup + 4f;
                                    while ((shop == null || !shop.Selling) && Time.realtimeSinceStartup < s2)
                                        yield return null;
                                    yield return WaitFrames(20);
                                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_5b_sell.png"));
                                    yield return WaitFrames(10);
                                    ShopItem mine = shop?.Items.FirstOrDefault(it => string.Equals(it.Name, cheap.Name, StringComparison.OrdinalIgnoreCase)) ?? shop?.Items.FirstOrDefault(it => it.Graphic == cheap.Graphic);
                                    if (mine != null)
                                    {
                                        int g2 = _session.World.Player.Gold, c2 = CountInPack();
                                        _session.Sell(vendor.Serial, new[] { (mine.Serial, 1) });
                                        foreach (UI.UiWindow w in _ui.Windows.Where(w => w is UI.ShopWindow).ToList())
                                            _ui.Close(w);
                                        float sellUntil = Time.realtimeSinceStartup + 5f;
                                        while ((CountInPack() == c2 || _session.World.Player.Gold == g2) && Time.realtimeSinceStartup < sellUntil)
                                            yield return null;
                                        shopReport += $"\n  sold it back for {mine.Price}gp: pack {c2} -> {CountInPack()}, gold {g2} -> {_session.World.Player.Gold}";
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
                _session.ContextMenuOpened -= onMenu;
                _session.ShopOpened -= onShop;
                uiReport += "\nshop: " + shopReport;

                // Fourth set: effects (injected locally: a fireball across the street, a sparkle on us, lightning) and a tooltip.
                foreach (UI.UiWindow w in _ui.Windows.ToList())
                    if (w.CanClose) _ui.Close(w);
                uint me = _session.World.PlayerSerial;
                byte[] Effect(byte type, uint src, uint dst, int graphic, int sx, int sy, int sz, int tx, int ty, int tz, int duration, bool explode) =>
                    new PacketWriter(0x70, 28).Byte(type).UInt32(src).UInt32(dst).UInt16(graphic)
                        .UInt16(sx).UInt16(sy).Byte((byte)(sbyte)sz).UInt16(tx).UInt16(ty).Byte((byte)(sbyte)tz)
                        .Byte(7).Byte(duration).UInt16(0).Byte(1).Byte(explode ? 1 : 0).ToArray();
                int ax = _avatar.X, ay = _avatar.Y, az = _avatar.Z;

                // A small house set down next to us (local only; removed again after the picture).
                const uint fakeHouse = 0x4FFFFFF0;
                _session.Inject(new PacketWriter(0xF3, 26).UInt16(1).Byte(2).UInt32(fakeHouse).UInt16(0x64).Byte(0)
                    .UInt16(1).UInt16(1).UInt16(ax + 7).UInt16(ay - 1).Byte((byte)(sbyte)az).Byte(0).UInt16(0).Byte(0).UInt16(0).ToArray(), false);
                yield return new WaitForSecondsRealtime(1.0f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_7_house.png"));
                yield return WaitFrames(10);
                int houseParts = _multis.TryGetValue(fakeHouse, out List<ItemView> hv) ? hv.Count : 0;
                _session.Inject(new PacketWriter(0x1D, 5).UInt32(fakeHouse).ToArray(), false);
                uiReport += $"\nhouse: {houseParts} parts drawn, {(_multis.ContainsKey(fakeHouse) ? "not removed" : "removed")}";
                _session.Inject(Effect(0, 0, 0, 0x36D4, ax - 6, ay - 4, az, ax + 4, ay + 3, az, 0, true), false);   // fireball
                _session.Inject(Effect(3, me, me, 0x376A, ax, ay, az, ax, ay, az, 40, false), false);              // sparkles
                _session.Inject(Effect(1, 0, 0, 0, ax + 2, ay - 2, az, ax + 2, ay - 2, az, 0, false), false);      // lightning
                yield return new WaitForSecondsRealtime(0.25f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_6_effects.png"));
                yield return WaitFrames(10);
                int effectsLive = _effects.Count;

                string tooltipReport = "no backpack items";
                uint packSerial = _session.World.Worn(_session.World.Player, 21);
                ContainedItem first = packSerial == 0 ? null : _session.World.ItemsIn(packSerial).FirstOrDefault();
                if (first != null)
                {
                    float tipUntil = Time.realtimeSinceStartup + 4f;
                    PropertyList props = null;
                    while ((props = _session.GetProperties(first.Serial)) == null && Time.realtimeSinceStartup < tipUntil)
                        yield return null;
                    tooltipReport = props == null ? $"no reply for 0x{first.Serial:X}" : $"0x{first.Serial:X}: " + string.Join(" / ", props.Lines);
                }
                // A footstep-like sound through the same path the server's 0x54 uses.
                _session.Inject(new PacketWriter(0x54, 12).Byte(1).UInt16(0x42).UInt16(0).UInt16(_avatar.X + 1).UInt16(_avatar.Y).UInt16(_avatar.Z).ToArray(), false);
                yield return WaitFrames(5);
                // Fifth set: a paperdoll item's tooltip (it must sit above the paperdoll), the chat window and the world map.
                string paperdollTip = "no paperdoll";
                _session.OpenPaperdoll();
                float pdUntil = Time.realtimeSinceStartup + 4f;
                while (!_ui.Windows.Any(w => w is UI.PaperdollWindow) && Time.realtimeSinceStartup < pdUntil)
                    yield return null;
                if (_ui.Windows.FirstOrDefault(w => w is UI.PaperdollWindow) is UI.UiWindow pd && _session.World.Player != null)
                {
                    EquippedItem worn = _session.World.Player.Equipment.Values.FirstOrDefault(eq => eq.Layer != 11 && eq.Layer != 21 && eq.Layer != 16);
                    _forcedHover = worn.Serial;
                    _forcedHoverAt = pd.Rect.position + new Vector2(60, 120); // over the paperdoll's body
                    float tipUntil2 = Time.realtimeSinceStartup + 4f;
                    while (_session.GetProperties(worn.Serial) == null && Time.realtimeSinceStartup < tipUntil2)
                        yield return null;
                    yield return new WaitForSecondsRealtime(0.6f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_8_paperdoll_tooltip.png"));
                    yield return WaitFrames(10);
                    paperdollTip = $"0x{worn.Serial:X} layer {worn.Layer}: {string.Join(" / ", _session.GetProperties(worn.Serial)?.Lines ?? new List<string>())}";
                    _forcedHover = 0;
                    _ui.Close(pd);
                }
                OnUiCommand("worldchat");
                OnUiCommand("worldmap");
                float mapUntil = Time.realtimeSinceStartup + 30f;
                yield return new WaitForSecondsRealtime(2f);
                while (Time.realtimeSinceStartup < mapUntil && !UI.WorldMapWindow.Ready(_session.World.Map))
                    yield return null;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "ui_9_chat_map.png"));
                yield return WaitFrames(10);
                // Sixth set: moving an item inside the backpack, then to the ground and back.
                string dragReport = "no item to move";
                uint myPack = _session.World.Worn(_session.World.Player, 21);
                ContainedItem moving = myPack == 0 ? null : _session.World.ItemsIn(myPack).FirstOrDefault(i => i.Graphic != 0x0EED); // not the gold
                if (moving != null)
                {
                    uint serial = moving.Serial;
                    int ox = moving.X, oy = moving.Y, nx = ox < 100 ? ox + 30 : ox - 30;
                    ContainedItem Now() => _session.World.ItemsIn(myPack).FirstOrDefault(i => i.Serial == serial);
                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                    _session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    _session.DropInto(serial, myPack, nx, oy);
                    float d1 = Time.realtimeSinceStartup + 3f;
                    while ((Now()?.X ?? ox) == ox && Time.realtimeSinceStartup < d1)
                        yield return null;
                    dragReport = $"0x{moving.Graphic:X4} moved in the pack {ox},{oy} -> {Now()?.X},{Now()?.Y}";

                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay

                    _session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    // A free tile next to us (a wall or stall would refuse it).
                    var free = new[] { (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, -1), (1, -1), (-1, 1) }.Select(o => (X: _avatar.X + o.Item1, Y: _avatar.Y + o.Item2))
                        .FirstOrDefault(c => _walk.TryStep(c.X, c.Y, _avatar.Z) != null);
                    if (free.X == 0) free = (_avatar.X + 1, _avatar.Y);
                    _session.DropOnGround(serial, free.X, free.Y, _walk.SurfaceZ(free.X, free.Y, _avatar.Z));
                    float d2 = Time.realtimeSinceStartup + 3f;
                    while (!_items.ContainsKey(serial) && Time.realtimeSinceStartup < d2)
                        yield return null;
                    bool onGround = _items.ContainsKey(serial) && _session.World.Items.ContainsKey(serial);
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield return new WaitForSecondsRealtime(1.2f); // the server refuses lifts faster than its action delay
                    _session.Lift(serial, moving.Amount);
                    yield return new WaitForSecondsRealtime(0.4f);
                    _session.DropInto(serial, myPack, ox, oy);
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
                    _session.World.GumpOpened += onGump;
                    _session.Say(command);
                    float gUntil = Time.realtimeSinceStartup + 8f;
                    while (got == null && Time.realtimeSinceStartup < gUntil)
                        yield return null;
                    _session.World.GumpOpened -= onGump;
                    yield return WaitFrames(20);
                    string shot = command.StartsWith("[groups") ? "ui_10_groups.png" : "ui_11_link.png";
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, shot));
                    yield return WaitFrames(10);
                    var win = got == null ? null : _ui.Windows.OfType<UI.ServerGumpWindow>().FirstOrDefault(w => w.Gump == got);
                    string reply = string.Join(" | ", _session.World.Journal.Skip(Math.Max(0, _session.World.Journal.Count - 3)).Where(j => j.Speaker == null).Select(j => j.Text));
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
                    int journalBefore = _session.World.Journal.Count;
                    yield return new WaitForSecondsRealtime(1.2f);
                    Hotkeys.Run(_session, action);
                    float mUntil = Time.realtimeSinceStartup + 6f;
                    while (Time.realtimeSinceStartup < mUntil)
                    {
                        Hotkeys.Update(_session); // answers the bandage's cursor when it comes
                        if (_session.World.Journal.Skip(journalBefore).Any(j => j.Speaker == null || j.Serial == _session.World.PlayerSerial))
                            break;
                        yield return null;
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                    macroReport += $"\n  {action}: " + string.Join(" | ", _session.World.Journal.Skip(journalBefore)
                        .Where(j => j.Speaker == null || j.Serial == _session.World.PlayerSerial).Select(j => j.Text).Take(3));
                }
                uiReport += $"\nmacros:{macroReport}";

                var byChannel = _chatLog.Lines.GroupBy(l => l.Channel).Select(g => $"{g.Key} {g.Count()}");
                uiReport += $"\npaperdoll tooltip: {paperdollTip}" +
                            $"\nchat: {_chatLog.Lines.Count} lines ({string.Join(", ", byChannel)}), {_chatLog.Lines.Count(l => l.Entry.Link != null)} with links" +
                            $"{(_chatLog.Lines.FirstOrDefault(l => l.Entry.Link != null) is var linked && linked.Entry.Link != null ? $", e.g. \"{linked.Entry.Text}\" -> [{linked.Entry.Link}" : "")}" +
                            $"\nworld map: {(UI.WorldMapWindow.Ready(_session.World.Map) ? "drawn" : "not ready")}; top bar {(_ui.Find("topbar") != null ? "open" : "missing")}";

                uiReport += $"\neffects: {effectsLive} running after 0.25 s\ntooltip: {tooltipReport}" +
                            $"\naudio: {Sound.SoundsPlayed} sounds played, music {(Sound.MusicPlaying ?? "none")}";
                uiReport += $"\nbuttons: {_ui.Windows.Count} windows ({string.Join(", ", _ui.Windows.Select(w => w.Key))}); {_session.Skills.Count} skills; " +
                            $"war mode {(warOn ? "on" : "never confirmed")}; anatomy cursor {(gotCursor ? "received and answered" : "never came")}; " +
                            $"{_overheads.Count} overheads; last journal: " +
                            string.Join(" | ", _session.World.Journal.Skip(Math.Max(0, _session.World.Journal.Count - 3)).Select(j => j.Text));
            }

            File.WriteAllText(Path.Combine(dir, "demo_capture_done.txt"),
                $"{(Application.isEditor ? "editor" : "player")} {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount}, target fps {Application.targetFrameRate}\n" +
                $"{(_session != null ? "online: " + _session.Status : "offline")}\n" +
                $"{startCut}\n" +
                $"route: {steps} steps from {startX},{startY} to {_avatar.X},{_avatar.Y}, {rejected} rejected\n" +
                $"frames during the run: {frameReport}\n" +
                $"hitches: {hitchReport}\n" +
                $"chunks: {_world.LoadedChunks} loaded, {_world.PendingChunks} pending, {_world.ChunksBuilt} built, worker {_world.WorkerMs / Math.Max(1, _world.ChunksBuilt):F1} ms/chunk\n" +
                $"character/item art: {_mobileArt.Built} built off-thread, {_mobileArt.WorkerMs / Math.Max(1, _mobileArt.Built):F1} ms each, {_mobileArt.PendingRequests} pending\n" +
                $"{_others.Count} mobiles, {_items.Count} items drawn\n" +
                $"slow packets: {(_session == null || _session.SlowPackets.Count == 0 ? "none" : string.Join(", ", _session.SlowPackets))}\n" +
                $"bodies: {BodyReport()}\n" +
                $"multis: {_multis.Count} ({_multis.Values.Sum(v => v.Count)} parts; ids {string.Join(", ", _session?.World.Items.Values.Where(i => i.IsMulti).Select(i => $"0x{i.Graphic:X}").Distinct() ?? Array.Empty<string>())})\n" +
                $"ui: {uiReport}");

            if (quitWhenDone)
                Application.Quit();
        }

        /// <summary>Each distinct body near us, how many, and how many are drawn (have art), for the tour report.</summary>
        private string BodyReport()
        {
            if (_session == null)
                return "offline";
            var parts = new List<string>();
            foreach (var g in _others.GroupBy(kv => _session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) ? m.Body : 0).OrderBy(g => g.Key))
            {
                int drawn = g.Count(kv => kv.Value.Visible);
                var mounted = g.Count(kv => _session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) && m.Equipment.ContainsKey(25));
                parts.Add($"0x{g.Key:X} x{g.Count()} ({drawn} drawn{(mounted > 0 ? $", {mounted} mounted" : "")})");
            }
            return string.Join(", ", parts);
        }

        public const string LoginCaptureRequestFile = "Library/UOClient/capture-login";

        /// <summary>Screenshots of the login form, character list and creation page (nothing is created), then back out.</summary>
        private IEnumerator LoginTour()
        {
            string dir = Path.Combine(ProjectRoot, "Library", "UOClient");
            yield return WaitFrames(30);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_1_form.png"));
            yield return WaitFrames(10);
            _login.LoginPressed(_login);
            float until = Time.realtimeSinceStartup + 10f;
            while (_login.Current != UI.LoginScreen.Page.Characters && Time.realtimeSinceStartup < until)
                yield return null;
            yield return WaitFrames(10);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_2_characters.png"));
            yield return WaitFrames(10);
            string report = $"page after login: {_login.Current}; status: {_login.Status}";
            if (_login.Current == UI.LoginScreen.Page.Characters)
            {
                _login.Current = UI.LoginScreen.Page.Create;
                yield return WaitFrames(20);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "login_3_create.png"));
                yield return WaitFrames(10);
                report += $"; characters [{string.Join(", ", _session.Characters)}]";
            }
            _session?.Dispose();
            _session = null;
            _login.Failed("");
            File.WriteAllText(Path.Combine(dir, "login_capture_done.txt"), report);
        }

        private static IEnumerator WaitFrames(int n)
        {
            for (int i = 0; i < n; i++)
                yield return null;
        }

        /// <summary>Written next to a player build: where the project (baked art, account file) lives on this machine.</summary>
        public const string PathsFileName = "unity-uo-paths.json";

        [Serializable]
        private sealed class PathsFile
        {
            public string projectRoot = "";
        }

        private static string _projectRoot;

        /// <summary>
        /// Folder relative paths (baked art, account file, capture output) resolve against: the project in the
        /// editor; in a player, the project named by the paths file next to the executable, else the player's folder.
        /// </summary>
        private static string ProjectRoot
        {
            get
            {
                if (_projectRoot != null)
                    return _projectRoot;
                string appFolder = Path.GetDirectoryName(Application.dataPath);
                _projectRoot = appFolder;
                string paths = Path.Combine(appFolder, PathsFileName);
                if (!Application.isEditor && File.Exists(paths))
                {
                    string root = JsonUtility.FromJson<PathsFile>(File.ReadAllText(paths)).projectRoot;
                    if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                        _projectRoot = root;
                }
                return _projectRoot;
            }
        }

        private void OnDestroy()
        {
            _session?.Dispose();
            _avatar?.Dispose();
            foreach (Avatar a in _others.Values)
                a.Dispose();
            foreach (ItemView v in _items.Values)
                v.Dispose();
            _world?.Dispose();
            _gumpArt?.Dispose();
            _mobileArt?.Dispose();
            _hud?.Dispose();
            _data?.Dispose();
        }

        private readonly struct TimeOfDay
        {
            public readonly string Name;
            public readonly Color Ambient, Light;
            public readonly float Vignette;

            public TimeOfDay(string name, Color ambient, Color light, float vignette)
            {
                Name = name;
                Ambient = ambient;
                Light = light;
                Vignette = vignette;
            }

            public static TimeOfDay Lerp(TimeOfDay a, TimeOfDay b, float t) =>
                new TimeOfDay(b.Name, Color.LerpUnclamped(a.Ambient, b.Ambient, t), Color.LerpUnclamped(a.Light, b.Light, t),
                              Mathf.Lerp(a.Vignette, b.Vignette, t));
        }
    }

    /// <summary>
    /// Times named parts of Update; when a frame turns out slow (seen at the start of the next one),
    /// records what that frame spent, including garbage collections, so hitches can be attributed.
    /// </summary>
    public sealed class FrameSections
    {
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private readonly List<(string Name, double Ms)> _current = new List<(string, double)>();

        // Unity's own main-thread phases, to attribute time spent outside our Update.
        private static readonly (string Label, global::Unity.Profiling.ProfilerCategory Category, string Marker)[] Markers =
        {
            ("player loop", global::Unity.Profiling.ProfilerCategory.Internal, "PlayerLoop"),
            ("editor loop", global::Unity.Profiling.ProfilerCategory.Internal, "EditorLoop"),
            ("render", global::Unity.Profiling.ProfilerCategory.Render, "Camera.Render"),
            ("GPU wait", global::Unity.Profiling.ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread"),
            ("IMGUI", global::Unity.Profiling.ProfilerCategory.Gui, "GUI.Repaint"),
            ("GC", global::Unity.Profiling.ProfilerCategory.Memory, "GC.Collect")
        };
        private readonly global::Unity.Profiling.ProfilerRecorder[] _recorders = Array.ConvertAll(Markers,
            m => global::Unity.Profiling.ProfilerRecorder.StartNew(m.Category, m.Marker));
        private string _open;
        private double _openedAt;
        private int _gcAtStart = GC.CollectionCount(0), _gc2AtStart = GC.CollectionCount(2);

        public readonly List<string> Hitches = new List<string>();
        public float HitchThresholdMs = 33f;

        public void Begin(string name)
        {
            if (!_watch.IsRunning)
                _watch.Start();
            double now = _watch.Elapsed.TotalMilliseconds;
            if (_open != null)
                _current.Add((_open, now - _openedAt));
            _open = name;
            _openedAt = now;
        }

        /// <summary>Call first thing in Update with the last frame's duration.</summary>
        public void EndFrame(float lastFrameSeconds)
        {
            Begin(null);
            double total = lastFrameSeconds * 1000.0;
            int gc0 = GC.CollectionCount(0) - _gcAtStart, gc2 = GC.CollectionCount(2) - _gc2AtStart;
            if (total > HitchThresholdMs && Hitches.Count < 40)
            {
                double inUpdate = _current.Sum(s => s.Ms);
                string parts = string.Join(", ", _current.Where(s => s.Ms >= 0.5).Select(s => $"{s.Name} {s.Ms:F1}"));
                var unity = new List<string>();
                for (int i = 0; i < Markers.Length; i++)
                {
                    double ms = _recorders[i].Valid ? _recorders[i].LastValue / 1e6 : -1;
                    if (ms >= 0.5)
                        unity.Add($"{Markers[i].Label} {ms:F1}");
                }
                Hitches.Add($"{total:F0} ms frame: Update {inUpdate:F1} ms [{parts}], outside Update {total - inUpdate:F0} ms, " +
                            $"GC gen0 {gc0} gen2 {gc2}; Unity: {string.Join(", ", unity)}");
            }
            _current.Clear();
            _watch.Restart();
            _gcAtStart = GC.CollectionCount(0);
            _gc2AtStart = GC.CollectionCount(2);
        }
    }

    /// <summary>Frame-time statistics: average, 99th percentile, worst, and hitches over 33 / 50 ms.</summary>
    public sealed class FrameStats
    {
        private readonly List<float> _ms = new List<float>(4096);

        public void Add(float seconds)
        {
            if (_ms.Count >= 20000)
                _ms.RemoveRange(0, 10000);
            _ms.Add(seconds * 1000f);
        }

        public void Reset() => _ms.Clear();

        public string Summary()
        {
            if (_ms.Count == 0)
                return "no frames";
            var sorted = new List<float>(_ms);
            sorted.Sort();
            float avg = sorted.Average();
            float p99 = sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            int over33 = sorted.Count(v => v > 33.4f), over50 = sorted.Count(v => v > 50f);
            return $"{sorted.Count} frames, avg {avg:F1} ms ({1000f / avg:F0} fps), p99 {p99:F1} ms, worst {sorted[sorted.Count - 1]:F1} ms, {over33} over 33 ms, {over50} over 50 ms";
        }
    }

    /// <summary>
    /// A server item on the ground (doors, signs, dropped things), drawn with its original art. The
    /// sprite is shared per (graphic, hue) and built in the background; the item appears once it is ready.
    /// </summary>
    public sealed class ItemView : IDisposable
    {
        private readonly MobileArt _art;
        private readonly int _scale;
        private readonly int _graphic, _hue;
        private DrawItem _item;
        private ItemSprite _sprite;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly MeshRenderer _renderer;

        public ItemView(UOData data, MobileArt art, Transform parent, Shader shader, int scale, WorldItem item)
        {
            _art = art;
            _scale = scale;
            _graphic = item.Graphic;
            _hue = item.Hue;
            StaticTileData td = data.TileData.GetStatic(item.Graphic);
            int priorityZ = item.Z + (td.IsBackground ? -1 : 0) + (td.Height > 0 ? 1 : 0);
            _item = new DrawItem { Kind = DrawKind.Static, Graphic = item.Graphic, X = item.X, Y = item.Y, Z = item.Z, PriorityZ = priorityZ };

            _go = new GameObject($"Item 0x{item.Graphic:X4}");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _shader = shader;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.enabled = false;
        }

        private readonly Shader _shader;

        // One material per atlas page, shared by every item on it (a house is ~150 parts), so they batch.
        private static readonly Dictionary<Texture, Material> PageMaterials = new Dictionary<Texture, Material>();

        private static Material MaterialFor(Shader shader, Texture page)
        {
            if (!PageMaterials.TryGetValue(page, out Material m) || m == null)
                PageMaterials[page] = m = new Material(shader) { mainTexture = page };
            return m;
        }

        /// <summary>Cheap: one quad. Re-done every frame because the depth origin moves as the player travels.</summary>
        /// <summary>Tile height (houses hide their parts above the player while inside).</summary>
        public int Z => _item.Z;

        public bool Hidden { get; set; }

        public void Render(IWorldSpace world)
        {
            if (_sprite == null)
            {
                _sprite = _art.GetItem(_graphic, _hue);
                if (_sprite == null)
                    return; // building, or no art
                (_item.Left, _item.Top, _item.Width, _item.Height) = IsoCompositor.PlacementRect(_item, _scale, _sprite.OriginalWidth, _sprite.OriginalHeight);
                _renderer.sharedMaterial = MaterialFor(_shader, _art.Atlas.Page(_sprite.Slot.Page));
            }
            _renderer.enabled = !Hidden;
            if (Hidden)
                return;
            LastRect = world.ToUnityRect(_item.Left, _item.Top, _item.Width, _item.Height);
            LastDepth = world.DepthFor(_item);
            MeshBuilder.SetQuad(_mesh, LastRect, _art.Atlas.Uv(_sprite.Slot), LastDepth, false, MeshBuilder.CutHeight(_item.Z));
        }

        /// <summary>Where the item was last drawn (Unity units) and its depth, for mouse picking; empty until its art is ready.</summary>
        public Rect LastRect { get; private set; }
        public float LastDepth { get; private set; }

        public void Dispose()
        {
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_go);
        }
    }
}
