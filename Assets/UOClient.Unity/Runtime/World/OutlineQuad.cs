using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.Unity
{
    /// <summary>
    /// The mouse-over outline of one world sprite (a character, a corpse, a ground item): a second quad drawn just in
    /// front of the sprite with its <see cref="MobileArt.Outline"/>, so the edge of what was drawn (every worn layer of a
    /// character) shows, not a box. Unlit, so the cue stays as bright at night.
    /// </summary>
    public sealed class OutlineQuad
    {
        // In front of the sprite, behind whatever sorts after it on the same diagonal (DepthKey.Step apart).
        private const float DepthBias = DepthKey.Step * 0.2f;

        private static readonly Dictionary<Texture, Material> PageMaterials = new Dictionary<Texture, Material>();

        private readonly Shader _shader;
        private readonly Transform _parent;
        private GameObject _go; // made the first time it shows: a house is ~150 item views that never do
        private Mesh _mesh;
        private MeshRenderer _renderer;

        public OutlineQuad(Transform parent, Shader shader)
        {
            _parent = parent;
            _shader = shader;
        }

        public bool Visible => _renderer != null && _renderer.enabled;

        /// <summary>Outlines the sprite <paramref name="slot"/> drawn over <paramref name="drawn"/>, or hides the outline.</summary>
        public void Update(bool show, MobileArt art, AtlasSlot slot, Rect drawn, bool mirror, float depth, float cutHeight)
        {
            if (!show || slot.Width <= 0 || slot.Height <= 0)
            {
                if (_renderer != null)
                    _renderer.enabled = false;
                return;
            }
            if (_go == null)
            {
                _go = new GameObject("Outline");
                _go.transform.SetParent(_parent, false);
                _mesh = new Mesh { name = "outline quad" };
                _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
                _renderer = _go.AddComponent<MeshRenderer>();
                _renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            AtlasSlot o = art.Outline(slot);
            int t = art.OutlineThickness;
            float kx = drawn.width / slot.Width, ky = drawn.height / slot.Height;
            var r = new Rect(drawn.xMin - t * kx, drawn.yMin - t * ky, drawn.width + 2 * t * kx, drawn.height + 2 * t * ky);
            _renderer.sharedMaterial = MaterialFor(art.Atlas.Page(o.Page));
            _renderer.enabled = true;
            MeshBuilder.SetQuad(_mesh, r, art.Atlas.Uv(o), depth - DepthBias, mirror, cutHeight);
        }

        private Material MaterialFor(Texture page)
        {
            if (!PageMaterials.TryGetValue(page, out Material m) || m == null)
            {
                PageMaterials[page] = m = new Material(_shader) { mainTexture = page, name = "UO outline" };
                m.SetFloat("_Unlit", 1f);
            }
            return m;
        }

        public void Dispose()
        {
            if (_go == null)
                return;
            Object.Destroy(_mesh);
            Object.Destroy(_go);
        }
    }
}
