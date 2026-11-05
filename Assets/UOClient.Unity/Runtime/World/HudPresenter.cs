using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Core;

namespace UOClient.Unity
{
    /// <summary>
    /// Fills the HUD model each frame: the player's position and stats, the connection text, the name labels
    /// over everyone in view, the health bars under their feet, where the quest arrow points, and the buff bar.
    /// </summary>
    public sealed class HudPresenter
    {
        private readonly GameContext _ctx;
        private readonly EntityViews _entities;

        public HudPresenter(GameContext ctx, EntityViews entities)
        {
            _ctx = ctx;
            _entities = entities;
        }

        public void Update()
        {
            _ctx.Hud.UpdatePlayer(_ctx.Player.X, _ctx.Player.Y, _ctx.Player.IsMoving, Time.deltaTime);
            _ctx.Hud.Labels.Clear();
            _ctx.Hud.QuestArrow = null;
            _ctx.Hud.Buffs.Clear();
            if (_ctx.Session == null)
                return;

            // Buff bar: the player's buffs with the time left as m:ss (counted down here; the server removes them).
            foreach (Buff b in _ctx.Session.Buffs)
            {
                if (b.Serial != _ctx.Session.World.PlayerSerial)
                    continue;
                double left = _ctx.Session.BuffSecondsLeft(b);
                string time = left < 0 ? "" : $"{(int)left / 60}:{(int)left % 60:00}";
                _ctx.Hud.Buffs.Add((b.Title, time));
            }

            // Quest arrow: the target tile's center at the player's height (the packet has no z), as Avatar anchors it.
            Core.QuestArrow arrow = _ctx.Session.QuestArrow;
            if (arrow != null)
            {
                var tile = new Vector2((arrow.X - arrow.Y) * 22, (arrow.X + arrow.Y) * 22 + 22 - _ctx.Player.Z * 4) * _ctx.ArtScale;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(tile));
                _ctx.Hud.QuestArrow = new Vector2(s.x, Screen.height - s.y);
                _ctx.Hud.QuestArrowTile = new Vector2Int(arrow.X, arrow.Y);
            }

            // Connection text only matters while not playing (connecting, or after a disconnect).
            _ctx.Hud.ConnectionStatus = _ctx.Session.State == SessionState.InWorld ? "" : _ctx.Session.Status;

            Core.Mobile p = _ctx.Session.World.Player;
            if (p != null && p.HitsMax > 0)
            {
                _ctx.Hud.Hits = p.Hits;
                _ctx.Hud.HitsMax = p.HitsMax;
                _ctx.Hud.Mana = p.Mana;
                _ctx.Hud.ManaMax = Math.Max(p.ManaMax, 1);
                _ctx.Hud.Stamina = p.Stamina;
                _ctx.Hud.StaminaMax = Math.Max(p.StaminaMax, 1);
            }

            foreach (KeyValuePair<uint, Avatar> kv in _entities.Others)
            {
                if (!_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) || string.IsNullOrEmpty(m.Name))
                    continue;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(kv.Value.AnchorPixels - new Vector2(0, 75 * _ctx.ArtScale)));
                if (s.x < -50 || s.x > Screen.width + 50 || s.y < -50 || s.y > Screen.height + 50)
                    continue;
                _ctx.Hud.Labels.Add((m.Name, new Vector2(s.x, Screen.height - s.y), DemoHud.NotorietyColor(m.Notoriety)));
            }

            // Health bars under the feet: whoever we fight, anyone hurt, and ourselves in war mode or when hurt.
            _ctx.Hud.HealthBars.Clear();
            void Bar(Core.Mobile m, Avatar a)
            {
                if (m == null || a == null || !a.Visible || m.HitsMax <= 0)
                    return;
                bool show = m.Serial == _ctx.Session.Combatant || m.Hits < m.HitsMax
                            || (m.Serial == _ctx.Session.World.PlayerSerial && _ctx.Session.WarMode);
                if (!show)
                    return;
                Vector3 s = _ctx.Camera.WorldToScreenPoint(_ctx.World.ToUnity(a.AnchorPixels + new Vector2(0, 10 * _ctx.ArtScale)));
                _ctx.Hud.HealthBars.Add((new Vector2(s.x, Screen.height - s.y), m.Hits / (float)m.HitsMax, DemoHud.NotorietyColor(m.Notoriety)));
            }
            Bar(p, _ctx.Player);
            foreach (KeyValuePair<uint, Avatar> kv in _entities.Others)
                if (_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m))
                    Bar(m, kv.Value);
        }
    }
}
