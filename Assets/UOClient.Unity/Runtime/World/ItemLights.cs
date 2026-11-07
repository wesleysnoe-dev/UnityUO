using System.Collections.Generic;
using UnityEngine;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// The item, static and multi-part lights near the player, as shader globals (UOItemLights.cginc) for UOClient/WorldSprite
    /// and the TEST Terrain. Each frame the candidates (ground items, multi parts, the loaded map's light statics) are
    /// gathered, the <see cref="Max"/> nearest within <see cref="RangeTiles"/> kept, and each placed at its tile's centre
    /// (TazUO adds a light at the object's draw position + 22, 22) with its light.mul shape's radius and peak and its
    /// LightColors colour. Keyword UO_ITEM_LIGHTS is on only while at least one light shows, so with none (daylight, Night
    /// Sight above the overall level, no light sources) the shaders run exactly their old code.
    /// </summary>
    public sealed class ItemLights
    {
        public const int Max = 16, RangeTiles = 18;
        public const string Keyword = "UO_ITEM_LIGHTS";

        private struct Candidate
        {
            public int X, Y, Z, Shape, Color;
        }

        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<Candidate> _chosen = new List<Candidate>();
        private readonly Vector4[] _positions = new Vector4[Max];
        private readonly Vector4[] _colors = new Vector4[Max];
        private bool _keywordOn;

        private static readonly int LightsId = Shader.PropertyToID("_UOItemLights");
        private static readonly int ColorsId = Shader.PropertyToID("_UOItemLightColors");
        private static readonly int CountId = Shader.PropertyToID("_UOItemLightCount");

        /// <summary>Candidates last frame, lights drawn, and the gather-to-upload cost (ms).</summary>
        public int Candidates { get; private set; }
        public int Count { get; private set; }
        public double LastMs { get; private set; }

        public void Begin() => _candidates.Clear();

        /// <summary>A light source at a tile with its light byte / tiledata Layer (<see cref="LightSources.Resolve"/>).</summary>
        public void Add(int x, int y, int z, int graphic, int id)
        {
            if (LightSources.Resolve(graphic, id) is (int shape, int color))
                _candidates.Add(new Candidate { X = x, Y = y, Z = z, Shape = shape, Color = color });
        }

        private static readonly System.Func<Candidate, (int, int)> At = c => (c.X, c.Y);

        /// <summary>
        /// Picks and uploads the lights. <paramref name="strength"/> is how dark it is (<see cref="Lighting.LocalLightScale"/>,
        /// 0 by day or while the personal light is at the overall level): 0 turns them all off.
        /// </summary>
        public void Apply(IWorldSpace world, LightShapes shapes, int scale, int playerX, int playerY, float strength)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Candidates = _candidates.Count;
            int n = 0;
            if (strength > 0f && world != null)
            {
                LightSources.Nearest(_candidates, At, playerX, playerY, RangeTiles, Max, _chosen);
                foreach (Candidate c in _chosen)
                {
                    IO.Formats.LightShape s = shapes.Get(c.Shape);
                    if (!s.Exists)
                        continue;
                    Vector2 at = world.ToUnity(new Vector2((c.X - c.Y) * 22f, (c.X + c.Y) * 22f + 22f - c.Z * 4f) * scale);
                    (float r, float g, float b) = LightSources.Rgb(c.Color);
                    _positions[n] = new Vector4(at.x, at.y, s.Radius * scale, s.Peak / 31f);
                    _colors[n] = new Vector4(r * strength, g * strength, b * strength, 0);
                    n++;
                }
            }
            Count = n;
            if (n > 0)
            {
                Shader.SetGlobalVectorArray(LightsId, _positions);
                Shader.SetGlobalVectorArray(ColorsId, _colors);
            }
            Shader.SetGlobalFloat(CountId, n);
            SetKeyword(n > 0);
            LastMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>No lights: the keyword goes off (the shaders' old path).</summary>
        public void Clear()
        {
            _candidates.Clear();
            Count = 0;
            Shader.SetGlobalFloat(CountId, 0);
            SetKeyword(false);
        }

        private void SetKeyword(bool on)
        {
            if (on == _keywordOn)
                return;
            _keywordOn = on;
            if (on)
                Shader.EnableKeyword(Keyword);
            else
                Shader.DisableKeyword(Keyword);
        }
    }
}
