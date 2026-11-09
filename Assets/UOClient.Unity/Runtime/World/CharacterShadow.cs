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

        /// <summary>
        /// Indoors (a roof or a floor above): no sun shadow; only the light of a lamp, torch, candle or brazier casts one.
        /// Set by the world (Walkability.IsUnderRoof); null: always outdoors.
        /// </summary>
        public static Func<int, int, int, bool> Indoors;

        /// <summary>Whether a wall stands between a light (x, y) and a tile (x, y, z) (Walkability.WallBetween); null: never.</summary>
        public static Func<int, int, int, int, int, bool> WallBetween;

        /// <summary>This frame's light sources near the player (ItemLights' candidates: tile and Z), for characters and items.</summary>
        public static readonly System.Collections.Generic.List<Vector3Int> LightSources = new System.Collections.Generic.List<Vector3Int>();

        /// <summary>How far (tiles) a light casts indoor shadows; they fade out toward it.</summary>
        public const float LightReach = 8f;

        /// <summary>A flame stands about this much above its object's Z (a candle on a table, a wall torch).</summary>
        public const int LightHeight = 12;

        /// <summary>
        /// How a caster on tile (x, y) at Z casts: outdoors, away from the sun (full strength, fading with daylight in the
        /// shader); indoors, away from the nearest light within <see cref="LightReach"/> on about the same level, longer
        /// the lower the light, fading with distance, by day and night alike. False: no shadow (indoors with no light near,
        /// or the caster is the light).
        /// </summary>
        public static bool Cast(int x, int y, int z, bool indoors, System.Collections.Generic.IReadOnlyList<Vector3Int> lights,
            out Vector2 perPixel, out float strength, out bool sun)
        {
            if (!indoors)
            {
                perPixel = PerPixel();
                strength = 1f;
                sun = true;
                return true;
            }
            perPixel = default;
            strength = 0f;
            sun = false;
            float best = float.MaxValue;
            Vector3Int light = default;
            if (lights != null)
                foreach (Vector3Int l in lights)
                {
                    if (Mathf.Abs(l.z - z) > 20 || l.x == x && l.y == y)
                        continue; // another floor; or the light itself
                    float d = new Vector2(x - l.x, y - l.y).sqrMagnitude;
                    if (d < best && d <= LightReach * LightReach && (WallBetween == null || !WallBetween(l.x, l.y, x, y, z)))
                    {
                        best = d; // the nearest light with no wall between (a street lamp does not shine in)
                        light = l;
                    }
                }
            float dist = Mathf.Sqrt(best);
            if (dist > LightReach)
                return false;
            var g = new Vector2(x - light.x, y - light.y) / Mathf.Max(dist, 1e-3f);
            float rise = Mathf.Max(0.6f, (light.z + LightHeight - z) / Mathf.Max(dist, 0.5f)); // Z per tile of the light's ray
            perPixel = new Vector2((g.x - g.y) * 22f, (g.x + g.y) * 22f) / (4f * rise);
            if (perPixel.magnitude > MaxLength)
                perPixel = perPixel.normalized * MaxLength;
            strength = Mathf.Pow(Mathf.Clamp01(1f - dist / LightReach), 1.2f);
            return strength > 0.02f;
        }

        /// <summary>The shadow quad's colour: g 1 for a sun shadow (fades with daylight), a its strength.</summary>
        public static Color32 Tint(float strength, bool sun) => new Color32(255, (byte)(sun ? 255 : 0), 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(strength) * 255f));

        private static Shader _shader;
        private static readonly Vector3[] Vertices = new Vector3[4];
        private static readonly Vector2[] Uvs = new Vector2[4];
        private static readonly Color32[] Colors = new Color32[4];
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
            System.Collections.Generic.List<int> indices, System.Collections.Generic.List<Color32> colors, Color32 tint,
            IWorldSpace view, Vector2 perPixel, Vector4 uv, bool mirror,
            float left, float top, float width, float height, float baseY, float depth)
        {
            int n = vertices.Count;
            for (int k = 0; k < 4; k++)
                colors.Add(tint);
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
            float left, float top, float width, float height, Vector2 foot, float depth, int tileX, int tileY, int tileZ)
        {
            if (_renderer == null)
                return;
            bool indoors = show && Indoors != null && Indoors(tileX, tileY, tileZ);
            if (!show || page == null || !Cast(tileX, tileY, tileZ, indoors, LightSources, out Vector2 perPixel, out float strength, out bool sun))
            {
                _renderer.enabled = false;
                return;
            }
            Color32 tint = Tint(strength, sun);
            for (int k = 0; k < 4; k++)
                Colors[k] = tint;

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
                _mesh.colors32 = Colors;
                _mesh.triangles = Indices;
            }
            else
            {
                _mesh.vertices = Vertices;
                _mesh.uv = Uvs;
                _mesh.colors32 = Colors;
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
