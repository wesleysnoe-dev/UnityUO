using System;
using UnityEngine;
using UOClient.Core;
using UOClient.IO.Formats;
using UOClient.WorldLayer;

namespace UOClient.Unity
{
    /// <summary>
    /// The world editor's hands (WORLD_EDITOR.md step 2): F5 opens and closes the panel (<see cref="UI.WorldEditorWindow"/>,
    /// administrators only), the brush circle follows the mouse on the ground, and holding the left mouse over the world
    /// paints with the panel's mode at the panel's flow. While the panel is open and the mouse is over the world, the
    /// left mouse is the brush's (<see cref="CapturesLeftMouse"/>: WorldInput leaves it alone); the right mouse still walks.
    /// Ctrl+Z / Ctrl+Y undo and redo.
    /// </summary>
    public sealed class WorldEditorTool
    {
        /// <summary>Read by WorldInput: the left mouse belongs to the brush this frame.</summary>
        public static bool CapturesLeftMouse { get; private set; }

        private readonly GameContext _ctx;
        private readonly EntityViews _entities;
        private readonly Func<UI.UiManager> _ui;
        private float _nextStroke;
        private bool _painting;
        private int _strokes;

        public WorldEditorTool(GameContext ctx, EntityViews entities, Func<UI.UiManager> ui)
        {
            _ctx = ctx;
            _entities = entities;
            _ui = ui;
        }

        /// <summary>The brush centre under the mouse (world tile units), or null off the world.</summary>
        public Vector2? Cursor { get; private set; }

        private static WorldLayerClient Layer => WorldLayerClient.Current;

        private bool Open => _ui()?.Find(UI.WorldEditorWindow.WindowKey) != null;

        // -worldeditor-selftest: once connected as an editor, opens the panel and paints a short script near the player
        // (a hill, a sand path across it, a smoothed rim), so the whole loop can be checked without a hand on the mouse.
        private static readonly bool SelfTest = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-worldeditor-selftest", StringComparison.OrdinalIgnoreCase));
        private int _testStep = -1;
        private float _testAt;

        // -worldeditor-stresstest: long, wide drags like painting by hand (snow, a big raise, a big smooth), started
        // once the world has settled, with pauses between drags; for the frame timing in the log ([editprofile]).
        private static readonly bool StressTest = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-worldeditor-stresstest", StringComparison.OrdinalIgnoreCase));
        private int _stressStep = -1;
        private float _stressAt = -1f;
        private int _stressX, _stressY;
        private const int StressEnd = 330;

        // -groundfollow-selftest: raises a hill beside the character, then walks it back and forth over the hill, logging
        // each tile ([groundfollow]: server Z, UO's average land Z, the drawn ground's Z there) so GroundFollow can be read.
        private static readonly bool FollowTest = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-groundfollow-selftest", StringComparison.OrdinalIgnoreCase));
        private float _followAt = -1f;
        private int _followStep = -1;
        private int _followX, _followY;
        private (int, int, int) _followLast;

        // -shadow-selftest[=x,y]: the test admin goes to a wooded spot (walnut trees, 1700,1240 by default) for the shadows.
        private static readonly string ShadowTestArg = Array.Find(Environment.GetCommandLineArgs(),
            a => a.StartsWith("-shadow-selftest", StringComparison.OrdinalIgnoreCase));
        private float _shadowAt = -1f;
        private bool _shadowSent;

        private float _shadowLogAt;
        private bool _nearLightSent, _gumpsShown, _fxShown, _questionShown;
        private float _fxLogAt, _fxAt, _deathLogAt, _questionLogAt;
        private int _deathStep;
        private uint _deathVictim, _deathNext;
        private int _sayStep;
        private static readonly string[] SayLines =
        {
            "Hail, friend!",
            "Hast thou seen the ships come in at the docks today? They say a storm is brewing out past the isle.",
            "Bank",
        };
        private static readonly string[] StateLines =
        {
            "[self set Map Felucca",
            "[go 1434 1690",
            "[self unhide",
            "[self set Poison Lesser",
            "[self set Paralyzed true",
            "[self set Paralyzed false",
            "[self set Poison null",
            "[self hide",
            "[self unhide",
        };

        private void RunShadowTest(WorldLayerClient layer)
        {
            // -shadow-nearlight: once there, go and stand beside the nearest indoor flame (a lamp shadow to look at).
            if (_shadowSent && !_nearLightSent && Time.unscaledTime >= _shadowAt + 10f && _ctx.Session != null
                && Array.Exists(Environment.GetCommandLineArgs(), a => string.Equals(a, "-shadow-nearlight", StringComparison.OrdinalIgnoreCase)))
            {
                _nearLightSent = true;
                Vector3Int? best = null;
                var me = new Vector3Int(_ctx.Player.X, _ctx.Player.Y, _ctx.Player.Z);
                foreach (Vector3Int l in CharacterShadow.LightSources)
                    if (best == null || (l - me).sqrMagnitude < (best.Value - me).sqrMagnitude)
                        best = l;
                if (best is Vector3Int light)
                {
                    _ctx.Session.Say($"[go {light.x + 2} {light.y + 1} {light.z}");
                    Debug.Log($"[shadowtest] beside the indoor light at {light}");
                }
            }
            // -say-selftest: once there, the character speaks a few lines (short and long), for the speech bubbles;
            // -say-selftest=states: staff commands that poison, paralyze, clear and hide the character, 8 s apart.
            // -path-selftest=x,y: once in Britain (the states test's second line), the character pathfinds to that tile.
            if (!_pathSent && _shadowSent && Time.unscaledTime >= _shadowAt + 36f
                && Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("-path-selftest=", StringComparison.OrdinalIgnoreCase)) is string pathArg)
            {
                _pathSent = true;
                string[] xy = pathArg.Substring(pathArg.IndexOf('=') + 1).Split(',');
                if (xy.Length == 2 && int.TryParse(xy[0], out int tx) && int.TryParse(xy[1], out int ty))
                    PlayerMovement.TestPathTo = new Vector2Int(tx, ty);
            }
            // -grid-selftest[=WORD]: once there, the backpack opens as a grid window (for captures of the grid's tools).
            if (!_gridOpened && _shadowSent && Time.unscaledTime >= _shadowAt + 12f && _ctx.Session != null
                && Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("-grid-selftest", StringComparison.OrdinalIgnoreCase)) is string gridArg)
            {
                _gridOpened = true;
                // -grid-selftest=WORD: the window opens sorted, with WORD in its search box.
                int eq = gridArg.IndexOf('=');
                if (eq > 0)
                {
                    UI.GridContainerWindow.InitialSearch = gridArg.Substring(eq + 1);
                    UI.GridContainerWindow.AutoSort = true;
                }
                UI.GridContainerWindow.DefaultToOldStyleView = false;
                uint pack = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                if (pack != 0)
                    _ctx.Session.Use(pack);
            }
            // -death-selftest: the character goes to the woods by Britain's moongate and makes two bots; one is attacked, then
            // killed with the staff command, and the log follows what is left of them here; then the next nearest is
            // double-clicked (an attack) and the log says who the server made our combatant.
            if (_shadowSent && Time.unscaledTime >= _shadowAt + 14f && _ctx.Session != null
                && Array.Exists(Environment.GetCommandLineArgs(), x => string.Equals(x, "-death-selftest", StringComparison.OrdinalIgnoreCase)))
            {
                Core.World w = _ctx.Session.World;
                uint Nearest(uint not)
                {
                    uint best = 0;
                    int bestD = int.MaxValue;
                    foreach (Core.Mobile m in w.Mobiles.Values)
                    {
                        int d = Math.Max(Math.Abs(m.X - _ctx.Player.X), Math.Abs(m.Y - _ctx.Player.Y));
                        if (m.Serial != w.PlayerSerial && m.Serial != not && (m.Body == 400 || m.Body == 401) && d < bestD)
                        {
                            best = m.Serial;
                            bestD = d;
                        }
                    }
                    return best;
                }
                float t = Time.unscaledTime - _shadowAt - 14f;
                // Out of town (no guards): the woods south of Britain's moongate, alive, with two bots made beside it.
                int px = _ctx.Player.X, py = _ctx.Player.Y, pz = _ctx.Player.Z;
                var steps = new (float At, Action Do)[]
                {
                    (0f, () => _ctx.Session.Say("[self set Map Felucca")),
                    (1.5f, () => _ctx.Session.Say("[go 1336 2012")),
                    (3f, () => _ctx.Session.Say("[self resurrect")),
                    (4.5f, () => _ctx.Session.Say("[self set Blessed false")),
                    (6f, () => _ctx.Session.Say("[add playerbot")),
                    (7.5f, () => _ctx.Session.TargetGround(px + 2, py, pz)),
                    (9f, () => _ctx.Session.Say("[add playerbot")),
                    (10.5f, () => _ctx.Session.TargetGround(px - 2, py, pz)),
                    // The first bot down to one hit point, then we are a plain player (the server treats staff apart:
                    // they see ghosts and pass every attack rule), and it dies to a real attack.
                    (13f, () =>
                    {
                        _deathVictim = Nearest(0);
                        _ctx.Session.Say("[set Hits 1");
                    }),
                    (14.5f, () => _ctx.Session.TargetObject(_deathVictim)),
                    (16f, () => _ctx.Session.Say("[self set AccessLevel Player")),
                    (18f, () =>
                    {
                        _ctx.Session.SetWarMode(true);
                        Debug.Log($"UOClient death-selftest: attacking 0x{_deathVictim:X8}");
                    }),
                    (19f, () => _ctx.Session.DoubleClick(_deathVictim)),
                    (20f, () =>
                    {
                        if (_ctx.Session.PendingAttackQuery != 0)
                        {
                            Debug.Log("UOClient death-selftest: the attack question came up; answering yes");
                            _ctx.Session.AnswerAttackQuery(_ctx.Session.PendingAttackQuery, true);
                        }
                    }),
                    (34f, () =>
                    {
                        _deathNext = Nearest(_deathVictim);
                        Debug.Log($"UOClient death-selftest: attacking the next, 0x{_deathNext:X8}");
                        _ctx.Session.DoubleClick(_deathNext);
                    }),
                    (35f, () =>
                    {
                        if (_ctx.Session.PendingAttackQuery != 0)
                        {
                            Debug.Log("UOClient death-selftest: the attack question came up; answering yes");
                            _ctx.Session.AnswerAttackQuery(_ctx.Session.PendingAttackQuery, true);
                        }
                    }),
                };
                if (_deathStep < steps.Length && t >= steps[_deathStep].At)
                    steps[_deathStep++].Do();
                if (Time.unscaledTime >= _deathLogAt)
                {
                    _deathLogAt = Time.unscaledTime + 1f;
                    bool there = w.Mobiles.TryGetValue(_deathVictim, out Core.Mobile v);
                    Debug.Log($"UOClient death-selftest: {t:0.0} s: victim in world {there}" + (there ? $" body {v.Body} worn {v.Equipment.Count}" : "")
                              + $"; me dead {_ctx.Session.PlayerDead} query 0x{_ctx.Session.PendingAttackQuery:X8} windows [{string.Join(" ", UI.UiManager.OpenKeysForTest())}] war {_ctx.Session.WarMode} combatant 0x{_ctx.Session.Combatant:X8} last attack 0x{_ctx.Session.LastAttack:X8}");
                }
            }
            // -question-selftest: once there, the attack question ("This may flag you criminal!") opens (for captures).
            if (!_questionShown && _shadowSent && Time.unscaledTime >= _shadowAt + 12f
                && Array.Exists(Environment.GetCommandLineArgs(), x => string.Equals(x, "-question-selftest", StringComparison.OrdinalIgnoreCase)))
            {
                _questionShown = true;
                UI.UiManager.OpenForTest(new UI.QuestionWindow("attack", UI.QuestionWindow.CriminalText, yes => Debug.Log($"UOClient question-selftest: answered {yes}")));
            }
            if (_questionShown && Time.unscaledTime >= _questionLogAt)
            {
                _questionLogAt = Time.unscaledTime + 3f;
                Debug.Log($"UOClient question-selftest: windows [{string.Join(" ", UI.UiManager.OpenKeysForTest())}]");
            }
            // -tooltip-selftest: once there, the backpack's tooltip shows in the middle of the screen (for captures).
            if (_shadowSent && Time.unscaledTime >= _shadowAt + 12f && _ctx.Session != null && UI.HoverTooltip.ForcedForTest == null
                && Array.Exists(Environment.GetCommandLineArgs(), x => string.Equals(x, "-tooltip-selftest", StringComparison.OrdinalIgnoreCase)))
            {
                uint worn = _ctx.Session.World.Worn(_ctx.Session.World.Player, 21);
                if (worn != 0)
                    UI.HoverTooltip.ForcedForTest = (worn, new Vector2(700f, 300f));
            }
            // -fx-selftest: once there, an explosion and a heal's sparkles play on the character, and the log says every
            // 2 s how many of the FX layer's one-shot particle systems are still alive (they must all go).
            if (_shadowSent && Time.unscaledTime >= _shadowAt + 12f && _ctx.Session != null
                && Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("-fx-selftest", StringComparison.OrdinalIgnoreCase)) is string fxArg)
            {
                if (!_fxShown)
                {
                    _fxShown = true;
                    _fxAt = Time.unscaledTime;
                    Fx.FxParticles.LoopBurstsForTest = fxArg.EndsWith("=loop", StringComparison.OrdinalIgnoreCase); // the old behaviour
                    foreach (ushort graphic in new ushort[] { 0x36B0, 0x376A })
                        _ctx.Session.ShowLocalEffect(new Core.GraphicEffect { Type = Core.GraphicEffect.AtLocation, Graphic = graphic, Speed = 10, Duration = 16,
                                                                              SourceX = _ctx.Player.X, SourceY = _ctx.Player.Y, SourceZ = _ctx.Player.Z,
                                                                              TargetX = _ctx.Player.X, TargetY = _ctx.Player.Y, TargetZ = _ctx.Player.Z });
                }
                if (Time.unscaledTime >= _fxLogAt)
                {
                    _fxLogAt = Time.unscaledTime + (Time.unscaledTime - _fxAt < 3f ? 0.3f : 2f);
                    int bursts = 0, motes = 0;
                    foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                    {
                        bursts += ps.name == "Burst" ? 1 : 0;
                        motes += ps.name == "Motes" ? 1 : 0;
                    }
                    Debug.Log($"UOClient fx-selftest: {Time.unscaledTime - _fxAt:0.0} s after the effects: bursts alive {bursts}, motes alive {motes}");
                }
            }
            // -gump-selftest=FILE: once there, the gumps written in FILE open as if the server had sent them (for captures
            // of our own windows for server gumps). Per gump: a line "x y", the layout on one line, its text lines, "---".
            if (!_gumpsShown && _shadowSent && Time.unscaledTime >= _shadowAt + 12f && _ctx.Session != null
                && Array.Find(Environment.GetCommandLineArgs(), x => x.StartsWith("-gump-selftest=", StringComparison.OrdinalIgnoreCase)) is string gumpArg)
            {
                _gumpsShown = true;
                uint n = 0;
                string[] all = System.IO.File.ReadAllLines(gumpArg.Substring(gumpArg.IndexOf('=') + 1));
                var blocks = new System.Collections.Generic.List<System.Collections.Generic.List<string>> { new System.Collections.Generic.List<string>() };
                foreach (string line in all)
                {
                    if (line.Trim() == "---")
                        blocks.Add(new System.Collections.Generic.List<string>());
                    else
                        blocks[blocks.Count - 1].Add(line);
                }
                foreach (System.Collections.Generic.List<string> block in blocks)
                {
                    if (block.Count < 2)
                        continue;
                    string[] rows = block.ToArray();
                    string[] xy = rows[0].Split(' ');
                    var texts = new string[Math.Max(0, rows.Length - 2)];
                    Array.Copy(rows, 2, texts, 0, texts.Length);
                    n++;
                    _ctx.Session.ShowLocalGump(new Core.ServerGump { Serial = 0x7F000000 + n, TypeId = 0x7F000000 + n, X = int.Parse(xy[0]), Y = int.Parse(xy[1]),
                                                                    Commands = Core.ServerGump.ParseLayout(rows[1]), Lines = texts });
                }
            }
            string sayArg = Array.Find(Environment.GetCommandLineArgs(), a => a.StartsWith("-say-selftest", StringComparison.OrdinalIgnoreCase));
            string[] lines = sayArg != null && sayArg.EndsWith("=states", StringComparison.OrdinalIgnoreCase) ? StateLines : SayLines;
            float gap = lines == StateLines ? 6f : 2.5f;
            if (sayArg != null && _shadowSent && _sayStep < lines.Length && Time.unscaledTime >= _shadowAt + 22f + _sayStep * gap && _ctx.Session != null)
                _ctx.Session.Say(lines[_sayStep++]);
            if (_shadowSent && Time.unscaledTime >= _shadowLogAt)
            {
                _shadowLogAt = Time.unscaledTime + 3f;
                int px = _ctx.Player.X, py = _ctx.Player.Y, pz = _ctx.Player.Z;
                bool indoors = CharacterShadow.Indoors != null && CharacterShadow.Indoors(px, py, pz);
                Vector3Int? nearest = null;
                foreach (Vector3Int l in CharacterShadow.LightSources)
                    if (nearest == null || (l - new Vector3Int(px, py, pz)).sqrMagnitude < (nearest.Value - new Vector3Int(px, py, pz)).sqrMagnitude)
                        nearest = l;
                bool casts = CharacterShadow.Cast(px, py, pz, indoors, CharacterShadow.LightSources, out Vector2 pp, out float strength, out bool sun);
                Debug.Log($"[shadowtest] player {px},{py},{pz} cutZ {Shader.GetGlobalFloat("_UOCutZ")} indoors {indoors} lights {CharacterShadow.LightSources.Count} " +
                          $"nearest {nearest?.ToString() ?? "-"} casts {casts} strength {strength:0.00} sun {sun} perPixel {pp} now {CharacterShadow.CurrentPerPixel} sunShift {Shader.GetGlobalVector("_UOSunShift")} fade {Shader.GetGlobalFloat("_UOSunFade"):0.00}");
            }
            if (!layer.CanEdit || _shadowSent || _ctx.Session == null)
                return;
            if (_shadowAt < 0f)
            {
                _shadowAt = Time.unscaledTime + 6f;
                return;
            }
            if (Time.unscaledTime < _shadowAt)
                return;
            _shadowSent = true;
            string at = ShadowTestArg.Contains("=") ? ShadowTestArg.Substring(ShadowTestArg.IndexOf('=') + 1).Replace(',', ' ') : "1700 1240";
            _ctx.Session.Say("[go " + at);
            Debug.Log($"[shadowtest] going to {at}");
        }

        private void RunFollowTest(UI.UiManager ui, WorldLayerClient layer)
        {
            if (!layer.CanEdit)
                return;
            if (_followAt < 0f)
            {
                _followAt = Time.unscaledTime + 12f;
                return;
            }
            // Log every tile reached.
            var at = (_ctx.Player.X, _ctx.Player.Y, _ctx.Player.Z);
            if (_followStep > 40 && at != _followLast)
            {
                int? predicted = _ctx.Walk?.TryStep(at.Item1, at.Item2, _followLast.Item3);
                _followLast = at;
                int? avg = GroundFollow.LandAverageZ?.Invoke(at.Item1, at.Item2);
                float? drawn = GroundFollow.DrawnZ?.Invoke(at.Item1 + 0.5f, at.Item2 + 0.5f);
                MapFile map = _ctx.Data?.GetMap(_ctx.World.Map);
                string corners = map == null ? "-" : $"{map.GetLand(at.Item1, at.Item2).Z}/{map.GetLand(at.Item1 + 1, at.Item2).Z}/{map.GetLand(at.Item1, at.Item2 + 1).Z}/{map.GetLand(at.Item1 + 1, at.Item2 + 1).Z}";
                string painted = $"{layer.HeightAt(at.Item1, at.Item2)?.ToString("0.0") ?? "-"}/{layer.HeightAt(at.Item1 + 0.5f, at.Item2 + 0.5f)?.ToString("0.0") ?? "-"}";
                Debug.Log($"[groundfollow] tile {at.Item1},{at.Item2} server Z {at.Item3} land avg {avg?.ToString() ?? "-"} drawn {drawn?.ToString("0.0") ?? "-"} " +
                          $"on land {GroundFollow.OnLand(at.Item1, at.Item2, at.Item3)} corners t/r/l/b {corners} layer corner/centre {painted} predicted {predicted?.ToString() ?? "blocked"}");
            }
            if (Time.unscaledTime < _followAt)
                return;
            _followAt = Time.unscaledTime + 0.1f;
            _followStep++;
            WorldBrushSettings b = layer.Brush;
            if (_followStep == 0)
            {
                _followX = _ctx.Player.X;
                _followY = _ctx.Player.Y;
                Debug.Log($"[groundfollow] self-test start at {_followX},{_followY},{_ctx.Player.Z}; ground follow {(GroundFollow.On ? "on" : "off")}");
                if (_ctx.Session != null)
                    _ctx.Session.MoveRejected += (x, y, z, d) => Debug.Log($"[groundfollow] server rejected a step: back to {x},{y},{z}");
            }
            else if (_followStep <= 30) // a hill 4 tiles screen-right of the character (tile x+3, y-3)
            {
                b.Current = WorldBrushSettings.Mode.Raise; b.Size = 8f; b.Softness = 0.9f; b.Strength = 0.22f;
                layer.Stroke(_followX + 3.5f, _followY - 2.5f);
            }
            else if (_followStep == 31)
            {
                layer.EndStroke();
                _followAt = Time.unscaledTime + 3f;
            }
            else if (_followStep <= 40)
                PlayerMovement.TestIntent = Vector2.zero;
            else // walk across the hill and back, 6 s each way, for as long as the test runs
                PlayerMovement.TestIntent = ((_followStep - 41) / 60) % 2 == 0 ? new Vector2(1f, 0f) : new Vector2(-1f, 0f);
        }

        private void RunStressTest(UI.UiManager ui, WorldLayerClient layer)
        {
            if (!layer.CanEdit)
                return;
            if (_stressAt < 0f)
            {
                _stressAt = Time.unscaledTime + 15f;
                return;
            }
            if (Time.unscaledTime < _stressAt)
                return;
            WorldBrushSettings b = layer.Brush;
            _stressAt = Time.unscaledTime + 0.1f; // 10 strokes a second, the panel's default flow
            _stressStep++;
            int s = _stressStep;
            if (s == 0)
            {
                _stressX = _ctx.Player.X;
                _stressY = _ctx.Player.Y;
                if (ui.Find(UI.WorldEditorWindow.WindowKey) == null)
                    ui.Open(new UI.WorldEditorWindow(layer));
                Debug.Log("[editprofile] stress test: start");
            }
            else if (s <= 120) // drag 1: snow, 6 tiles wide, 30 tiles across
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Ground = 0; b.Material = WorldMaterials.Snow;
                b.Size = 6f; b.Softness = 0.6f; b.Strength = 0.5f;
                layer.Stroke(_stressX - 15f + s * 0.25f, _stressY + 6.5f);
            }
            else if (s == 121)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 1 done");
                _stressAt = Time.unscaledTime + 4f;
            }
            else if (s <= 221) // drag 2: a big raise, slowly across
            {
                b.Current = WorldBrushSettings.Mode.Raise; b.Size = 20f; b.Softness = 0.8f; b.Strength = 0.5f;
                layer.Stroke(_stressX - 8f + (s - 122) * 0.16f, _stressY - 6.5f);
            }
            else if (s == 222)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 2 done");
                _stressAt = Time.unscaledTime + 4f;
            }
            else if (s <= 302) // drag 3: flatten back to the Z 0 plain across both
            {
                b.Current = WorldBrushSettings.Mode.SetZ; b.TargetOffset = 0f; b.Size = 10f; b.Softness = 0.5f; b.Strength = 1f;
                layer.Stroke(_stressX - 10f + (s - 223) * 0.25f, _stressY);
            }
            else if (s == 303)
            {
                layer.EndStroke();
                Debug.Log("[editprofile] stress test: drag 3 done");
                _stressAt = Time.unscaledTime + 12f;
            }
            else if (s == StressEnd)
                Debug.Log("[editprofile] stress test: end");
        }

        private void RunSelfTest(UI.UiManager ui, WorldLayerClient layer)
        {
            if (!layer.CanEdit || Time.unscaledTime < _testAt)
                return;
            int px = _ctx.Player.X, py = _ctx.Player.Y;
            WorldBrushSettings b = layer.Brush;
            _testAt = Time.unscaledTime + 0.08f;
            _testStep++;
            if (_testStep == 0)
            {
                if (ui.Find(UI.WorldEditorWindow.WindowKey) == null)
                    ui.Open(new UI.WorldEditorWindow(layer));
                _ctx.Hud?.Log(null, "World editor self-test: painting near you.");
            }
            else if (_testStep <= 30) // raise a hill 5 tiles right-down of the player
            {
                b.Current = WorldBrushSettings.Mode.Raise; b.Size = 5f; b.Softness = 0.9f; b.Strength = 0.6f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep <= 70) // a sand path across it
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Material = WorldMaterials.Sand; b.Size = 1.6f; b.Softness = 0.7f; b.Strength = 0.7f;
                float t = (_testStep - 31) / 39f;
                layer.Stroke(px - 2f + t * 14f, py - 4f + t * 9f);
            }
            else if (_testStep <= 80) // rock on the hill top
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Material = WorldMaterials.Rock; b.Size = 2f; b.Softness = 0.8f; b.Strength = 0.5f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep <= 90) // soften the rim
            {
                b.Current = WorldBrushSettings.Mode.Soft; b.Size = 7f; b.Softness = 0.6f; b.Strength = 0.5f;
                layer.Stroke(px + 5.5f, py + 0.5f);
            }
            else if (_testStep <= 96) // SURFACE: a patch of UO wooden floor beside the player
            {
                b.Current = WorldBrushSettings.Mode.Paint; b.Size = 2f; b.Softness = 0.1f; b.Strength = 1f;
                LandTileData[] land = _ctx.Data.TileData.Land;
                b.Ground = (ushort)Math.Max(0, Array.FindIndex(land, l => string.Equals(l.Name?.Trim(), "wooden floor", StringComparison.OrdinalIgnoreCase)));
                layer.Stroke(px - 3.5f, py + 3.5f);
                b.Ground = 0;
            }
            else if (_testStep == 97)
            {
                layer.EndStroke();
                b.Current = WorldBrushSettings.Mode.Raise;
                (ui.Find(UI.WorldEditorWindow.WindowKey) as UI.WorldEditorWindow)?.ShowSurface("wooden floor");
            }
            else if (_testStep <= 101) // OBJECTS: a short stone wall north of the player
            {
                b.Current = WorldBrushSettings.Mode.Object;
                b.ObjectGraphic = StaticNamed("stone wall");
                int x = px + 1 + (_testStep - 98), y = py - 3;
                layer.PlaceObject(x, y, PlaceZ(x, y, 0));
            }
            else if (_testStep == 102) // and a tree west of it
            {
                b.ObjectGraphic = StaticNamed("tree");
                layer.PlaceObject(px - 3, py - 3, PlaceZ(px - 3, py - 3, 0));
            }
            else if (_testStep == 110 && layer.SelectedItem is WorldItem tree) // the tree (just placed, so selected) one tile south
                layer.MoveObject(tree.Serial, tree.X, tree.Y + 1, tree.Z);
            else if (_testStep == 116) // and back (undo the move)
                layer.Undo(false);
            else if (_testStep == TestEnd)
            {
                b.Current = WorldBrushSettings.Mode.Select;
                (ui.Find(UI.WorldEditorWindow.WindowKey) as UI.WorldEditorWindow)?.ShowObjects("walls", "stone wall");
                _ctx.Hud?.Log(null, "World editor self-test: done.");
            }
        }

        private const int TestEnd = 122;

        /// <summary>The first item graphic with this tiledata name (the self-test's objects).</summary>
        private ushort StaticNamed(string name)
        {
            StaticTileData[] items = _ctx.Data.TileData.Statics;
            int g = Array.FindIndex(items, 1, items.Length - 1, s => string.Equals(s.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return (ushort)Math.Max(0, g);
        }

        /// <summary>Every frame. <paramref name="takesKeys"/>: the world has the keyboard (not typing in chat or a text box).</summary>
private bool _gridOpened, _pathSent;

                public void Update(bool takesKeys)
        {
            UI.UiManager ui = _ui();
            WorldLayerClient layer = Layer;
            CapturesLeftMouse = false;
            Cursor = null;
            if (ui == null || layer == null || _ctx.World == null || _ctx.Player == null)
                return;
            if (SelfTest && _testStep < TestEnd)
                RunSelfTest(ui, layer);
            if (StressTest && _stressStep < StressEnd)
                RunStressTest(ui, layer);
            if (FollowTest)
                RunFollowTest(ui, layer);
            if (ShadowTestArg != null)
                RunShadowTest(layer);

            if (takesKeys && Input.GetKeyDown(KeyCode.F5))
            {
                if (ui.Find(UI.WorldEditorWindow.WindowKey) is UI.UiWindow w)
                    ui.Close(w);
                else if (layer.CanEdit)
                    ui.Open(new UI.WorldEditorWindow(layer));
                else
                    _ctx.Hud?.Log(null, "World editor: " + (string.IsNullOrEmpty(layer.Status) ? "not connected to an editing server." : layer.Status));
            }
            if (!Open)
            {
                StopPainting();
                return;
            }
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (takesKeys && ctrl && Input.GetKeyDown(KeyCode.Z))
                layer.Undo(false);
            if (takesKeys && ctrl && Input.GetKeyDown(KeyCode.Y))
                layer.Undo(true);

            WorldBrushSettings brush = layer.Brush;
            brush.PlayerZ = _ctx.Player.Z; // FLAT's target and the panel's readout follow you
            if (brush.ObjectMode && takesKeys)
                ObjectKeys(layer, brush);

            bool overWorld = !ui.IsOverUi(Input.mousePosition) && ui.Dragging == null;
            if (!overWorld)
            {
                if (!Input.GetMouseButton(0))
                {
                    StopPainting();
                    _dragging = 0;
                    _sloping = false; // let go over a window: no ramp
                }
                if (!_painting && _dragging == 0 && !_sloping)
                    return;
            }
            Cursor = MouseTile();
            CapturesLeftMouse = true;
            if (Cursor is not Vector2 at)
                return;

            if (brush.ObjectMode)
            {
                StopPainting();
                UpdateObjects(layer, brush, at, overWorld);
                return;
            }
            if (brush.Current == WorldBrushSettings.Mode.Look)
            {
                if (Input.GetMouseButtonDown(0) && layer.HeightAt(at.x, at.y) is float picked)
                {
                    brush.TargetOffset = Mathf.Round(picked) - _ctx.Player.Z; // the height picked, relative to you
                    brush.Current = WorldBrushSettings.Mode.SetZ;
                    _ctx.Hud?.Log(null, $"World editor: flatten to Z {brush.TargetZ:0} ({brush.TargetOffset:+0;-0;0} from you)");
                }
                return;
            }
            if (brush.Current == WorldBrushSettings.Mode.Slope)
            {
                // SLOPE: press at one end, let go at the other: one ramp between the two ground heights.
                if (Input.GetMouseButtonDown(0) && overWorld)
                {
                    _sloping = true;
                    _slopeStart = at;
                    _slopeStartZ = GroundZ(at.x, at.y);
                }
                else if (_sloping && !Input.GetMouseButton(0))
                {
                    _sloping = false;
                    if ((at - _slopeStart).magnitude >= 1f)
                    {
                        layer.StrokeSlope(_slopeStart, _slopeStartZ, at, GroundZ(at.x, at.y));
                        layer.EndStroke();
                    }
                }
                return;
            }
            if (Input.GetMouseButtonDown(0) && overWorld)
            {
                _painting = true;
                _strokes = 0;
                _nextStroke = 0f;
            }
            if (_painting && Input.GetMouseButton(0))
            {
                if (Time.unscaledTime >= _nextStroke)
                {
                    _nextStroke = Time.unscaledTime + 1f / Mathf.Max(1f, brush.Flow);
                    layer.Stroke(at.x, at.y);
                    _strokes++;
                }
            }
            else
                StopPainting();
        }

        // ---- OBJECTS: place, select, drag to move, PageUp / PageDown height, Delete ----

        private uint _dragging;
        private Vector2Int _dragFrom;

        private static int Tile(float v) => Mathf.FloorToInt(v);

        /// <summary>The height an object goes in at a tile: the ground's there, plus the panel's lift.</summary>
        private int PlaceZ(int x, int y, int lift) => Mathf.RoundToInt(GroundZ(x, y)) + lift;

        private void ObjectKeys(WorldLayerClient layer, WorldBrushSettings brush)
        {
            int step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 5 : 1;
            int dz = Input.GetKeyDown(KeyCode.PageUp) ? step : Input.GetKeyDown(KeyCode.PageDown) ? -step : 0;
            WorldItem sel = layer.SelectedItem;
            if (dz != 0)
            {
                if (brush.Current == WorldBrushSettings.Mode.Select && sel != null)
                    layer.MoveObject(sel.Serial, sel.X, sel.Y, sel.Z + dz);
                else
                    brush.ObjectLift = Mathf.Clamp(brush.ObjectLift + dz, -60, 120);
            }
            if (sel != null && (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace)))
                layer.DeleteObject(sel.Serial);
        }

        private void UpdateObjects(WorldLayerClient layer, WorldBrushSettings brush, Vector2 at, bool overWorld)
        {
            int tx = Tile(at.x), ty = Tile(at.y);
            if (brush.Current == WorldBrushSettings.Mode.Object)
            {
                if (Input.GetMouseButtonDown(0) && overWorld && brush.ObjectGraphic != 0)
                    layer.PlaceObject(tx, ty, PlaceZ(tx, ty, brush.ObjectLift));
                return;
            }
            if (Input.GetMouseButtonDown(0) && overWorld)
            {
                uint serial = _entities.PickAt(Input.mousePosition);
                bool isItem = serial != 0 && _ctx.Session.World.Items.TryGetValue(serial, out WorldItem item) && !item.IsMulti;
                brush.Selected = isItem ? serial : 0;
                _dragging = brush.Selected;
                _dragFrom = new Vector2Int(tx, ty);
            }
            if (_dragging != 0 && !Input.GetMouseButton(0))
            {
                // Dropped on another tile: it keeps its height above the ground.
                if ((tx != _dragFrom.x || ty != _dragFrom.y) && _ctx.Session.World.Items.TryGetValue(_dragging, out WorldItem moved))
                {
                    int above = moved.Z - Mathf.RoundToInt(GroundZ(moved.X, moved.Y));
                    layer.MoveObject(_dragging, tx, ty, PlaceZ(tx, ty, above));
                }
                _dragging = 0;
            }
        }

        // SLOPE's drag: where it began and the ground height there.
        private bool _sloping;
        private Vector2 _slopeStart;
        private float _slopeStartZ;

        private void StopPainting()
        {
            if (!_painting)
                return;
            _painting = false;
            if (_strokes > 0)
                Layer?.EndStroke();
            _strokes = 0;
        }

        /// <summary>
        /// The world point (tile units) under the mouse on the ground: the screen point back through the iso projection,
        /// twice, the second time at the ground's height where the first guess landed.
        /// </summary>
        private Vector2? MouseTile()
        {
            Vector2 unity = _entities.ScreenToWorld(Input.mousePosition);
            Vector2 px = _ctx.World.FromUnity(unity);
            float s = _ctx.ArtScale;
            float z = _ctx.Player.Z;
            float x = 0, y = 0;
            for (int pass = 0; pass < 3; pass++)
            {
                float u = px.x / (22f * s), v = (px.y / s + z * 4f) / 22f;
                x = (v + u) * 0.5f;
                y = (v - u) * 0.5f;
                z = GroundZ(x, y);
            }
            return new Vector2(x, y);
        }

        private float GroundZ(float x, float y)
        {
            if (Layer?.HeightAt(x, y) is float h)
                return h;
            MapFile map = _ctx.Data?.GetMap(_ctx.World.Map);
            if (map == null)
                return _ctx.Player.Z;
            int ix = Mathf.Clamp(Mathf.FloorToInt(x), 0, map.Width - 1), iy = Mathf.Clamp(Mathf.FloorToInt(y), 0, map.Height - 1);
            return map.GetLand(ix, iy).Z;
        }

        private static Texture2D _dot;

        /// <summary>The brush circle on the ground under the mouse (OnGUI, before the windows).</summary>
        public void DrawOverlay()
        {
            if (Layer == null || _ctx.Camera == null || Event.current.type != EventType.Repaint || !Open)
                return;
            _dot ??= Texture2D.whiteTexture;
            WorldBrushSettings brush = Layer.Brush;
            if (brush.ObjectMode)
            {
                DrawObjectOverlay(brush);
                return;
            }
            if (Cursor is not Vector2 at)
                return;
            float r = brush.Current == WorldBrushSettings.Mode.Look ? 0.3f : brush.Size;
            float inner = r * (1f - brush.Softness);
            var edge = brush.Current == WorldBrushSettings.Mode.Erase ? new Color(1f, 0.55f, 0.45f, 0.95f) : new Color(0.75f, 0.95f, 0.45f, 0.95f);
            var soft = new Color(edge.r, edge.g, edge.b, 0.45f);
            BrushShape shape = brush.Current == WorldBrushSettings.Mode.Look || brush.Current == WorldBrushSettings.Mode.Slope ? BrushShape.Circle : brush.Shape;
            if (brush.Current == WorldBrushSettings.Mode.Slope && _sloping)
            {
                // The ramp to be: its two ends, its sides, and the heights it runs between.
                Vector2 dir = at - _slopeStart;
                Vector2 side = dir.sqrMagnitude > 1e-4f ? new Vector2(-dir.y, dir.x).normalized * r : Vector2.zero;
                DrawRing(_slopeStart, r, edge, 48);
                DrawRing(at, r, edge, 48);
                int points = Mathf.Clamp(Mathf.RoundToInt(dir.magnitude * 6f), 6, 200);
                DrawLine(_slopeStart + side, at + side, edge, points);
                DrawLine(_slopeStart - side, at - side, edge, points);
                DrawLine(_slopeStart, at, soft, points);
                DrawLabel(_slopeStart, "Z " + _slopeStartZ.ToString("0"));
                DrawLabel(at, "Z " + GroundZ(at.x, at.y).ToString("0"));
                return;
            }
            if (shape == BrushShape.Tile)
            {
                // The exact tiles: the footprint's outline, and the tile lines inside it.
                (int x0, int y0, int n) = brush.StrokeAt(at.x, at.y).TileFootprint();
                for (int k = 0; k <= n; k++)
                {
                    Color c = k == 0 || k == n ? edge : soft;
                    DrawLine(new Vector2(x0 + k, y0), new Vector2(x0 + k, y0 + n), c, n * 8);
                    DrawLine(new Vector2(x0, y0 + k), new Vector2(x0 + n, y0 + k), c, n * 8);
                }
            }
            else if (shape == BrushShape.Square)
            {
                DrawSquare(at, r, edge);
                if (inner > 0.2f && inner < r - 0.1f)
                    DrawSquare(at, inner, soft);
            }
            else
            {
                DrawRing(at, r, edge, 64);
                if (inner > 0.2f && inner < r - 0.1f)
                    DrawRing(at, inner, soft, 48);
            }
            DrawPoint(at, new Color(1f, 1f, 1f, 0.9f), 4f);
        }

        private static GUIStyle _labelStyle;

        private void DrawLabel(Vector2 tile, string text)
        {
            float z = GroundZ(tile.x, tile.y), s = _ctx.ArtScale;
            Vector3 world = _ctx.World.ToUnity(new Vector2((tile.x - tile.y) * 22f, (tile.x + tile.y) * 22f - z * 4f) * s);
            Vector3 screen = _ctx.Camera.WorldToScreenPoint(world);
            _labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            GUI.Label(new Rect(screen.x + 8f, Screen.height - screen.y - 22f, 120f, 22f), text, _labelStyle);
        }

        private void DrawSquare(Vector2 centre, float half, Color color)
        {
            Vector2 a = centre + new Vector2(-half, -half), b = centre + new Vector2(half, -half);
            Vector2 c = centre + new Vector2(half, half), d = centre + new Vector2(-half, half);
            int points = Mathf.Clamp(Mathf.RoundToInt(half * 12f), 8, 96);
            DrawLine(a, b, color, points);
            DrawLine(b, c, color, points);
            DrawLine(c, d, color, points);
            DrawLine(d, a, color, points);
        }

        private void DrawLine(Vector2 from, Vector2 to, Color color, int points)
        {
            for (int i = 0; i <= points; i++)
                DrawPoint(Vector2.Lerp(from, to, i / (float)points), color, 3f);
        }

        /// <summary>OBJECTS: the selected object's tile outlined, and the object to place (or the one dragged) as a ghost.</summary>
        private void DrawObjectOverlay(WorldBrushSettings brush)
        {
            if (Layer.SelectedItem is WorldItem sel)
                DrawTileOutline(sel.X, sel.Y, sel.Z, new Color(1f, 0.85f, 0.25f, 0.95f));
            if (Cursor is not Vector2 at)
                return;
            int tx = Tile(at.x), ty = Tile(at.y);
            if (brush.Current == WorldBrushSettings.Mode.Object && brush.ObjectGraphic != 0)
            {
                int z = PlaceZ(tx, ty, brush.ObjectLift);
                DrawTileOutline(tx, ty, z, new Color(0.75f, 0.95f, 0.45f, 0.9f));
                DrawGhost(brush.ObjectGraphic, brush.ObjectHue, tx, ty, z, 0.6f);
            }
            else if (_dragging != 0 && _ctx.Session.World.Items.TryGetValue(_dragging, out WorldItem dragged))
            {
                int z = PlaceZ(tx, ty, dragged.Z - Mathf.RoundToInt(GroundZ(dragged.X, dragged.Y)));
                DrawTileOutline(tx, ty, z, new Color(0.75f, 0.95f, 0.45f, 0.9f));
                DrawGhost(dragged.Graphic, dragged.Hue, tx, ty, z, 0.5f);
            }
            else
                DrawTileOutline(tx, ty, Mathf.RoundToInt(GroundZ(tx, ty)), new Color(1f, 1f, 1f, 0.5f));
        }

        /// <summary>A screen point (GUI, top-left origin) of a world point at height z.</summary>
        private Vector2 ScreenAt(float x, float y, float z)
        {
            float s = _ctx.ArtScale;
            Vector3 world = _ctx.World.ToUnity(new Vector2((x - y) * 22f, (x + y) * 22f - z * 4f) * s);
            Vector3 p = _ctx.Camera.WorldToScreenPoint(world);
            return new Vector2(p.x, UnityEngine.Screen.height - p.y);
        }

        private void DrawTileOutline(int x, int y, int z, Color color)
        {
            Vector2 a = ScreenAt(x, y, z), b = ScreenAt(x + 1, y, z), c = ScreenAt(x + 1, y + 1, z), d = ScreenAt(x, y + 1, z);
            Color saved = GUI.color;
            GUI.color = color;
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                foreach (Vector2 p in new[] { Vector2.Lerp(a, b, t), Vector2.Lerp(b, c, t), Vector2.Lerp(c, d, t), Vector2.Lerp(d, a, t) })
                    GUI.DrawTexture(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), _dot);
            }
            GUI.color = saved;
        }

        /// <summary>An item's art standing on a tile as the world draws statics (bottom centre on the tile's lower corner).</summary>
        private void DrawGhost(int graphic, int hue, int x, int y, int z, float alpha)
        {
            UI.UiImage img = _ui()?.Art?.Item(graphic, hue);
            if (img?.Texture == null || img.Bitmap == null)
                return;
            Vector2 foot = ScreenAt(x + 1, y + 1, z);
            float px = (ScreenAt(x + 2, y + 2, z).y - foot.y) / 44f; // screen pixels per art pixel
            float w = img.Bitmap.Width * px, h = img.Bitmap.Height * px;
            Color saved = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(foot.x - w * 0.5f, foot.y - h, w, h), img.Texture);
            GUI.color = saved;
        }

        private void DrawRing(Vector2 centre, float radius, Color color, int points)
        {
            for (int i = 0; i < points; i++)
            {
                float a = i * Mathf.PI * 2f / points;
                DrawPoint(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, 3f);
            }
        }

        private void DrawPoint(Vector2 tile, Color color, float size)
        {
            float z = GroundZ(tile.x, tile.y), s = _ctx.ArtScale;
            Vector3 world = _ctx.World.ToUnity(new Vector2((tile.x - tile.y) * 22f, (tile.x + tile.y) * 22f - z * 4f) * s);
            Vector3 screen = _ctx.Camera.WorldToScreenPoint(world);
            Color saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(screen.x - size * 0.5f, Screen.height - screen.y - size * 0.5f, size, size), _dot);
            GUI.color = saved;
        }
    }
}
