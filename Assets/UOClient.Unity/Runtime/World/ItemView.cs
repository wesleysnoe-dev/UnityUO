using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.ArtPipeline;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// A server item on the ground (doors, signs, dropped things), drawn with its original art. The
    /// sprite is shared per (graphic, hue) and built in the background; the item appears once it is ready.
    /// A corpse is drawn as its body lying in its last death frame instead (<see cref="CorpseDraw"/>).
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

        // Corpses only: the item (its Layer turns when the owner's fall ends), its look and death action, and the
        // frame last drawn, kept while a changed look (a piece looted) is being built.
        private readonly WorldItem _corpse;
        private MobileLook _corpseLook;
        private string _corpseKey;
        private int _corpseAction;
        private MobileFrame _lastCorpseFrame;
        private bool _lastCorpseMirror;

        public ItemView(UOData data, MobileArt art, Transform parent, Shader shader, int scale, WorldItem item, MobileLook corpseLook = null,
                        bool multiPart = false)
        {
            _art = art;
            _scale = scale;
            _graphic = item.Graphic;
            // A hidden item (sent to staff only) is drawn grey, as TazUO's ItemView: staff props, not furniture.
            _hue = item.IsHidden && !item.IsMulti ? MobileLook.HiddenHue : item.Hue;
            if (corpseLook != null && item.IsCorpse)
            {
                _corpse = item;
                SetCorpseLook(corpseLook, CorpseDraw.Action(data.AnimationLibrary, item));
            }
            StaticTileData td = data.TileData.GetStatic(item.Graphic);
            // A house's window glows at night like the map's (PlantKind.Window in the quad's sprite code).
            _spriteCode = multiPart && (td.Flags & TileFlags.Window) != 0 ? PlantKind.Window : 0f;
            // A coin pile's art and a stack's second copy (ItemStack); the view is remade when the amount changes.
            _shown = item.IsMulti || item.IsCorpse ? item.Graphic : ItemStack.DisplayedGraphic(item.Graphic, item.Amount);
            _twice = !item.IsMulti && !item.IsCorpse && ItemStack.DrawsTwice(item.Graphic, item.Amount, td.Flags);
            // A light (LightSources): a ground item lights by the server's light byte (0xF3's direction byte, ModernUO's
            // Item.Light), a multi part, as a static, by its tiledata Layer.
            if (!item.IsCorpse && !item.IsMulti && (multiPart ? LightSources.IsLight(td.Flags) : LightSources.ItemGivesLight(item.Graphic, td.Flags)))
            {
                GivesLight = true;
                LightId = multiPart ? td.Layer : item.Direction;
            }
            // Animated art (ArtAnimation, TazUO's ItemData.IsAnimated + AnimOffset): every frame's sprite is asked for now,
            // so a frame change only swaps to art already built.
            if (!item.IsCorpse && !item.IsMulti)
            {
                AnimDataEntry entry = data.AnimData.Get(_shown);
                if (ArtAnimation.Animates(td.Flags, entry))
                {
                    _animation = entry;
                    _animates = true;
                    foreach (sbyte offset in entry.Offsets)
                        art.GetItem((_shown + offset) & 0xFFFF, _hue);
                }
            }
            _locked = ObjectHighlight.IsLocked(item.Flags, td.Weight);
            int priorityZ = item.Z + (td.IsBackground ? -1 : 0) + (td.Height > 0 ? 1 : 0);
            _item = new DrawItem { Kind = DrawKind.Static, Graphic = item.Graphic, X = item.X, Y = item.Y, Z = item.Z, PriorityZ = priorityZ };

            _go = new GameObject($"Item 0x{item.Graphic:X4}");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _shader = shader;
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.enabled = false;
            if (_twice)
            {
                var back = new GameObject("Stack copy");
                back.transform.SetParent(_go.transform, false);
                _backMesh = new Mesh();
                back.AddComponent<MeshFilter>().sharedMesh = _backMesh;
                _backRenderer = back.AddComponent<MeshRenderer>();
                _backRenderer.enabled = false;
            }
            _outline = new OutlineQuad(_go.transform, shader);
            // A free-standing object (a tree, a sign, a lamp...) casts its own outline (ShadowRules, CharacterShadow):
            // decided once its art is in (Render), since a low tiledata height can still be tall art.
            _shadowTd = td;
            _shadowPending = !item.IsCorpse && !item.IsMulti && CharacterShadow.ShadowShader != null;
            Placed = ++_placements;
        }

        private CharacterShadow _shadow; // null: no outline shadow (the TEST column, if any, as before)
        private readonly StaticTileData _shadowTd;
        private bool _shadowPending;

        private readonly int _shown;          // the art drawn: ItemStack.DisplayedGraphic of the graphic as sent
        private readonly bool _animates;      // animdata frames (ArtAnimation)
        private readonly AnimDataEntry _animation;
        private int _spriteGraphic = -1;      // the frame graphic _sprite holds

        /// <summary>The clock every animated art runs on (ms; one per graphic, shared with the map's statics).</summary>
        public static double AnimationClockMs => Time.unscaledTimeAsDouble * 1000.0;

        /// <summary>It is a light source (ItemLights): its tile, graphic as sent and light id.</summary>
        public bool GivesLight { get; }
        public int LightId { get; }
        public int TileX => _item.X;
        public int TileY => _item.Y;
        public int TileZ => _item.Z;
        public int Graphic => _graphic;

        /// <summary>The art graphic last drawn (the frame's, for animated art).</summary>
        public int DrawnGraphic => _spriteGraphic;
        private readonly bool _twice;         // a stack: a second copy up-left, under it
        private readonly Mesh _backMesh;
        private readonly MeshRenderer _backRenderer;

        /// <summary>The stack's second copy as the world pick sees it (hit on either copy, ItemView.cs:509-520), or null.</summary>
        public MousePick.Sprite? BackPickSprite =>
            _backRenderer != null && _backRenderer.enabled ? MobileArt.PickSprite(_backRect, LastSlot, false, _backDepth, LastCut) : (MousePick.Sprite?)null;

        private Rect _backRect;
        private float _backDepth;

        private static long _placements;

        /// <summary>When this view was made: every item update makes a new one, as TazUO's re-adds the item to its tile's end.</summary>
        public long Placed { get; }

        /// <summary>The depth key's sort key; items sharing one draw in <see cref="Tie"/> order (<see cref="MousePick.TieRanks"/>).</summary>
        public long TieKey => MousePick.TieKey(_item.X + _item.Y, _item.PriorityZ);

        /// <summary>The tie level among items with the same <see cref="TieKey"/>: a later one is drawn, and picked, in front.</summary>
        public int Tie { get; set; }

        private readonly OutlineQuad _outline;

        /// <summary>The mouse-over outline is drawn around what was last drawn.</summary>
        public bool Outlined => _outline.Visible;

        /// <summary>What the world pick sees this as (<see cref="ObjectHighlight.Hovers"/>); multi parts are never offered to it.</summary>
        public ObjectHighlight.PickKind PickKind =>
            _corpse != null ? ObjectHighlight.PickKind.Corpse : _locked ? ObjectHighlight.PickKind.LockedItem : ObjectHighlight.PickKind.GroundItem;

        /// <summary>Drawn as a body (a corpse with frames), not as item art.</summary>
        public bool IsBody => _corpse != null && _lastCorpseFrame != null;

        private readonly Shader _shader;

        // One material per atlas page, shared by every item on it (a house is ~150 parts), so they batch.
        private static readonly Dictionary<Texture, Material> PageMaterials = new Dictionary<Texture, Material>();

        private static Material MaterialFor(Shader shader, Texture page)
        {
            if (!PageMaterials.TryGetValue(page, out Material m) || m == null)
                PageMaterials[page] = m = new Material(shader) { mainTexture = page };
            return m;
        }

        private static readonly Dictionary<Texture, Material> SpectralPageMaterials = new Dictionary<Texture, Material>();

        private static Material SpectralMaterialFor(Shader shader, Texture page)
        {
            if (!SpectralPageMaterials.TryGetValue(page, out Material m) || m == null)
            {
                SpectralPageMaterials[page] = m = new Material(shader) { mainTexture = page };
                Avatar.SetSpectral(m, true);
            }
            return m;
        }

        /// <summary>Cheap: one quad. Re-done every frame because the depth origin moves as the player travels.</summary>
        /// <summary>Tile height (houses hide their parts above the player while inside).</summary>
        public int Z => _item.Z;

        public bool Hidden { get; set; }

        /// <summary>Under the mouse in the world (TazUO's SelectedObject.Object): outlined, in its own hue (<see cref="ObjectHighlight"/>).</summary>
        public bool Selected { get; set; }

        private readonly bool _locked; // TazUO's Item.IsLocked: a fixture, never outlined without HighlightGameObjects

        /// <summary>This view draws <paramref name="item"/> as a corpse where it already lies, so a new look can be set in place.</summary>
        public bool IsCorpseOf(WorldItem item) =>
            _corpse != null && ReferenceEquals(_corpse, item) && item.IsCorpse && item.Graphic == _graphic && item.Hue == _hue
            && item.X == _item.X && item.Y == _item.Y && item.Z == _item.Z;

        /// <summary>A corpse's look changed (0x89 arrived, a piece was looted, the fall changed): what was drawn stays until the new frames are ready.</summary>
        public void SetCorpseLook(MobileLook look, int action)
        {
            _corpseLook = look;
            _corpseKey = look.Key;
            _corpseAction = action;
        }

        public void Render(IWorldSpace world)
        {
            if (_corpse != null)
                RenderCorpse(world);
            else
                RenderArt(world);
            // The edge of what was drawn this frame: a corpse's body (every worn layer) or the item's art.
            bool show = _renderer.enabled && ObjectHighlight.OutlinesInWorld(Selected, IsBody, _locked, ObjectHighlight.HighlightGameObjects);
            _outline.Update(show, _art, LastSlot, LastRect, LastMirror, LastDepth, LastCut);
            // Its sun shadow: the art laid on the ground from its base (statics stand on the bottom of their art).
            if (_shadowPending && _sprite != null)
            {
                _shadowPending = false;
                if (ShadowRules.CastsOutline(_shadowTd, _sprite.OriginalHeight))
                    _shadow = new CharacterShadow(_go.transform);
            }
            if (_shadow != null)
            {
                bool drawn = _renderer.enabled && _sprite != null;
                _shadow.Update(drawn, world, drawn ? _art.Atlas.Page(LastSlot.Page) : null, drawn ? _art.Atlas.Uv(LastSlot) : default, false,
                    _item.Left, _item.Top, _item.Width, _item.Height, new Vector2(0f, _item.Top + _item.Height), LastDepth,
                    _item.X, _item.Y, _item.Z);
            }
        }

        private void RenderCorpse(IWorldSpace world)
        {
            // DrawCorpse indexes LayerOrder.UsedLayers by the file direction (ItemView.cs:191-228), so that is the layer order.
            MobileFrame[] Frames(int action, int fileDir) => _art.Get(_corpseLook, _corpseKey, action, fileDir, fileDir);
            CorpseDraw.Pick pick = CorpseDraw.Select(_corpse, _corpseAction, Hidden, Frames, out MobileFrame[] frames);
            MobileFrame f;
            bool mirror;
            switch (pick.Art)
            {
                case CorpseDraw.Art.Body:
                    f = frames[pick.Frame];
                    mirror = pick.Mirror;
                    break;
                case CorpseDraw.Art.Building when _lastCorpseFrame != null:
                    f = _lastCorpseFrame;
                    mirror = _lastCorpseMirror;
                    break;
                case CorpseDraw.Art.StaticArt:
                    // The quad may have shown body frames from another atlas page until now.
                    _lastCorpseFrame = null;
                    if (_sprite != null)
                        _renderer.sharedMaterial = MaterialFor(_shader, _art.Atlas.Page(_sprite.Slot.Page));
                    else
                        _renderer.enabled = false;
                    RenderArt(world);
                    return;
                default: // hidden, or nothing built yet
                    _renderer.enabled = false;
                    return;
            }
            _lastCorpseFrame = f;
            _lastCorpseMirror = mirror;

            // Anchored like a character on its tile (Avatar.TileAnchor), so the corpse takes over from the fall in place.
            Vector2 anchor = new Vector2((_item.X - _item.Y) * 22, (_item.X + _item.Y) * 22 + 22 - _item.Z * 4) * _scale;
            float left = anchor.x - (mirror ? f.Slot.Width - f.AnchorX : f.AnchorX);
            float top = anchor.y - f.AnchorY;
            LastRect = world.ToUnityRect(left, top, f.Slot.Width, f.Slot.Height);
            LastDepth = Depth(world);
            LastSlot = f.Slot;
            LastMirror = mirror;
            // A spectral body (a wraith's corpse, hue 0x4001) is drawn blended, as Avatar draws it standing.
            _renderer.sharedMaterial = _art.IsSpectral(f.Slot)
                ? SpectralMaterialFor(_shader, _art.Atlas.Page(f.Slot.Page))
                : MaterialFor(_shader, _art.Atlas.Page(f.Slot.Page));
            _renderer.enabled = true;
            SetQuad(world, _art.Atlas.Uv(f.Slot), mirror);
        }

        private void SetQuad(IWorldSpace world, Vector4 uv, bool mirror)
        {
            if (!MeshBuilder.FootMetadata)
            {
                MeshBuilder.SetQuad(_mesh, LastRect, uv, LastDepth, mirror, LastCut, _spriteCode);
                return;
            }
            // LiveGroundCompare sprite shadow (TEST): stands upright on its tile's centre (Avatar.TileAnchor).
            Vector2 foot = world.ToUnity(new Vector2((_item.X - _item.Y) * 22, (_item.X + _item.Y) * 22 + 22 - _item.Z * 4) * _scale);
            MeshBuilder.SetQuad(_mesh, LastRect, uv, LastDepth, mirror, LastCut, new Vector4(foot.x, foot.y, _item.Z, 1f), _spriteCode);
            if (_shadow == null) // an outline caster does not also cast a column
                MeshBuilder.ShadowCasters?.Item(this, _item.X, _item.Y, _item.Z, _graphic);
        }

        private void RenderArt(IWorldSpace world)
        {
            int want = _animates ? ArtAnimation.Graphic(_shown, _animation, AnimationClockMs) : _shown;
            if (_sprite == null || want != _spriteGraphic)
            {
                ItemSprite next = _art.GetItem(want, _hue);
                if (next != null)
                {
                    // Each frame is placed by its own art, as DrawStaticAnimated re-anchors on the frame's size.
                    _sprite = next;
                    _spriteGraphic = want;
                    (_item.Left, _item.Top, _item.Width, _item.Height) = IsoCompositor.PlacementRect(_item, _scale, _sprite.OriginalWidth, _sprite.OriginalHeight);
                }
                else if (_sprite == null)
                    return; // building, or no art (a frame still building keeps the last one shown)
            }
            _renderer.enabled = !Hidden;
            if (_backRenderer != null)
                _backRenderer.enabled = !Hidden;
            if (Hidden)
                return;
            // Always the item's own hue: the mouse-over cue is the outline alone (ObjectHighlight).
            ItemSprite sprite = _sprite;
            Material material = MaterialFor(_shader, _art.Atlas.Page(sprite.Slot.Page));
            _renderer.sharedMaterial = material;
            LastRect = world.ToUnityRect(_item.Left, _item.Top, _item.Width, _item.Height);
            LastDepth = Depth(world);
            LastSlot = sprite.Slot;
            LastMirror = false;
            Vector4 uv = _art.Atlas.Uv(sprite.Slot);
            SetQuad(world, uv, false);
            if (_backRenderer != null)
            {
                // TazUO draws the copy first, 5 art pixels up-left: just behind this item, in front of anything behind it.
                int o = ItemStack.GroundOffset * _scale;
                _backRect = world.ToUnityRect(_item.Left + o, _item.Top + o, _item.Width, _item.Height);
                _backDepth = LastDepth + DepthKey.Step * 0.5f;
                _backRenderer.sharedMaterial = material;
                // The same foot as the item (TEST sprite shadow), so both copies shade alike; the plain quad otherwise.
                Vector2 foot = world.ToUnity(new Vector2((_item.X - _item.Y) * 22, (_item.X + _item.Y) * 22 + 22 - _item.Z * 4) * _scale);
                MeshBuilder.SetQuad(_backMesh, _backRect, uv, _backDepth, false, LastCut, new Vector4(foot.x, foot.y, _item.Z, 1f));
            }
        }

        private readonly float _spriteCode; // PlantKind.Window for a house window, else 0

        // DepthKey's tie levels are its last digit: tie t is t steps nearer than tie 0.
        private float Depth(IWorldSpace world) => world.DepthFor(_item) - Mathf.Clamp(Tie, 0, DepthKey.TieLevels - 1) * DepthKey.Step;

        /// <summary>The upper-floor cut height the quad is drawn with (WorldSprite.shader hides it under the cut).</summary>
        public float LastCut => MeshBuilder.CutHeight(_item.Z);

        /// <summary>Where the item was last drawn (Unity units) and its depth, for mouse picking; empty until its art is ready.</summary>
        public Rect LastRect { get; private set; }
        public float LastDepth { get; private set; }

        /// <summary>The sprite last drawn and whether it was mirrored (a corpse), for pixel-exact picking.</summary>
        public AtlasSlot LastSlot { get; private set; }
        public bool LastMirror { get; private set; }

        /// <summary>Whether it was drawn last frame, so the pick may take it.</summary>
        public bool Drawn => _renderer.enabled;

        /// <summary>What was last drawn, as the world pick (<see cref="MousePick"/>) sees it.</summary>
        public MousePick.Sprite PickSprite => MobileArt.PickSprite(LastRect, LastSlot, LastMirror, LastDepth, LastCut);

        public void Dispose()
        {
            _outline.Dispose();
            _shadow?.Dispose();
            if (_backMesh != null)
                UnityEngine.Object.Destroy(_backMesh);
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_go);
        }
    }
}
