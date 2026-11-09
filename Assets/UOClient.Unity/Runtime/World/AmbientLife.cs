using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.ArtPipeline;
using UOClient.Core;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// Small life around the player, drawn as one mesh with Resources/AmbientLife.shader: fireflies over grass and plants
    /// at night, leaves falling from tree canopies (by season: fresh green, gold and red in fall, none in winter; more in
    /// wind), and pollen drifting over meadows on spring and summer days. Each spawns on a real tile near the player (a
    /// sample of tiles every frame, read through Walkability), never indoors, and sorts with the world as a character on
    /// that tile would, so buildings and trees hide it. Rain stops fireflies and pollen. -noambient turns it off.
    /// </summary>
    public sealed class AmbientLife : IDisposable
    {
        public static readonly bool Enabled = !Array.Exists(Environment.GetCommandLineArgs(),
            a => string.Equals(a, "-noambient", StringComparison.OrdinalIgnoreCase));

        private const int MaxParticles = 220;
        private const int Radius = 15;          // tiles around the player where life spawns
        private const int SamplesPerFrame = 10; // tiles looked at each frame

        private enum Kind : byte { Firefly = 0, Leaf = 1, Pollen = 2 }

        private struct Particle
        {
            public Kind Kind;
            public float X, Y, Z;      // UO tile units and Z (fractional)
            public float Vx, Vy, Vz;   // per second
            public float Age, Life;
            public float Seed;
            public Color32 Colour;
        }

        private readonly List<Particle> _particles = new List<Particle>(MaxParticles);
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private readonly System.Random _rng = new System.Random(1234);

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Vector2> _kinds = new List<Vector2>();
        private readonly List<Color32> _colours = new List<Color32>();
        private readonly List<int> _indices = new List<int>();

        public int Count => _particles.Count;

        public AmbientLife(Transform parent)
        {
            Shader shader = Enabled ? Resources.Load<Shader>("AmbientLife") : null;
            if (shader == null || !shader.isSupported)
            {
                Debug.Log($"UOClient ambient life: off ({(!Enabled ? "-noambient" : "shader missing or unsupported")})");
                return;
            }
            _go = new GameObject("Ambient life");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh { name = "Ambient life" };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(shader) { name = "Ambient life" };
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            Debug.Log("UOClient ambient life: on (fireflies at night, falling leaves by season, pollen on spring and summer days)");
        }

        /// <summary>Every frame: spawn by what is around the player, move, and rebuild the mesh.</summary>
        public void Update(GameContext ctx, SunState sun, Season season, float rain, float wind)
        {
            if (_renderer == null || ctx.World == null || ctx.Walk == null || ctx.Player == null || ctx.Data == null)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool night = sun.Moon || sun.Strength < 0.15f && (sun.Hour < 6f || sun.Hour > 21f);
            bool dryish = rain < 0.15f;
            // How many of each the scene wants (a target the spawner fills, so life comes and goes gradually).
            int wantFlies = night && dryish && season != Season.Winter ? 60 : 0;
            int wantLeaves = season == Season.Winter || season == Season.Desolation ? 0 : (int)((season == Season.Fall ? 70 : 18) * (1f + wind * 1.5f));
            int wantPollen = !night && dryish && (season == Season.Spring || season == Season.Summer) ? 45 : 0;
            int flies = 0, leaves = 0, pollen = 0;
            foreach (Particle p in _particles)
                if (p.Kind == Kind.Firefly) flies++; else if (p.Kind == Kind.Leaf) leaves++; else pollen++;

            for (int s = 0; s < SamplesPerFrame && _particles.Count < MaxParticles; s++)
            {
                int x = ctx.Player.X + _rng.Next(-Radius, Radius + 1), y = ctx.Player.Y + _rng.Next(-Radius, Radius + 1);
                if (x < 0 || y < 0)
                    continue;
                int landZ = ctx.Walk.LandZ(x, y);
                if (CharacterShadow.Indoors != null && CharacterShadow.Indoors(x, y, landZ))
                    continue;
                StaticCell[] statics = ctx.Walk.StaticsOn(x, y);
                bool canopy = false, plants = false;
                int canopyZ = landZ;
                foreach (StaticCell c in statics)
                {
                    StaticTileData td = ctx.Data.TileData.GetStatic(c.Graphic);
                    float code = PlantKind.Of(td);
                    if ((td.Flags & TileFlags.Foliage) != 0)
                    {
                        canopy = true;
                        canopyZ = Math.Max(canopyZ, c.Z);
                    }
                    else if (code > 0f)
                        plants = true;
                }
                string landName = ctx.Data.TileData.Land[ctx.Data.GetMap(ctx.World.Map).GetLand(x, y).Graphic & 0x3FFF].Name ?? "";
                bool green = plants || landName.Contains("grass") || landName.Contains("forest") || landName.Contains("jungle");
                if (canopy && leaves < wantLeaves && _rng.NextDouble() < 0.5)
                {
                    leaves++;
                    Spawn(Kind.Leaf, x, y, canopyZ + 18 + (float)_rng.NextDouble() * 14f, LeafColour(season));
                }
                else if (green && flies < wantFlies)
                {
                    flies++;
                    Spawn(Kind.Firefly, x, y, landZ + 3 + (float)_rng.NextDouble() * 10f, new Color32(206, 255, 120, 255));
                }
                else if (green && !canopy && pollen < wantPollen)
                {
                    pollen++;
                    Spawn(Kind.Pollen, x, y, landZ + 4 + (float)_rng.NextDouble() * 14f, new Color32(255, 250, 225, 255));
                }
            }

            float t = Time.time;
            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                Particle p = _particles[i];
                p.Age += dt;
                if (p.Age >= p.Life || Mathf.Abs(p.X - ctx.Player.X) > Radius + 6 || Mathf.Abs(p.Y - ctx.Player.Y) > Radius + 6)
                {
                    _particles.RemoveAt(i);
                    continue;
                }
                switch (p.Kind)
                {
                    case Kind.Firefly: // a slow wander, bobbing
                        p.Vx += (Mathf.PerlinNoise(p.Seed, t * 0.4f) - 0.5f) * dt * 1.2f;
                        p.Vy += (Mathf.PerlinNoise(t * 0.4f, p.Seed) - 0.5f) * dt * 1.2f;
                        p.Vx = Mathf.Clamp(p.Vx, -0.35f, 0.35f);
                        p.Vy = Mathf.Clamp(p.Vy, -0.35f, 0.35f);
                        p.Vz = Mathf.Sin(t * 1.3f + p.Seed * 9f) * 2f;
                        break;
                    case Kind.Leaf: // falls slowly, fluttering side to side, carried by the wind
                        p.Vz = -5.5f - wind * 3f;
                        p.Vx = Mathf.Sin(t * 2.2f + p.Seed * 7f) * 0.6f + wind * 1.4f;
                        p.Vy = Mathf.Cos(t * 1.7f + p.Seed * 5f) * 0.3f - wind * 0.4f;
                        if (p.Z <= ctx.Walk.LandZ((int)p.X, (int)p.Y) + 0.5f)
                        {
                            p.Vx = p.Vy = p.Vz = 0f; // lies on the ground a moment, then fades
                            p.Life = Mathf.Min(p.Life, p.Age + 2.5f);
                        }
                        break;
                    default: // pollen: drifts with the wind, rising and sinking a little
                        p.Vx = 0.25f + wind * 1.2f + Mathf.Sin(t * 0.7f + p.Seed * 3f) * 0.12f;
                        p.Vy = -0.08f + Mathf.Cos(t * 0.6f + p.Seed * 4f) * 0.1f;
                        p.Vz = Mathf.Sin(t * 0.9f + p.Seed * 6f) * 1.5f;
                        break;
                }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Z += p.Vz * dt;
                _particles[i] = p;
            }
            Rebuild(ctx, t);
        }

        private void Spawn(Kind kind, int x, int y, float z, Color32 colour)
        {
            float life = kind == Kind.Firefly ? 8f + (float)_rng.NextDouble() * 10f
                       : kind == Kind.Leaf ? 9f + (float)_rng.NextDouble() * 6f
                       : 10f + (float)_rng.NextDouble() * 8f;
            _particles.Add(new Particle
            {
                Kind = kind, X = x + (float)_rng.NextDouble(), Y = y + (float)_rng.NextDouble(), Z = z,
                Life = life, Seed = (float)_rng.NextDouble() * 100f, Colour = colour
            });
        }

        private Color32 LeafColour(Season season)
        {
            double r = _rng.NextDouble();
            if (season == Season.Fall)
                return r < 0.35 ? new Color32(232, 176, 52, 255) : r < 0.7 ? new Color32(214, 104, 34, 255) : new Color32(168, 48, 30, 255);
            return r < 0.5 ? new Color32(96, 150, 52, 255) : new Color32(128, 170, 64, 255);
        }

        private void Rebuild(GameContext ctx, float t)
        {
            _vertices.Clear();
            _uvs.Clear();
            _kinds.Clear();
            _colours.Clear();
            _indices.Clear();
            int s = Mathf.Max(1, ctx.ArtScale);
            foreach (Particle p in _particles)
            {
                // Fade in and out over a second at each end of its life.
                float fade = Mathf.Clamp01(p.Age) * Mathf.Clamp01(p.Life - p.Age);
                float size;
                switch (p.Kind)
                {
                    case Kind.Firefly:
                        float blink = Mathf.Max(0f, Mathf.Sin(t * 2.4f + p.Seed * 13f));
                        fade *= 0.15f + 0.85f * blink * blink;
                        size = 16f;
                        break;
                    case Kind.Leaf:
                        size = 6.5f;
                        break;
                    default:
                        fade *= 0.7f;
                        size = 2.6f;
                        break;
                }
                if (fade <= 0.01f)
                    continue;
                Vector2 px = new Vector2((p.X - p.Y) * 22f, (p.X + p.Y) * 22f - p.Z * 4f) * s;
                Vector2 u = ctx.World.ToUnity(px);
                int tx = (int)p.X, ty = (int)p.Y, tz = Mathf.RoundToInt(p.Z);
                float depth = ctx.World.DepthFor(new DrawItem { Kind = DrawKind.Mobile, X = tx, Y = ty, Z = (sbyte)Mathf.Clamp(tz, -128, 127), PriorityZ = tz + 2 });
                float h = size * s * 0.5f;
                // A leaf turns as it falls: its quad's corners rotate.
                float angle = p.Kind == Kind.Leaf ? t * 2.5f + p.Seed : 0f;
                float ca = Mathf.Cos(angle), sa = Mathf.Sin(angle);
                int n = _vertices.Count;
                for (int k = 0; k < 4; k++)
                {
                    float cx = k == 0 || k == 1 ? -1f : 1f, cy = k == 0 || k == 3 ? -1f : 1f;
                    float rx = cx * ca - cy * sa * 0.6f, ry = cx * sa + cy * ca * 0.6f; // a leaf is longer than it is wide
                    _vertices.Add(new Vector3(u.x + rx * h, u.y + ry * h, depth));
                    _uvs.Add(new Vector2(cx * 0.5f + 0.5f, cy * 0.5f + 0.5f));
                    _kinds.Add(new Vector2((float)p.Kind, fade));
                    _colours.Add(p.Colour);
                }
                _indices.Add(n);
                _indices.Add(n + 1);
                _indices.Add(n + 2);
                _indices.Add(n);
                _indices.Add(n + 2);
                _indices.Add(n + 3);
            }
            _mesh.Clear();
            _mesh.SetVertices(_vertices);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetUVs(1, _kinds);
            _mesh.SetColors(_colours);
            _mesh.SetTriangles(_indices, 0);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 1e6f));
        }

        public void Dispose()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
