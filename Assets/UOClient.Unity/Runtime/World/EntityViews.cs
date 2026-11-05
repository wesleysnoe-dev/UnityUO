using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// The views of what the server shows us: other characters, ground items, and houses and boats (one view per
    /// fixed part, the parts joining the walkable statics). Kept in step with the session's world events.
    /// </summary>
    public sealed class EntityViews : IDisposable
    {
        private readonly GameContext _ctx;
        private readonly Dictionary<uint, Avatar> _others = new Dictionary<uint, Avatar>();
        private readonly Dictionary<uint, int> _otherLook = new Dictionary<uint, int>();
        private readonly Dictionary<uint, ItemView> _items = new Dictionary<uint, ItemView>();
        private readonly Dictionary<uint, List<ItemView>> _multis = new Dictionary<uint, List<ItemView>>();
        private int _playerLook = -1;
        private GameSession _attached;

        public EntityViews(GameContext ctx)
        {
            _ctx = ctx;
        }

        /// <summary>Called with the player's name whenever the server sends it.</summary>
        public Action<string> PlayerNamed;

        public IReadOnlyDictionary<uint, Avatar> Others => _others;
        public int ItemCount => _items.Count;
        public int MultiCount => _multis.Count;
        public int MultiPartCount => _multis.Values.Sum(v => v.Count);
        public bool HasItem(uint serial) => _items.ContainsKey(serial);
        public bool HasMulti(uint serial) => _multis.ContainsKey(serial);

        /// <summary>Drawn parts of a multi, or 0 if it isn't shown.</summary>
        public int MultiParts(uint serial) => _multis.TryGetValue(serial, out List<ItemView> views) ? views.Count : 0;

        public void Attach(GameSession s)
        {
            _attached = s;
            s.World.MobileUpdated += OnMobileUpdated;
            s.World.MobileMoved += OnMobileMoved;
            s.World.EntityRemoved += OnEntityRemoved;
            s.World.ItemUpdated += OnItemUpdated;
        }

        public void Detach()
        {
            if (_attached == null)
                return;
            _attached.World.MobileUpdated -= OnMobileUpdated;
            _attached.World.MobileMoved -= OnMobileMoved;
            _attached.World.EntityRemoved -= OnEntityRemoved;
            _attached.World.ItemUpdated -= OnItemUpdated;
            _attached = null;
        }

        /// <summary>The player's avatar on entering the world (replacing any earlier one).</summary>
        public void SpawnPlayer(Core.Mobile p, string name)
        {
            _ctx.Player?.Dispose();
            _ctx.Player = new Avatar(_ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, LookOf(p), p.X, p.Y, p.Z, name);
            _ctx.Player.Face(p.Direction);
            _playerLook = p.AppearanceVersion;
        }

        /// <summary>Views for everything the session already knows about (after entering the world).</summary>
        public void SyncAll()
        {
            foreach (Core.Mobile m in _ctx.Session.World.Mobiles.Values)
                OnMobileUpdated(m);
            foreach (WorldItem i in _ctx.Session.World.Items.Values)
                OnItemUpdated(i);
        }

        public void Tick(float dt)
        {
            foreach (Avatar a in _others.Values)
                a.Tick(dt);
        }

        public void Render(bool hideUpperMultiParts)
        {
            foreach (Avatar a in _others.Values)
                a.Render(_ctx.World);
            foreach (ItemView v in _items.Values)
                v.Render(_ctx.World);
            RenderMultis(hideUpperMultiParts);
        }

        /// <summary>Tile position of a mobile we draw (the player included), or of a ground item.</summary>
        public Vector3Int? Locate(uint serial)
        {
            if (_ctx.Session != null && serial == _ctx.Session.World.PlayerSerial && _ctx.Player != null)
                return new Vector3Int(_ctx.Player.X, _ctx.Player.Y, _ctx.Player.Z);
            if (_others.TryGetValue(serial, out Avatar a))
                return new Vector3Int(a.X, a.Y, a.Z);
            if (_ctx.Session != null && _ctx.Session.World.Items.TryGetValue(serial, out WorldItem item))
                return new Vector3Int(item.X, item.Y, item.Z);
            return null;
        }

        /// <summary>The drawn rectangle (world units) overhead text for a serial goes over, or null if it's not drawn.</summary>
        public Rect? DrawnRect(uint serial)
        {
            if (_ctx.Session != null && serial == _ctx.Session.World.PlayerSerial)
                return _ctx.Player.LastRect;
            if (_others.TryGetValue(serial, out Avatar a) && a.Visible)
                return a.LastRect;
            if (_items.TryGetValue(serial, out ItemView v))
                return v.LastRect;
            return null;
        }

        /// <summary>The nearest character or ground item under a screen point (by drawn rectangle), or 0.</summary>
        public uint PickAt(Vector2 screen)
        {
            Vector3 p = _ctx.Camera.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0));
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
            if (_ctx.Player.Visible)
                Consider(_ctx.Session.World.PlayerSerial, _ctx.Player.LastRect, _ctx.Player.LastDepth);
            foreach (KeyValuePair<uint, Avatar> kv in _others)
                if (kv.Value.Visible)
                    Consider(kv.Key, kv.Value.LastRect, kv.Value.LastDepth);
            foreach (KeyValuePair<uint, ItemView> kv in _items)
                Consider(kv.Key, kv.Value.LastRect, kv.Value.LastDepth);
            return best;
        }

        private void OnMobileUpdated(Core.Mobile m)
        {
            if (_ctx.World == null)
                return;
            if (m.Serial == _ctx.Session.World.PlayerSerial)
            {
                if (_ctx.Player == null)
                    return;
                if (m.Name != null)
                    PlayerNamed?.Invoke(m.Name);
                if (m.AppearanceVersion != _playerLook)
                {
                    _playerLook = m.AppearanceVersion;
                    _ctx.Player.SetLook(LookOf(m));
                }
                // A server-side position change (teleport, resync) overrides prediction.
                if (!_ctx.Player.IsMoving && (m.X != _ctx.Player.X || m.Y != _ctx.Player.Y))
                    _ctx.Player.Teleport(m.X, m.Y, m.Z);
                return;
            }

            if (!_others.TryGetValue(m.Serial, out Avatar a))
            {
                a = new Avatar(_ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, LookOf(m), m.X, m.Y, m.Z, m.Name ?? $"Mobile 0x{m.Serial:X}");
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

        /// <summary>Art the classic clients never draw for world items (ClassicUO's no-draw list).</summary>
        private static bool IsNoDraw(ushort graphic) =>
            graphic == 0x0001 || graphic == 0x21BC || graphic == 0x9E4C || graphic == 0x9E64 || graphic == 0x9E65 || graphic == 0x9E7D;

        public void ShowMulti(WorldItem multi)
        {
            RemoveMulti(multi.Serial);
            var views = new List<ItemView>();
            foreach (MultiComponent part in CustomHouses.PartsOf(_ctx.Data, _ctx.Session?.World, multi))
            {
                if (!part.Fixed || part.Graphic <= 1) // placeholders and the invisible anchor
                    continue;
                var piece = new WorldItem
                {
                    Graphic = part.Graphic, X = multi.X + part.X, Y = multi.Y + part.Y,
                    Z = (sbyte)Mathf.Clamp(multi.Z + part.Z, -128, 127), Hue = multi.Hue
                };
                views.Add(new ItemView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, piece));
                _ctx.Walk?.AddMultiPart(multi.Serial, piece.X, piece.Y, piece.Z, piece.Graphic);
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
            _ctx.Walk?.RemoveMulti(serial);
        }

        /// <summary>Inside a house, its upper floors and roof step aside like the map's roofs do.</summary>
        private void RenderMultis(bool hideAbove)
        {
            int cut = _ctx.Player.Z + 16;
            foreach (List<ItemView> views in _multis.Values)
                foreach (ItemView v in views)
                {
                    v.Hidden = hideAbove && v.Z >= cut;
                    v.Render(_ctx.World);
                }
        }

        private void OnItemUpdated(WorldItem item)
        {
            if (_ctx.World == null)
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
            _items[item.Serial] = new ItemView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, item);
        }

        private MobileLook LookOf(Core.Mobile m)
        {
            return MobileLook.From(_ctx.Data, m.Body, m.Hue, m.Equipment.Values.Select(e => (e.Layer, (int)e.Graphic, (int)e.Hue)));
        }

        /// <summary>Each distinct body near us, how many, and how many are drawn (have art), for the tour report.</summary>
        public string BodyReport()
        {
            if (_ctx.Session == null)
                return "offline";
            var parts = new List<string>();
            foreach (var g in _others.GroupBy(kv => _ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) ? m.Body : 0).OrderBy(g => g.Key))
            {
                int drawn = g.Count(kv => kv.Value.Visible);
                var mounted = g.Count(kv => _ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m) && m.Equipment.ContainsKey(25));
                parts.Add($"0x{g.Key:X} x{g.Count()} ({drawn} drawn{(mounted > 0 ? $", {mounted} mounted" : "")})");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Disposes the character and item views, as WorldDemo.OnDestroy did (multi parts are left to the scene unload).</summary>
        public void Dispose()
        {
            foreach (Avatar a in _others.Values)
                a.Dispose();
            foreach (ItemView v in _items.Values)
                v.Dispose();
        }
    }
}
