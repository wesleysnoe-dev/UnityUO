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

        public ItemView(UOData data, MobileArt art, Transform parent, Shader shader, int scale, WorldItem item, MobileLook corpseLook = null)
        {
            _art = art;
            _scale = scale;
            _graphic = item.Graphic;
            _hue = item.Hue;
            if (corpseLook != null && item.IsCorpse)
            {
                _corpse = item;
                SetCorpseLook(corpseLook, CorpseDraw.Action(data.AnimationLibrary, item));
            }
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
            {
                RenderCorpse(world);
                return;
            }
            RenderArt(world);
        }

        private void RenderCorpse(IWorldSpace world)
        {
            CorpseDraw.Pick pick = CorpseDraw.Select(_corpse, _corpseAction, Hidden,
                (action, fileDir) => _art.Get(_corpseLook, _corpseKey, action, fileDir), out MobileFrame[] frames);
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
            LastDepth = world.DepthFor(_item);
            _renderer.sharedMaterial = MaterialFor(_shader, _art.Atlas.Page(f.Slot.Page));
            _renderer.enabled = true;
            MeshBuilder.SetQuad(_mesh, LastRect, _art.Atlas.Uv(f.Slot), LastDepth, mirror, MeshBuilder.CutHeight(_item.Z));
        }

        private void RenderArt(IWorldSpace world)
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
