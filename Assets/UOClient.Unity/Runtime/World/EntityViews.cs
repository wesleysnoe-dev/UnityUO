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

        // Each drawn multi's half-extent in tiles (TazUO's MultiDistanceBonus), which World.PruneOutOfRange adds to its range.
        private readonly Dictionary<uint, int> _multiExtent = new Dictionary<uint, int>();
        private Func<WorldItem, int> _extentOf;

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
            _extentOf ??= m => _multiExtent.TryGetValue(m.Serial, out int e) ? e : 0;
            s.World.MultiExtent = _extentOf;
            s.World.MobileUpdated += OnMobileUpdated;
            s.World.MobileMoved += OnMobileMoved;
            s.World.MobileCarried += OnMobileCarried;
            s.World.EntityRemoved += OnEntityRemoved;
            s.World.ItemUpdated += OnItemUpdated;
            s.World.ContainerChanged += OnContainerChanged;
            s.MobileDying += OnMobileDying;
            s.MobileAnimated += OnMobileAnimated;
        }

        public void Detach()
        {
            if (_attached == null)
                return;
            if (_attached.World.MultiExtent == _extentOf)
                _attached.World.MultiExtent = null;
            _attached.World.MobileUpdated -= OnMobileUpdated;
            _attached.World.MobileMoved -= OnMobileMoved;
            _attached.World.MobileCarried -= OnMobileCarried;
            _attached.World.EntityRemoved -= OnEntityRemoved;
            _attached.World.ItemUpdated -= OnItemUpdated;
            _attached.World.ContainerChanged -= OnContainerChanged;
            _attached.MobileDying -= OnMobileDying;
            _attached.MobileAnimated -= OnMobileAnimated;
            _attached = null;
            Lights.Clear();
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
            UpdateLights();
        }

        /// <summary>Item, static and multi-part lights near the player (bounded, see <see cref="ItemLights"/>).</summary>
        public ItemLights Lights { get; } = new ItemLights();
        private System.Action<int, int, int, int> _addShadowLight;

        private readonly Fx.FlameFx _flames = new Fx.FlameFx();

        /// <summary>The living flames near the player (their glow halos are drawn by AmbientLife).</summary>
        public Fx.FlameFx Flames => _flames;
        private static readonly bool FxTestOn = Fx.FxCatalog.HasArg("-fx-test");
        private float _fxTestNext;

        /// <summary>
        /// -fx-test (local only, nothing sent): a campfire flame two tiles south-east of the player and a fire field to the west, and every 3 s a fireball
        /// thrown from the player to a spot 6 tiles east that explodes, so the FX can be seen without casting.
        /// </summary>
        private void FxTest()
        {
            if (!FxTestOn)
                return;
            int x = _ctx.Player.X, y = _ctx.Player.Y, z = _ctx.Player.Z;
            _flames.Add(x + 2, y + 2, z, 0x0DE3);
            _flames.Add(x - 3, y + 1, z, 0x398C); // a fire field
            if (Time.time < _fxTestNext || EffectsLayer.Instance == null)
                return;
            _fxTestNext = Time.time + 3f;
            EffectsLayer.Instance.Start(new UOClient.Core.GraphicEffect
            {
                Type = UOClient.Core.GraphicEffect.Moving, Graphic = 0x36D4, Explode = true,
                SourceX = x, SourceY = y, SourceZ = z, TargetX = x + 6, TargetY = y - 2, TargetZ = z
            });
        }
        private System.Action<int, int, int, int> _addFlame;

        private void UpdateLights()
        {
            if (_ctx.World == null || _ctx.Player == null || _ctx.Data == null)
            {
                Lights.Clear();
                _flames.Clear();
                return;
            }
            Lights.Begin();
            foreach (ItemView v in _items.Values)
                if (v.GivesLight)
                    Lights.Add(v.TileX, v.TileY, v.TileZ, v.Graphic, v.LightId);
            foreach (List<ItemView> parts in _multis.Values)
                foreach (ItemView v in parts)
                    if (v.GivesLight)
                        Lights.Add(v.TileX, v.TileY, v.TileZ, v.Graphic, v.LightId);
            _ctx.World.AddStaticLights(Lights);
            GatherCarriedLights();
            Fx.FxCatalog.Data = _ctx.Data;
            Lights.Walk = _ctx.Walk; // walls stop the lights
            Lights.Apply(_ctx.World, _ctx.Data.LightShapes, _ctx.ArtScale, _ctx.Player.X, _ctx.Player.Y, Lighting.LocalLightScale);
            // Living flames on the fire art among the light sources (Fx.FlameFx), by day too.
            _flames.Begin();
            Lights.ForEachSource(_addFlame ??= (x, y, z, g) => _flames.Add(x, y, z, g));
            // Indoor shadows (CharacterShadow): this frame's light sources, and the indoor check.
            // Only lights that are themselves indoors: a street lamp outside does not shine through the wall.
            CharacterShadow.Indoors ??= (x, y, z) => _ctx.Walk != null && _ctx.Walk.IsUnderRoof(x, y, z);
            CharacterShadow.WallBetween ??= (lx, ly, x, y, z) => _ctx.Walk != null && _ctx.Walk.WallBetween(lx, ly, x, y, z);
            CharacterShadow.LightSources.Clear();
            Lights.ForEachSource(_addShadowLight ??= (x, y, z, g) =>
            {
                // A flame casts shadows; a window (UO flags them light sources: light coming in) does not.
                if ((_ctx.Data.TileData.GetStatic(g).Flags & TileFlags.Window) == 0 && CharacterShadow.Indoors(x, y, z))
                    CharacterShadow.LightSources.Add(new Vector3Int(x, y, z));
            });
            FxTest();
            _flames.Apply(_ctx.Data, _ctx.World, _ctx.ArtScale, _ctx.Player.X, _ctx.Player.Y);
            _carriedFlames.Apply(_ctx.Data, _ctx.World, _ctx.ArtScale);
            Fx.HitSparks.Draw(_sparkAt ??= serial => serial == _ctx.Session?.World.PlayerSerial ? _ctx.Player
                : _others.TryGetValue(serial, out Avatar a) ? a : null, _ctx.ArtScale);
        }

        /// <summary>
        /// The lights the player and the characters near them carry (ItemLights.Carried): each worn item with the tiledata
        /// light flag, at its bearer's drawn feet.
        /// </summary>
        private readonly Fx.CarriedFlames _carriedFlames = new Fx.CarriedFlames();
        private System.Func<uint, Avatar> _sparkAt;

        private void GatherCarriedLights()
        {
            ItemLights.Carried.Clear();
            _carriedFlames.Begin();
            if (_ctx.Session == null)
                return;
            void From(Avatar a, Core.Mobile m)
            {
                if (a == null || m == null || Math.Max(Math.Abs(a.X - _ctx.Player.X), Math.Abs(a.Y - _ctx.Player.Y)) > ItemLights.RangeTiles)
                    return;
                foreach (Core.EquippedItem e in m.Equipment.Values)
                {
                    StaticTileData td = _ctx.Data.TileData.GetStatic(e.Graphic);
                    if (!LightSources.IsLight(td.Flags))
                        continue;
                    ItemLights.Carried.Add((a.AnchorPixels, e.Graphic, td.LightIndex));
                    if (td.AnimId != 0)
                        _carriedFlames.Add(m.Serial, a, e.Graphic, td.AnimId); // its living flame, on the frame's fire
                }
            }
            if (_ctx.Session.World.Mobiles.TryGetValue(_ctx.Session.World.PlayerSerial, out Core.Mobile me))
                From(_ctx.Player, me);
            foreach (var kv in _others)
                if (_ctx.Session.World.Mobiles.TryGetValue(kv.Key, out Core.Mobile m))
                    From(kv.Value, m);
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

        /// <summary>
        /// The server played an action on a mobile we draw (the player or another; a dying one keeps its fall): the action
        /// the body draws for it (<see cref="AnimationLibrary.ServerAction"/> / <see cref="AnimationLibrary.NewServerAction"/>)
        /// plays on the avatar, then it goes back to walking, running or standing.
        /// </summary>
        private void OnMobileAnimated(ServerAnimation anim)
        {
            Avatar a = _ctx.Session != null && anim.Serial == _ctx.Session.World.PlayerSerial ? _ctx.Player
                : _others.TryGetValue(anim.Serial, out Avatar other) ? other : null;
            if (a == null || a.Look == null || _ctx.Data == null)
                return;
            MobileLook look = a.Look;
            AnimationLibrary anims = _ctx.Data.AnimationLibrary;
            int coin = UnityEngine.Random.Range(0, 2);
            if (anim.IsNew)
            {
                int action = anims.NewServerAction(look.Body, anim.Type, anim.Action, anim.Mode, look.Mounted, a.WarMode, coin);
                a.PlayServerAnimation(action, 0, 0, anim.RepeatCount, anim.Repeat, true);
            }
            else
            {
                int action = anims.ServerAction(look.Body, anim.Action, look.Mounted, a.WarMode, coin);
                a.PlayServerAnimation(action, anim.Delay, anim.FrameCount, anim.RepeatCount, anim.Repeat, anim.Forward);
            }
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
            if (serial >= StaticLabelBase && _staticLabels.TryGetValue(serial, out StaticLabel label))
                return StaticRect(label.Item, label.Width, label.Height);
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
                {
                    // A stack's copy behind it first, so at one depth the item itself is the later, picked one.
                    if (kv.Value.BackPickSprite is MousePick.Sprite back)
                        AddPick(kv.Key, back, kv.Value.LastSlot);
                    AddPick(kv.Key, kv.Value.PickSprite, kv.Value.LastSlot);
                }

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

        // ---- map statics under the mouse (a click names them, as the classic client and ClassicUO do) ----

        /// <summary>A clicked static's name, to show over it (label serial, text): WorldDemo puts it in the overhead text.</summary>
        public Action<uint, string> StaticNamed { get; set; }

        /// <summary>Overhead labels on statics use serials from here up (never a real item or mobile serial).</summary>
        public const uint StaticLabelBase = 0xC0000000;

        private struct StaticLabel
        {
            public UOClient.ArtPipeline.DrawItem Item;
            public int Width, Height;
        }

        private readonly Dictionary<uint, StaticLabel> _staticLabels = new Dictionary<uint, StaticLabel>();
        private readonly Dictionary<long, uint> _staticLabelKeys = new Dictionary<long, uint>();
        private readonly Dictionary<int, Bitmap32> _staticArt = new Dictionary<int, Bitmap32>();
        private uint _nextStaticLabel = StaticLabelBase;

        /// <summary>A static picked by <see cref="PickStatic"/>: where it is, its graphic, and the label serial its name goes over.</summary>
        public struct PickedStatic
        {
            public int X, Y, Z, Graphic;
            public uint LabelSerial;
        }

        private Bitmap32 StaticArt(int graphic)
        {
            if (!_staticArt.TryGetValue(graphic, out Bitmap32 art))
                _staticArt[graphic] = art = _ctx.Data.GetStatic(graphic);
            return art;
        }

        private Rect StaticRect(UOClient.ArtPipeline.DrawItem it, int width, int height)
        {
            var (left, top, w, h) = UOClient.ArtPipeline.IsoCompositor.PlacementRect(it, _ctx.ArtScale, width, height);
            return _ctx.World.ToUnityRect(left, top, w, h);
        }

        private static UOClient.ArtPipeline.DrawItem StaticItem(int x, int y, int z, int graphic, StaticTileData td) =>
            new UOClient.ArtPipeline.DrawItem
            {
                Kind = UOClient.ArtPipeline.DrawKind.Static, Graphic = (ushort)graphic, X = x, Y = y, Z = (sbyte)z,
                PriorityZ = z + (td.IsBackground ? -1 : 0) + (td.Height > 0 ? 1 : 0) // as ItemView orders them
            };

        /// <summary>
        /// The map static or house / boat part drawn in front under a screen point (bottom-left origin), on a pixel of its
        /// art (not its transparent box, not cut away by the roof / upper-floor cut), or null. Statics stand on tiles below
        /// the point on screen, so the tiles from a little above the mouse's tile to well below it are searched.
        /// </summary>
        public PickedStatic? PickStatic(Vector2 screen)
        {
            if (_ctx.World == null || _ctx.Data == null || _ctx.Walk == null || _ctx.Player == null || _ctx.Camera == null)
                return null;
            Vector2 wp = ScreenToWorld(screen);
            Vector2Int t = _ctx.World.TileAt(_ctx.World.FromUnity(wp), _ctx.Player.Z);
            float cutZ = Shader.GetGlobalFloat("_UOCutZ");
            PickedStatic? best = null;
            float bestDepth = float.MaxValue;
            UOClient.ArtPipeline.DrawItem bestItem = default;
            Bitmap32 bestArt = null;
            for (int dy = -3; dy <= 14; dy++)
                for (int dx = -3; dx <= 14; dx++)
                {
                    int x = t.x + dx, y = t.y + dy;
                    if (x < 0 || y < 0)
                        continue;
                    foreach (StaticCell cell in _ctx.Walk.StaticsOn(x, y))
                    {
                        Bitmap32 art = StaticArt(cell.Graphic);
                        if (art == null || art.Width <= 0 || art.Height <= 0)
                            continue;
                        UOClient.ArtPipeline.DrawItem it = StaticItem(x, y, cell.Z, cell.Graphic, _ctx.Data.TileData.GetStatic(cell.Graphic));
                        float depth = _ctx.World.DepthFor(it);
                        if (depth > bestDepth)
                            continue;
                        Rect r = StaticRect(it, art.Width, art.Height);
                        var sprite = new MousePick.Sprite
                        {
                            Left = r.xMin, Bottom = r.yMin, Width = r.width, Height = r.height,
                            SpriteWidth = art.Width, SpriteHeight = art.Height, Depth = depth, CutHeight = MeshBuilder.CutHeight(cell.Z)
                        };
                        Bitmap32 a = art;
                        if (!MousePick.Hits(sprite, wp.x, wp.y, cutZ, (px, py) => (byte)(a.Pixels[py * a.Width + px] >> 24)))
                            continue;
                        bestDepth = depth;
                        bestArt = art;
                        bestItem = it;
                        best = new PickedStatic { X = x, Y = y, Z = cell.Z, Graphic = cell.Graphic };
                    }
                }
            if (best is not PickedStatic p)
                return null;

            // One label serial per static, so clicking it again stacks its lines as speech does.
            long key = ((long)(p.X & 0xFFFF) << 40) | ((long)(p.Y & 0xFFFF) << 24) | ((long)(p.Z & 0xFF) << 16) | (long)(p.Graphic & 0xFFFF);
            if (!_staticLabelKeys.TryGetValue(key, out uint serial))
            {
                if (_staticLabelKeys.Count >= 256 || _nextStaticLabel == 0) // old labels are long gone by now
                {
                    _staticLabelKeys.Clear();
                    _staticLabels.Clear();
                    _nextStaticLabel = StaticLabelBase;
                }
                serial = _nextStaticLabel++;
                _staticLabelKeys[key] = serial;
            }
            _staticLabels[serial] = new StaticLabel { Item = bestItem, Width = bestArt.Width, Height = bestArt.Height };
            p.LabelSerial = serial;
            return p;
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

        /// <summary>A boat moved a character (0xF6): placed at the new tile without a walking step, the player too.</summary>
        private void OnMobileCarried(Core.Mobile m)
        {
            if (_ctx.World == null)
                return;
            if (m.Serial == _ctx.Session.World.PlayerSerial)
            {
                _ctx.Player?.Teleport(m.X, m.Y, m.Z);
                return;
            }
            if (_others.TryGetValue(m.Serial, out Avatar a))
                a.Teleport(m.X, m.Y, m.Z);
            else
                OnMobileUpdated(m);
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
            int extent = 0;
            int px0 = int.MaxValue, py0 = int.MaxValue, px1 = int.MinValue, py1 = int.MinValue;
            foreach (MultiComponent part in CustomHouses.PartsOf(_ctx.Data, _ctx.Session?.World, multi))
            {
                extent = Math.Max(extent, Math.Max(Math.Abs((int)part.X), Math.Abs((int)part.Y)));
                if (!part.Fixed || part.Graphic <= 1) // placeholders and the invisible anchor
                    continue;
                var piece = new WorldItem
                {
                    Graphic = part.Graphic, X = multi.X + part.X, Y = multi.Y + part.Y,
                    Z = (sbyte)Mathf.Clamp(multi.Z + part.Z, -128, 127), Hue = multi.Hue
                };
                views.Add(new ItemView(_ctx.Data, _ctx.MobileArt, _ctx.Root, _ctx.Shader, _ctx.ArtScale, piece, multiPart: true));
                _ctx.Walk?.AddMultiPart(multi.Serial, piece.X, piece.Y, piece.Z, piece.Graphic);
                px0 = Math.Min(px0, piece.X); py0 = Math.Min(py0, piece.Y); px1 = Math.Max(px1, piece.X); py1 = Math.Max(py1, piece.Y);
            }
            if (px0 <= px1)
                _ctx.Walk?.NotifyParts(px0, py0, px1, py1);
            _multis[multi.Serial] = views;
            _multiExtent[multi.Serial] = extent;
        }

        private void RemoveMulti(uint serial)
        {
            _multiExtent.Remove(serial);
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
            MobileLook look = MobileLook.From(_ctx.Data, m.Body, m.Hue, m.Equipment.Values.Select(e => (e.Layer, (int)e.Graphic, (int)e.Hue)), m.BondedDead);
            return m.IsHidden ? look.AsHidden() : look; // staff see hidden characters, drawn grey (TazUO HiddenBodyHue)
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

        /// <summary>
        /// The session ended: stops listening to it and frees every view (multi parts too), ready for the next session's
        /// <see cref="Attach"/>. Calling it again does nothing more.
        /// </summary>
        public void Reset()
        {
            Detach();
            Dispose();
            _others.Clear();
            _otherLook.Clear();
            _items.Clear();
            _tieViews.Clear();
            foreach (List<ItemView> parts in _multis.Values)
                foreach (ItemView v in parts)
                    v.Dispose();
            _multis.Clear();
            _multiExtent.Clear();
            _playerLook = -1;
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
