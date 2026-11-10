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
        /// <summary>-nocharshadows was given: no shadows this run whatever the options say.</summary>
        public static readonly bool OffFromLaunch = Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-nocharshadows", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether shadows are drawn this run. Fixed at startup (<see cref="StartWith"/>): characters, items and map chunks
        /// build their shadows when they are made, so turning them on or off mid-run would leave some with and some without.
        /// </summary>
        public static bool Enabled { get; private set; } = !OffFromLaunch;

        private static bool _started;

        /// <summary>
        /// The saved option (GameOptions.shadows, TazUO's ShadowsEnabled), once, before the first world is built; later
        /// calls are ignored (the change waits for the next start). -nocharshadows still wins.
        /// </summary>
        public static void StartWith(bool option)
        {
            if (_started)
                return;
            _started = true;
            Enabled = !OffFromLaunch && option;
        }

        /// <summary>
        /// The options' sun shadows (GameOptions.sunShadows), every frame: off, the sun's and moon's shadows (and the
        /// clouds', which fade with them) are faded out through _UOSunFade (<see cref="SetSun"/>); a lamp's indoors stay.
        /// </summary>
        public static bool SunShadows = true;

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
        private static readonly Vector2[] Heights = new Vector2[4];
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
            // The reference sun: what every quad is built with. The time of day moves it in the shader (SetSun).
            Vector3 sun = SunGround ?? DefaultSun;
            var g = new Vector2(sun.x, sun.y).normalized;
            float tilesPerPixel = 1f / (4f * Mathf.Max(0.5f, sun.z));
            var perPixel = new Vector2((g.x - g.y) * 22f, (g.x + g.y) * 22f) * tilesPerPixel;
            return perPixel.magnitude > MaxLength ? perPixel.normalized * MaxLength : perPixel;
        }

        /// <summary>The reference sun's elevation (LiveGroundLighting's compositor light, 14:00 on the shard's clock).</summary>
        private static readonly float RefElevation = Mathf.Atan2(0.77f, Mathf.Sqrt(0.6f * 0.6f + 0.2f * 0.2f));

        private static readonly int SunShiftId = Shader.PropertyToID("_UOSunShift"), SunFadeId = Shader.PropertyToID("_UOSunFade");

        /// <summary>
        /// The time of day's sun: <paramref name="towardSun"/> its ground direction (tile space), <paramref name="elevation"/>
        /// its height (radians), <paramref name="strength"/> 0..1. Sun shadows are built with the reference sun
        /// (<see cref="PerPixel"/>) and carry each corner's height above the foot (uv2.x), so the shader moves them by
        /// height x (now - reference) per pixel: they swing and stretch every frame with no rebuild. Lamp shadows stay put.
        /// </summary>
        public static void SetSun(Vector2 towardSun, float elevation, float strength)
        {
            Vector3 reference = SunGround ?? DefaultSun;
            Vector2 g = towardSun.sqrMagnitude > 1e-8f ? -towardSun.normalized : new Vector2(reference.x, reference.y).normalized;
            float rise = reference.z * Mathf.Tan(Mathf.Max(0.02f, elevation)) / Mathf.Tan(RefElevation);
            var now = new Vector2((g.x - g.y) * 22f, (g.x + g.y) * 22f) / (4f * Mathf.Max(0.05f, rise));
            if (now.magnitude > SunMaxLength)
                now = now.normalized * SunMaxLength;
            Vector2 shift = now - PerPixel();
            Shader.SetGlobalVector(SunShiftId, new Vector4(shift.x, -shift.y, 0f, 0f)); // world pixels (y down) to Unity (y up)
            Shader.SetGlobalFloat(SunFadeId, SunShadows ? 1f - Mathf.Clamp01(strength) : 1f);
            CurrentPerPixel = now;
        }

        /// <summary>A low sun's longest shadow, per pixel of height (morning and evening; noon's is about 0.8).</summary>
        public const float SunMaxLength = 2.2f;

        /// <summary>The sun shadow's run per pixel of height as last set (<see cref="SetSun"/>).</summary>
        public static Vector2 CurrentPerPixel { get; private set; }

        /// <summary>
        /// Appends the shadow of art drawn at world-screen pixel rect (left, top, width, height) standing on the line
        /// <paramref name="baseY"/>: the quad laid away from the sun, corners as MeshBuilder.SetQuad's.
        /// </summary>
        public static void AddQuad(System.Collections.Generic.List<Vector3> vertices, System.Collections.Generic.List<Vector2> uvs,
            System.Collections.Generic.List<int> indices, System.Collections.Generic.List<Color32> colors, Color32 tint,
            IWorldSpace view, Vector2 perPixel, Vector4 uv, bool mirror,
            float left, float top, float width, float height, float baseY, float depth,
            System.Collections.Generic.List<Vector2> heights = null, float cutHeight = 0f)
        {
            int n = vertices.Count;
            for (int k = 0; k < 4; k++)
                colors.Add(tint);
            if (heights != null)
            {
                // x: each corner's height above the foot line, the shader's lever for the moving sun (SetSun);
                // y: the caster's cut height (MeshBuilder.CutHeight), so the shadow hides with it under an upper floor.
                float bottom = baseY - (top + height), upper = baseY - top;
                heights.Add(new Vector2(bottom, cutHeight));
                heights.Add(new Vector2(upper, cutHeight));
                heights.Add(new Vector2(upper, cutHeight));
                heights.Add(new Vector2(bottom, cutHeight));
            }
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
            float cut = MeshBuilder.CutHeight(tileZ); // hidden with the caster when the floor above is cut away
            Heights[0] = Heights[3] = new Vector2(foot.y - (top + height), cut);
            Heights[1] = Heights[2] = new Vector2(foot.y - top, cut);
            if (_mesh.vertexCount != 4)
            {
                _mesh.Clear();
                _mesh.vertices = Vertices;
                _mesh.uv = Uvs;
                _mesh.uv2 = Heights;
                _mesh.colors32 = Colors;
                _mesh.triangles = Indices;
            }
            else
            {
                _mesh.vertices = Vertices;
                _mesh.uv = Uvs;
                _mesh.uv2 = Heights;
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
