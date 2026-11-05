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
