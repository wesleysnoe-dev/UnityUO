using System;
using System;
using UnityEngine;

namespace UOClient.Unity
{
    /// <summary>Walking: keyboard or right-mouse intent, local prediction and, online, server-paced steps.</summary>
    public sealed class PlayerMovement
    {
        private readonly GameContext _ctx;
        private double _nextStepAt;

        public PlayerMovement(GameContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>The options' always run (TazUO's AlwaysRun): every step runs, Shift or not.</summary>
        public bool AlwaysRun { get; set; }

        /// <summary>The options' "unless hidden" (TazUO's AlwaysRunUnlessHidden): always run leaves a hidden player walking.</summary>
        public bool AlwaysRunUnlessHidden { get; set; } = true;

        /// <summary>The player's hidden flag (0x80 in the mobile's flags, as TazUO's Flags.Hidden).</summary>
        private bool PlayerHidden => _ctx.Session?.World.Player is Core.Mobile p && (p.Flags & 0x80) != 0;

        private bool _rightPressOnUi;

        /// <summary>WASD / arrows (Shift runs), or right mouse toward the cursor while over the world.</summary>
        public void Update(UI.UiManager ui)
        {
            // Screen-space intent: keyboard, or right mouse toward the cursor (far cursor = run, like the classic client).
            Vector2 intent = Vector2.zero;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) intent.y -= 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) intent.y += 1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) intent.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) intent.x += 1;
            bool run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                       || AlwaysRun && !(AlwaysRunUnlessHidden && PlayerHidden);

            // Right mouse walks only over the world; over a window it closes the window instead.
            // (Nor over the quest arrow, where a right click cancels it, nor while a press that began on a nameplate or
            // a window is held: that right-click closed it, and the world under the mouse once it is gone is no walk.
            // A walk begun on the world carries on over plates, as TazUO's.)
            if (!Input.GetMouseButton(1))
                _rightPressOnUi = false;
            else if (Input.GetMouseButtonDown(1))
                _rightPressOnUi = _ctx.Hud.NamePlateAt(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)) != 0
                                     || ui != null && (ui.IsOverUi(Input.mousePosition) || ui.RightPressOnWindow);
            // The options' pathfinding: a right-click (double, or single; with Shift if asked) on the world walks there.
            if (Input.GetMouseButtonDown(1) && !_rightPressOnUi && TileAtScreen != null && _ctx.Session != null)
            {
                Vector2 at = Input.mousePosition;
                bool twice = Time.realtimeSinceStartup - _lastRightDown < 0.35f && (at - _lastRightDownAt).sqrMagnitude < 100f;
                _lastRightDown = Time.realtimeSinceStartup;
                _lastRightDownAt = at;
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (GameOptions.PathfindClick(EnablePathfind, UseShiftToPathfind, PathfindSingleClick, shift, twice))
                {
                    Vector2Int tile = TileAtScreen(at);
                    if (!PathTo(tile.x, tile.y))
                        _ctx.Hud.Log(null, "You can't find a way there.");
                    _pathPressHeld = true; // this press is the click, not a walk
                    _lastRightDown = 0;
                }
            }
            if (!Input.GetMouseButton(1))
                _pathPressHeld = false;
            if (intent == Vector2.zero && Input.GetMouseButton(1) && !_rightPressOnUi && !_pathPressHeld && (ui == null || !ui.IsOverUi(Input.mousePosition))
                && !_ctx.Hud.QuestArrowRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))
            {
                Vector3 player = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(_ctx.Player.AnchorPixels));
                intent = new Vector2(Input.mousePosition.x - player.x, -(Input.mousePosition.y - player.y));
                if (intent.magnitude < 12f)
                    return;
                run |= intent.magnitude > Screen.height * 0.25f;
            }
            if (intent == Vector2.zero)
                intent = TestIntent; // self-tests walk the character (GroundFollow); zero otherwise
            if (intent == Vector2.zero)
            {
                if (TestPathTo is Vector2Int test && _ctx.Session != null)
                {
                    // -path-selftest: walk there and say in the log how it went.
                    TestPathTo = null;
                    _logPath = true;
                    bool found = PathTo(test.x, test.y);
                    Debug.Log($"UOClient path: from {_ctx.Player.X},{_ctx.Player.Y},{_ctx.Player.Z} to {test.x},{test.y}: "
                              + (found ? $"{_path.Count - 1} steps, ending at {_path[_path.Count - 1].X},{_path[_path.Count - 1].Y}" : "no way"));
                }
                if (_path != null)
                    WalkPath(run);
                return;
            }
            CancelPath(); // walking by hand takes over

            TryStep(DirectionFromScreen(intent), run);
        }

        // ---- pathfinding (the options' EnablePathfind; Core.PathFinder) ----

        /// <summary>TazUO's EnablePathfind, UseShiftToPathfind and PathfindSingleClick.</summary>
        public bool EnablePathfind { get; set; } = true;
        public bool UseShiftToPathfind { get; set; }
        public bool PathfindSingleClick { get; set; }

        /// <summary>The tile under a screen point (WorldInput.TileAt), for the click.</summary>
        public Func<Vector2, Vector2Int> TileAtScreen { get; set; }

        /// <summary>Whether an item graphic is a door, and whether closed doors count as passable (auto-open doors is on).</summary>
        public Func<ushort, bool> IsDoor { get; set; }
        public bool DoorsOpenThemselves { get; set; }

        /// <summary>A path is being walked.</summary>
        public bool Pathing => _path != null;

        private System.Collections.Generic.List<Core.PathFinder.Node> _path;
        private (int X, int Y) _pathTarget, _pathLastAt;
        private double _pathStuckSince;
        private bool _repathed, _pathPressHeld, _doorsAreWalls;
        private float _lastRightDown;
        private Vector2 _lastRightDownAt;
        private readonly System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<(int Z, int Height)>> _blockers =
            new System.Collections.Generic.Dictionary<long, System.Collections.Generic.List<(int, int)>>();

        /// <summary>Starts walking to a tile (or as near as can be reached); false when there is no way.</summary>
        public bool PathTo(int tx, int ty)
        {
            CollectBlockers(tx, ty);
            var path = Core.PathFinder.Find(_ctx.Player.X, _ctx.Player.Y, _ctx.Player.Z, tx, ty, StepForPath);
            if (path == null || path.Count < 2)
            {
                CancelPath();
                return false;
            }
            _path = path;
            _pathTarget = (tx, ty);
            _pathLastAt = (_ctx.Player.X, _ctx.Player.Y);
            _pathStuckSince = Time.realtimeSinceStartupAsDouble;
            return true;
        }

        /// <summary>Self-tests only (-path-selftest=x,y): a tile to walk to by pathfinding, taken once.</summary>
        public static Vector2Int? TestPathTo;
        private bool _logPath;

        public void CancelPath()
        {
            if (_logPath && _path != null)
                Debug.Log($"UOClient path: ended at {_ctx.Player.X},{_ctx.Player.Y},{_ctx.Player.Z} (target {_pathTarget.X},{_pathTarget.Y})");
            _path = null;
            _repathed = false;
        }

        /// <summary>
        /// What stands in the way besides the map: impassable items lying in the world (closed doors too, unless they
        /// open by themselves) and other characters, by tile. The target tile's own characters are left out, so a click
        /// on someone walks up to them.
        /// </summary>
        private void CollectBlockers(int tx, int ty)
        {
            _blockers.Clear();
            Core.World world = _ctx.Session?.World;
            if (world == null)
                return;
            void Add(int x, int y, int z, int height)
            {
                long key = ((long)x << 32) | (uint)y;
                if (!_blockers.TryGetValue(key, out var list))
                    _blockers[key] = list = new System.Collections.Generic.List<(int, int)>();
                list.Add((z, height));
            }
            foreach (Core.WorldItem item in world.Items.Values)
            {
                if (item.IsMulti || Core.PathFinder.Distance(_ctx.Player.X, _ctx.Player.Y, item.X, item.Y) > Core.PathFinder.MaxDistance)
                    continue;
                IO.Formats.StaticTileData tile = _ctx.Data.TileData.GetStatic(item.Graphic);
                if ((tile.Flags & IO.Formats.TileFlags.Impassable) == 0)
                    continue;
                if (DoorsOpenThemselves && !_doorsAreWalls && IsDoor != null && IsDoor(item.Graphic))
                    continue;
                Add(item.X, item.Y, item.Z, Math.Max(1, (int)tile.Height));
            }
            foreach (Core.Mobile m in world.Mobiles.Values)
                if (m.Serial != world.PlayerSerial && !m.IsDead && !(m.X == tx && m.Y == ty))
                    Add(m.X, m.Y, m.Z, 16);
        }

        private int? StepForPath(int x, int y, int fromZ)
        {
            int? z = _ctx.Walk.TryStep(x, y, fromZ);
            if (!z.HasValue)
                return null;
            if (_blockers.TryGetValue(((long)x << 32) | (uint)y, out var list))
                foreach ((int bz, int height) in list)
                    if (bz < z.Value + 16 && bz + height > z.Value)
                        return null;
            return z;
        }

        /// <summary>
        /// The next step of the path. Off the path, or standing still for two seconds (someone walked in, a door, the
        /// server refused), the way is looked for once more; still stuck after that, the walk ends.
        /// </summary>
        private void WalkPath(bool run)
        {
            int px = _ctx.Player.X, py = _ctx.Player.Y;
            double now = Time.realtimeSinceStartupAsDouble;
            if (_ctx.Session == null)
            {
                CancelPath();
                return;
            }
            if ((px, py) != _pathLastAt)
            {
                _pathLastAt = (px, py);
                _pathStuckSince = now;
                _repathed = false;
            }
            int at = _path.FindIndex(n => n.X == px && n.Y == py);
            if (at == _path.Count - 1)
            {
                CancelPath(); // there
                return;
            }
            if (at < 0 || now - _pathStuckSince > 2.0)
            {
                if (_logPath)
                    Debug.Log($"UOClient path: {(at < 0 ? "off the path" : "stuck")} at {px},{py}; looking again: {!_repathed}");
                if (_logPath && at >= 0 && at + 1 < _path.Count)
                {
                    Core.PathFinder.Node n = _path[at + 1];
                    var here = new System.Text.StringBuilder();
                    foreach (Core.WorldItem item in _ctx.Session.World.Items.Values)
                        if (item.X == n.X && item.Y == n.Y)
                            here.Append($" item 0x{item.Graphic:X4} z{item.Z} flags {_ctx.Data.TileData.GetStatic(item.Graphic).Flags}");
                    foreach (Core.Mobile m in _ctx.Session.World.Mobiles.Values)
                        if (m.X == n.X && m.Y == n.Y)
                            here.Append($" mobile {m.Name} z{m.Z}");
                    Debug.Log($"UOClient path: next {n.X},{n.Y},{n.Z} facing {_ctx.Player.Direction}; step rule {_ctx.Walk.TryStep(n.X, n.Y, _ctx.Player.Z)}; "
                              + $"may move {_ctx.Session.MayMove}, pending {_ctx.Session.PendingSteps};{here}");
                }
                // A door that did not open (locked) is a wall for the second look.
                _doorsAreWalls = true;
                bool again = !_repathed && PathTo(_pathTarget.X, _pathTarget.Y);
                _doorsAreWalls = false;
                if (!again)
                    CancelPath();
                else
                    _repathed = true;
                return;
            }
            Core.PathFinder.Node next = _path[at + 1];
            int direction = Core.PathFinder.DirectionOf(next.X - px, next.Y - py);
            if (direction < 0)
            {
                CancelPath();
                return;
            }
            // Runs when you always run, or while the way is still long (as holding the mouse far away does).
            TryStep(direction, run || _path.Count - 1 - at > 4);
        }

        /// <summary>Self-tests only: a screen-space walk direction used while no key or mouse is walking (zero: none).</summary>
        public static Vector2 TestIntent;

        /// <summary>
        /// One step (or a turn) in a direction, predicted locally and, online, sent to the server.
        /// Online, steps are paced on real time at the server's rate (ModernUO throttles anything faster),
        /// and each one starts the moment the previous one is due to end, so walking never pauses.
        /// </summary>
        public bool TryStep(int direction, bool run)
        {
            // The server's speed control (0xBF 0x26): mount speed on foot, walk only, or held still.
            if (_ctx.Session != null)
            {
                if (!_ctx.Session.MayMove)
                    return false;
                run &= _ctx.Session.MayRun;
                _ctx.Player.FastOnFoot = _ctx.Session.FastOnFoot;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            bool canStep = _ctx.Session == null ? !_ctx.Player.IsMoving : now >= _nextStepAt - 0.004 && _ctx.Session.PendingSteps < 3;
            if (!canStep)
                return false;

            if (_ctx.Session != null && direction != _ctx.Player.Direction)
            {
                // The server turns first and moves on the next request.
                if (_ctx.Session.RequestStep(direction, run))
                    _ctx.Player.Face(direction);
                return true;
            }

            Vector2Int d = Avatar.Delta(direction);
            int nx = _ctx.Player.X + d.x, ny = _ctx.Player.Y + d.y;
            int? z = _ctx.Walk.TryStep(nx, ny, _ctx.Player.Z);
            // Someone stands there: below full stamina they block you; at full you shove through (Core.Shove).
            if (!z.HasValue || _ctx.Session != null && Core.Shove.Blocker(_ctx.Session.World, nx, ny, z.Value) != null)
            {
                _ctx.Player.Face(direction);
                return false;
            }
            if (_ctx.Session != null)
            {
                if (!_ctx.Session.RequestStep(direction, run))
                    return false;
                _ctx.Session.ApplyPredictedStep(nx, ny, (sbyte)z.Value, direction);
                // Never faster than one step per server delay, measured from when the last step was due.
                _nextStepAt = Math.Max(now, _nextStepAt) + _ctx.Player.StepTime(run);
            }
            _ctx.Player.BeginStep(direction, z.Value, run);
            return true;
        }

        /// <summary>Screen vector (x right, y down) to a UO direction: right = 1 (east-north), down = 3, left = 5, up = 7.</summary>
        public static int DirectionFromScreen(Vector2 v)
        {
            float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
            int octant = Mathf.RoundToInt(angle / 45f);
            return ((octant + 1) % 8 + 8) % 8;
        }
    }
}
