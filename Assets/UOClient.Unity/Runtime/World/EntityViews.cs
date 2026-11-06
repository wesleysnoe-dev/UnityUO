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

        // Mobiles playing their fall (0xAF), no longer tied to their serial: TazUO re-keys the dying mobile so a ghost
        // update for the same serial gets a fresh one (DisplayDeath.cs:26-38). Its corpse stays hidden while the session's
        // CorpseFalls lists it (ItemView.cs:183), and is turned to the owner's facing when the fall is dropped.
        private readonly List<(Avatar Avatar, uint Serial)> _dying = new List<(Avatar, uint)>();
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
            s.World.ContainerChanged += OnContainerChanged;
            s.MobileDying += OnMobileDying;
        }

        public void Detach()
        {
            if (_attached == null)
                return;
            _attached.World.MobileUpdated -= OnMobileUpdated;
            _attached.World.MobileMoved -= OnMobileMoved;
            _attached.World.EntityRemoved -= OnEntityRemoved;
            _attached.World.ItemUpdated -= OnItemUpdated;
            _attached.World.ContainerChanged -= OnContainerChanged;
            _attached.MobileDying -= OnMobileDying;
            _attached = null;
        }

        /// <summary>Characters playing their fall right now.</summary>
        public int DyingCount => _dying.Count;

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
            // The player's war mode is 0x72's (GameSession.WarMode); being dead is read off the ghost body.
            if (_ctx.Session != null && _ctx.Player != null)
                _ctx.Player.SetCombat(_ctx.Session.WarMode, _ctx.Session.PlayerDead);
            foreach (Avatar a in _others.Values)
                a.Tick(dt);
            foreach (var d in _dying)
                d.Avatar.Tick(dt);
        }

        public void Render(bool hideUpperMultiParts)
        {
            if (_ctx.Player != null && _ctx.Session != null)
                _ctx.Player.Selected = Selected != 0 && Selected == _ctx.Session.World.PlayerSerial;
            foreach (KeyValuePair<uint, Avatar> kv in _others)
            {
                kv.Value.Selected = Selected != 0 && kv.Key == Selected;
                kv.Value.Render(_ctx.World);
            }
            foreach (var d in _dying)
            {
                d.Avatar.Selected = false; // no longer pickable: a fall never stays lit from before it began
                d.Avatar.Render(_ctx.World);
            }
            // Before the items: a fall that just reached its end hands over to its corpse in this same frame (the
            // avatar's destroy is carried out before rendering), so the last frame is never missing or doubled.
            DropFinishedFalls();
            RankTies();
            foreach (KeyValuePair<uint, ItemView> kv in _items)
            {
                kv.Value.Hidden = IsFallingCorpse(kv.Key);
                kv.Value.Selected = Selected != 0 && kv.Key == Selected;
                kv.Value.Render(_ctx.World);
            }
            RenderMultis(hideUpperMultiParts);
        }

        // RankTies' buffers, reused every frame.
        private readonly List<ItemView> _tieViews = new List<ItemView>();
        private readonly List<(long Key, long Placed)> _tieKeys = new List<(long, long)>();
        private readonly List<int> _tieOrder = new List<int>();
        private int[] _tieRanks = Array.Empty<int>();

        /// <summary>
        /// Ground items sharing a depth key (a pile on one tile, things along one diagonal at one height) take its tie
        /// levels by when they were placed, the newest in front, so what is drawn on top is what the pick takes
        /// (<see cref="MousePick.TieRanks"/>); otherwise the depth test and the pick each settle the tie their own way.
        /// </summary>
        private void RankTies()
        {
            _tieViews.Clear();
            _tieKeys.Clear();
            foreach (ItemView v in _items.Values)
            {
                _tieViews.Add(v);
                _tieKeys.Add((v.TieKey, v.Placed));
            }
            if (_tieRanks.Length < _tieViews.Count)
                _tieRanks = new int[Math.Max(_tieViews.Count, _tieRanks.Length * 2)];
            MousePick.TieRanks(_tieKeys, _tieOrder, _tieRanks, DepthKey.TieLevels);
            for (int i = 0; i < _tieViews.Count; i++)
                _tieViews[i].Tie = _tieRanks[i];
        }

        private bool IsFallingCorpse(uint serial) =>
            _ctx.Session != null && _ctx.Session.World.CorpseFalls.Count > 0 && _ctx.Session.World.CorpseFalls.Exists(serial, 0);

        private void OnMobileDying(uint serial, uint corpse, bool second)
        {
            if (!_others.TryGetValue(serial, out Avatar a))
            {
                // No fall to watch: the corpse must not stay hidden waiting for one.
                _ctx.Session?.World.CorpseFalls.Finish(serial);
                return;
            }
            _others.Remove(serial);
            _otherLook.Remove(serial);
            a.BeginDeath(second);
            _dying.Add((a, serial));
        }

        /// <summary>A fall that has played through is dropped (TazUO removes the dying mobile then, Mobile.cs:706-714).</summary>
        private void DropFinishedFalls()
        {
            for (int i = _dying.Count - 1; i >= 0; i--)
            {
                var d = _dying[i];
                if (!d.Avatar.DeathFinished)
                    continue;
                d.Avatar.Dispose();
                _dying.RemoveAt(i);
                // CorpseManager.Remove: the corpse shows from now on, lying the way the owner faced.
                _ctx.Session?.World.CorpseFalls.Finish(d.Serial);
                // A player who died stays in view as a ghost; if no update has drawn it yet, draw it now.
                if (_ctx.Session != null && !_others.ContainsKey(d.Serial) && _ctx.Session.World.Mobiles.TryGetValue(d.Serial, out Core.Mobile m))
                    OnMobileUpdated(m);
            }
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

        // PickAt's candidates, reused every call: serial, the sprite as drawn, and its atlas slot for the alpha.
        private readonly List<uint> _pickSerials = new List<uint>();
        private readonly List<MousePick.Sprite> _pickSprites = new List<MousePick.Sprite>();
        private readonly List<AtlasSlot> _pickSlots = new List<AtlasSlot>();
        private Func<int, int, int, byte> _pickAlpha;

        /// <summary>
        /// The character, corpse or ground item drawn in front under a screen point (Unity's bottom-left origin), on a
        /// pixel the world shader draws (TazUO picks by the art's pixels, so a sprite's transparent box never takes the
        /// click), or 0. Only what <see cref="ObjectHighlight.Hovers"/> takes: house and boat parts, map statics and land
        /// are never offered. The one world pick: hover, click, drag start and drop all ask here (<see cref="MousePick"/>).
        /// </summary>
        public uint PickAt(Vector2 screen)
        {
            _pickSerials.Clear();
            _pickSprites.Clear();
            _pickSlots.Clear();
            if (_ctx.Player.Visible)
                AddPick(_ctx.Session.World.PlayerSerial, _ctx.Player.PickSprite, _ctx.Player.LastSlot);
            foreach (KeyValuePair<uint, Avatar> kv in _others)
                if (kv.Value.Visible && ObjectHighlight.Hovers(ObjectHighlight.PickKind.Mobile))
                    AddPick(kv.Key, kv.Value.PickSprite, kv.Value.LastSlot);
            foreach (KeyValuePair<uint, ItemView> kv in _items)
                if (kv.Value.Drawn && ObjectHighlight.Hovers(kv.Value.PickKind))
                    AddPick(kv.Key, kv.Value.PickSprite, kv.Value.LastSlot);

            Camera cam = _ctx.Camera;
            Vector3 c = cam.transform.position;
            Rect view = cam.pixelRect;
            _pickAlpha ??= (k, x, y) => _ctx.MobileArt.Alpha(_pickSlots[k], x, y);
            int i = MousePick.World(_pickSprites, screen.x, screen.y, c.x, c.y, cam.orthographicSize,
                view.x, view.y, view.width, view.height, Shader.GetGlobalFloat("_UOCutZ"), _pickAlpha);
            return i < 0 ? 0 : _pickSerials[i];
        }

        private void AddPick(uint serial, MousePick.Sprite sprite, AtlasSlot slot)
        {
            _pickSerials.Add(serial);
            _pickSprites.Add(sprite);
            _pickSlots.Add(slot);
        }

        /// <summary>
        /// A screen point (bottom-left origin) on the world plane, at its pixel's centre: the point <see cref="PickAt"/>
        /// tests, so the ground tile a click or a drop falls on is found the same way.
        /// </summary>
        public Vector2 ScreenToWorld(Vector2 screen)
        {
            Camera cam = _ctx.Camera;
            Vector3 c = cam.transform.position;
            Rect view = cam.pixelRect;
            (float cx, float cy) = MousePick.PixelCenter(screen.x, screen.y);
            (float x, float y) = MousePick.ScreenToWorld(cx, cy, c.x, c.y, cam.orthographicSize, view.x, view.y, view.width, view.height);
            return new Vector2(x, y);
        }

        /// <summary>
        /// What the mouse is on in the world (0 for nothing, or while a window covers the world), as TazUO's
        /// SelectedObject.Object: outlined, in its own hues, where <see cref="ObjectHighlight"/> says so.
        /// </summary>
        public uint Selected { get; set; }

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
                a.SetCombat(m.WarMode, m.IsDead);
                _others[m.Serial] = a;
                _otherLook[m.Serial] = m.AppearanceVersion;
                return;
            }
            if (_otherLook[m.Serial] != m.AppearanceVersion)
            {
                _otherLook[m.Serial] = m.AppearanceVersion;
                a.SetLook(LookOf(m));
            }
            a.SetCombat(m.WarMode, m.IsDead);
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
                a.SetCombat(m.WarMode, m.IsDead);
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
            {
                // A corpse lying where it was (0x89 arrived, the same corpse re-sent): new look, same view, no flicker.
                if (item.IsCorpse && v.IsCorpseOf(item))
                {
                    RefreshCorpse(v, item);
                    return;
                }
                v.Dispose();
            }
            MobileLook corpseLook = item.IsCorpse ? CorpseDraw.Look(_ctx.Data, _ctx.Session.World, item) : null;
            _items[item.Serial] = new ItemView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, item, corpseLook);
        }

        private void RefreshCorpse(ItemView v, WorldItem corpse) =>
            v.SetCorpseLook(CorpseDraw.Look(_ctx.Data, _ctx.Session.World, corpse), CorpseDraw.Action(_ctx.Data.AnimationLibrary, corpse));

        /// <summary>A corpse's contents changed (0x3C on sight, a piece looted): what it wears is redrawn.</summary>
        private void OnContainerChanged(uint container)
        {
            if (_ctx.World == null || !_items.TryGetValue(container, out ItemView v))
                return;
            if (_ctx.Session.World.Items.TryGetValue(container, out WorldItem item) && v.IsCorpseOf(item))
                RefreshCorpse(v, item);
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
            foreach (var d in _dying)
                d.Avatar.Dispose();
            _dying.Clear();
            foreach (ItemView v in _items.Values)
                v.Dispose();
        }
    }
}
