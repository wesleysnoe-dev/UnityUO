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

        private bool _rightPressOnPlate;

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
            // (Nor over the quest arrow, where a right click cancels it, nor while a press that began on a nameplate is
            // held: that right-click closes the plate. A walk begun on the world carries on over plates, as TazUO's.)
            if (!Input.GetMouseButton(1))
                _rightPressOnPlate = false;
            else if (Input.GetMouseButtonDown(1))
                _rightPressOnPlate = _ctx.Hud.NamePlateAt(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)) != 0;
            if (intent == Vector2.zero && Input.GetMouseButton(1) && !_rightPressOnPlate && (ui == null || !ui.IsOverUi(Input.mousePosition))
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
                return;

            TryStep(DirectionFromScreen(intent), run);
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
            if (!z.HasValue)
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
