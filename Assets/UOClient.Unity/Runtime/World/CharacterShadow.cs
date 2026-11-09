using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace UOClient.Unity
{
    /// <summary>
    /// A character's sun shadow drawn from its own frame (as ClassicUO's shadow option does): the sprite's quad laid flat on
    /// the ground away from the sun, pinned at the foot line, darkened by Resources/CharacterShadow.shader. It has the
    /// character's real outline, follows every animation frame and always meets the feet.
    /// Drawn after the grass and before every sprite (no depth test), so the grass under it is shaded and anything drawn
    /// in its place, the character first, stays on top. Fades with the time-of-day light in the shader.
    /// The sun's ground direction and rise come from <see cref="SunGround"/> (the LiveGround terrain sets its own sun;
    /// otherwise a default from the upper left). -nocharshadows turns them off.
    /// </summary>
    public sealed class CharacterShadow : IDisposable
    {
        public static readonly bool Enabled = !Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-nocharshadows", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The sun: the ground direction (tile units, x and y) a shadow falls in, and the sun's rise (Z per tile of shadow
        /// length, as LiveGroundLighting.SunRay). Set by a renderer with its own sun; null for the default.
        /// </summary>
        public static Vector3? SunGround;

        private static readonly Vector3 DefaultSun = new Vector3(0.6f, 0.8f, 9f);

        /// <summary>The longest a shadow is drawn, as a share of the character's height on screen.</summary>
        public const float MaxLength = 0.9f;

        private static Shader _shader;
        private static readonly Vector3[] Vertices = new Vector3[4];
        private static readonly Vector2[] Uvs = new Vector2[4];
        private static readonly int[] Indices = { 0, 1, 2, 0, 2, 3 };

        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;

        /// <summary>The shadow shader (Resources/CharacterShadow.shader), or null when shadows are off or unsupported.</summary>
        public static Shader ShadowShader
        {
            get
            {
                _shader ??= Resources.Load<Shader>("CharacterShadow");
                return Enabled && _shader != null && _shader.isSupported ? _shader : null;
            }
        }

        /// <summary>
        /// Screen offset (world pixels, y down) per pixel of height above the base: h pixels is h / 4 Z (the Z step and
        /// the tile at the same art scale), which casts h / 4 / rise tiles along the ground away from the sun.
        /// </summary>
        public static Vector2 PerPixel()
        {
            Vector3 sun = SunGround ?? DefaultSun;
            var g = new Vector2(sun.x, sun.y).normalized;
            float tilesPerPixel = 1f / (4f * Mathf.Max(0.5f, sun.z));
            var perPixel = new Vector2((g.x - g.y) * 22f, (g.x + g.y) * 22f) * tilesPerPixel;
            return perPixel.magnitude > MaxLength ? perPixel.normalized * MaxLength : perPixel;
        }

        /// <summary>
        /// Appends the shadow of art drawn at world-screen pixel rect (left, top, width, height) standing on the line
        /// <paramref name="baseY"/>: the quad laid away from the sun, corners as MeshBuilder.SetQuad's.
        /// </summary>
        public static void AddQuad(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> indices, IWorldSpace view, Vector2 perPixel, Vector4 uv, bool mirror,
            float left, float top, float width, float height, float baseY, float depth)
        {
            int n = vertices.Count;
            var foot = new Vector2(0f, baseY);
            vertices.Add(Ground(view, left, top + height, foot, perPixel, depth));
            vertices.Add(Ground(view, left, top, foot, perPixel, depth));
            vertices.Add(Ground(view, left + width, top, foot, perPixel, depth));
            vertices.Add(Ground(view, left + width, top + height, foot, perPixel, depth));
            float u0 = mirror ? uv.z : uv.x, u1 = mirror ? uv.x : uv.z;
            uvs.Add(new Vector2(u0, uv.y));
            uvs.Add(new Vector2(u0, uv.w));
            uvs.Add(new Vector2(u1, uv.w));
            uvs.Add(new Vector2(u1, uv.y));
            indices.Add(n);
            indices.Add(n + 1);
            indices.Add(n + 2);
            indices.Add(n);
            indices.Add(n + 2);
            indices.Add(n + 3);
        }

        public CharacterShadow(Transform parent)
        {
            if (ShadowShader == null)
                return;
            _go = new GameObject("Shadow");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Character shadow" };
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(_shader) { name = "Character shadow" };
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        /// <summary>
        /// The shadow of a frame drawn at world-screen pixel rect (<paramref name="left"/>, <paramref name="top"/>,
        /// <paramref name="width"/>, <paramref name="height"/>) with its feet at <paramref name="foot"/>.
        /// </summary>
        public void Update(bool show, IWorldSpace view, Texture page, Vector4 uv, bool mirror,
            float left, float top, float width, float height, Vector2 foot, float depth)
        {
            if (_renderer == null)
                return;
            if (!show || page == null)
            {
                _renderer.enabled = false;
                return;
            }
            Vector2 perPixel = PerPixel();

            // Corners as the sprite's quad (MeshBuilder.SetQuad): bottom-left, top-left, top-right, bottom-right.
            Vertices[0] = Ground(view, left, top + height, foot, perPixel, depth);
            Vertices[1] = Ground(view, left, top, foot, perPixel, depth);
            Vertices[2] = Ground(view, left + width, top, foot, perPixel, depth);
            Vertices[3] = Ground(view, left + width, top + height, foot, perPixel, depth);
            float u0 = mirror ? uv.z : uv.x, u1 = mirror ? uv.x : uv.z;
            Uvs[0] = new Vector2(u0, uv.y);
            Uvs[1] = new Vector2(u0, uv.w);
            Uvs[2] = new Vector2(u1, uv.w);
            Uvs[3] = new Vector2(u1, uv.y);
            if (_mesh.vertexCount != 4)
            {
                _mesh.Clear();
                _mesh.vertices = Vertices;
                _mesh.uv = Uvs;
                _mesh.triangles = Indices;
            }
            else
            {
                _mesh.vertices = Vertices;
                _mesh.uv = Uvs;
            }
            _mesh.RecalculateBounds();
            _material.mainTexture = page;
            _renderer.enabled = true;
        }

        /// <summary>A sprite corner laid on the ground: its height above the feet becomes a run away from the sun.</summary>
        private static Vector3 Ground(IWorldSpace view, float x, float y, Vector2 foot, Vector2 perPixel, float depth)
        {
            float above = foot.y - y; // pixels above the foot line (negative below it)
            Vector2 u = view.ToUnity(new Vector2(x + perPixel.x * above, foot.y + perPixel.y * above));
            return new Vector3(u.x, u.y, depth);
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
