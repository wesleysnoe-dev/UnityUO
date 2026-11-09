using System.Collections.Generic;
using UnityEngine;
using UOClient.ArtPipeline;
using UOClient.IO;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Living flames on the fire art near the player (torches, candles, braziers, campfires, fireplaces, fire fields):
    /// each gets a particle flame on its art's own fire pixels (<see cref="FxCatalog.Spot"/>), drawn just in front of it.
    /// The sources are the light sources ItemLights gathers each frame; the nearest <see cref="Max"/> within
    /// <see cref="RangeTiles"/> are kept, the rest released.
    /// </summary>
    public sealed class FlameFx
    {
        public const int Max = 40, RangeTiles = 15;

        private struct Source
        {
            public int X, Y, Z, Graphic;
            public long Key => ((long)(X & 0xFFFF) << 40) | ((long)(Y & 0xFFFF) << 24) | ((long)(Z & 0xFF) << 16) | (long)(Graphic & 0xFFFF);
        }

        private readonly Dictionary<long, ParticleSystem> _live = new Dictionary<long, ParticleSystem>();
        private readonly List<Source> _sources = new List<Source>();
        private readonly HashSet<long> _seen = new HashSet<long>();
        private readonly List<long> _gone = new List<long>();
        private Transform _root;

        public int Count => _live.Count;

        /// <summary>Every live flame's position (Unity, its depth just in front of its fire art): the glow halos (AmbientLife).</summary>
        public void ForEachFlame(System.Action<Vector3, int, int, int> f)
        {
            foreach (KeyValuePair<long, ParticleSystem> kv in _live)
                if (kv.Value != null)
                    f(kv.Value.transform.position, (int)((kv.Key >> 40) & 0xFFFF), (int)((kv.Key >> 24) & 0xFFFF), (sbyte)((kv.Key >> 16) & 0xFF));
        }

        public void Begin() => _sources.Clear();

        public void Add(int x, int y, int z, int graphic)
        {
            _sources.Add(new Source { X = x, Y = y, Z = z, Graphic = graphic });
        }

        public void Apply(UOData data, IWorldSpace world, int scale, int playerX, int playerY)
        {
            _seen.Clear();
            if (FxCatalog.Enabled && world != null && data != null)
            {
                if (_root == null)
                    _root = new GameObject("FX flames").transform;
                _sources.Sort((a, b) => Dist(a, playerX, playerY).CompareTo(Dist(b, playerX, playerY)));
                int kept = 0;
                foreach (Source s in _sources)
                {
                    if (kept >= Max || Dist(s, playerX, playerY) > RangeTiles)
                        break;
                    FxCatalog.FlameKind kind = FxCatalog.Flame(data, s.Graphic);
                    if (kind == FxCatalog.FlameKind.None || !_seen.Add(s.Key))
                        continue;
                    kept++;
                    if (!_live.TryGetValue(s.Key, out ParticleSystem ps) || ps == null)
                    {
                        ps = Make(data, world, scale, s, kind);
                        if (ps == null)
                            continue;
                        _live[s.Key] = ps;
                    }
                    else
                        Place(ps.transform, data, world, scale, s); // the world origin moves as the player walks
                }
            }
            _gone.Clear();
            foreach (var kv in _live)
                if (!_seen.Contains(kv.Key))
                    _gone.Add(kv.Key);
            foreach (long k in _gone)
            {
                FxParticles.Release(_live[k]);
                _live.Remove(k);
            }
        }

        private static int Dist(Source s, int px, int py) => Mathf.Max(Mathf.Abs(s.X - px), Mathf.Abs(s.Y - py));

        private ParticleSystem Make(UOData data, IWorldSpace world, int scale, Source s, FxCatalog.FlameKind kind)
        {
            FxCatalog.FireSpot spot = FxCatalog.Spot(data, s.Graphic);
            if (spot.Width <= 0)
                return null;
            float height = kind switch
            {
                FxCatalog.FlameKind.Candle => 5f,
                FxCatalog.FlameKind.Torch => 10f,
                FxCatalog.FlameKind.Brazier => 16f,
                FxCatalog.FlameKind.Hearth => 16f,
                FxCatalog.FlameKind.Campfire => 22f,
                FxCatalog.FlameKind.Field => 22f,
                _ => 10f
            } * scale;
            float width = Mathf.Clamp(spot.Width, kind == FxCatalog.FlameKind.Candle ? 2f : 4f, 40f) * scale;
            bool embers = kind is FxCatalog.FlameKind.Campfire or FxCatalog.FlameKind.Brazier or FxCatalog.FlameKind.Field
                          or FxCatalog.FlameKind.Hearth;
            ParticleSystem ps = FxParticles.Flame(_root, width, height, scale, embers);
            Place(ps.transform, data, world, scale, s);
            return ps;
        }

        private static void Place(Transform t, UOData data, IWorldSpace world, int scale, Source s)
        {
            var art = data.GetStatic(s.Graphic);
            if (art == null)
                return;
            FxCatalog.FireSpot spot = FxCatalog.Spot(data, s.Graphic);
            var it = new DrawItem { Kind = DrawKind.Static, Graphic = (ushort)s.Graphic, X = s.X, Y = s.Y, Z = (sbyte)s.Z };
            var (left, top, _, _) = IsoCompositor.PlacementRect(it, scale, art.Width, art.Height);
            // the flame rises from the lower part of the fire pixels
            Vector2 at = world.ToUnity(new Vector2(left + spot.X * scale, top + (spot.Y + 2f) * scale));
            t.position = new Vector3(at.x, at.y, world.DepthFor(it) - 1f);
        }

        public void Clear()
        {
            foreach (ParticleSystem ps in _live.Values)
                FxParticles.Release(ps);
            _live.Clear();
        }
    }
}
