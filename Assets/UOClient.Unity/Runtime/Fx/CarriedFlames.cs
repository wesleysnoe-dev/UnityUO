using System.Collections.Generic;
using UnityEngine;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity.Fx
{
    /// <summary>
    /// Living flames on the fire a character carries (a lit torch, lantern or candle in hand): each sits on its item's
    /// fire pixels in the animation frame being drawn, so it moves with the arm as they walk, fight or stand. The fire is
    /// found in the item's own animation frames (the same fire-coloured top band as <see cref="FxCatalog.Spot"/>), per
    /// frame, cached by (animation, action, direction). Fed each frame by EntityViews with the bearers near the player.
    /// </summary>
    public sealed class CarriedFlames
    {
        public const int Max = 16;

        private readonly Dictionary<(uint Bearer, int Graphic), ParticleSystem> _live = new Dictionary<(uint, int), ParticleSystem>();
        private readonly List<(uint Bearer, Avatar Avatar, int Graphic, int Anim)> _sources = new List<(uint, Avatar, int, int)>();
        private readonly HashSet<(uint, int)> _seen = new HashSet<(uint, int)>();
        private readonly List<(uint, int)> _gone = new List<(uint, int)>();
        // Per frame of an item's animation: the fire's middle relative to the feet (art pixels), or null (no fire drawn).
        private readonly Dictionary<(int Anim, int Action, int FileDir), Vector2?[]> _spots = new Dictionary<(int, int, int), Vector2?[]>();
        private Transform _root;

        public void Begin() => _sources.Clear();

        /// <summary>A carried light: its bearer (serial and drawn avatar), the item's graphic and its animation id.</summary>
        public void Add(uint bearer, Avatar avatar, int graphic, int anim)
        {
            if (_sources.Count < Max)
                _sources.Add((bearer, avatar, graphic, anim));
        }

        public void Apply(UOData data, IWorldSpace world, int scale)
        {
            _seen.Clear();
            if (FxCatalog.Enabled && data != null && world != null)
            {
                if (_root == null)
                    _root = new GameObject("FX carried flames").transform;
                foreach (var s in _sources)
                {
                    Avatar a = s.Avatar;
                    FxCatalog.FlameKind kind = FxCatalog.Flame(data, s.Graphic);
                    if (kind == FxCatalog.FlameKind.None || a.DrawnAction < 0 || a.DrawnSeated)
                        continue;
                    Vector2? spot = Spot(data, s.Anim, a.DrawnAction, a.DrawnFileDir, a.DrawnFrame);
                    if (spot == null)
                        continue; // this frame shows no fire (the torch behind the body, or none in the art)
                    var key = (s.Bearer, s.Graphic);
                    _seen.Add(key);
                    if (!_live.TryGetValue(key, out ParticleSystem ps) || ps == null)
                    {
                        float height = (kind == FxCatalog.FlameKind.Candle ? 5f : 9f) * scale;
                        float width = (kind == FxCatalog.FlameKind.Candle ? 2f : 4f) * scale;
                        _live[key] = ps = FxParticles.Flame(_root, width, height, scale, false);
                    }
                    Vector2 off = spot.Value;
                    if (a.DrawnMirror)
                        off.x = -off.x; // a mirrored frame flips about the feet
                    Vector2 at = world.ToUnity(a.AnchorPixels + off * scale);
                    ps.transform.position = new Vector3(at.x, at.y, a.LastDepth - 0.01f);
                }
            }
            _gone.Clear();
            foreach (var kv in _live)
                if (!_seen.Contains(kv.Key))
                    _gone.Add(kv.Key);
            foreach (var k in _gone)
            {
                FxParticles.Release(_live[k]);
                _live.Remove(k);
            }
        }

        /// <summary>The fire in frame <paramref name="frame"/> of an item's animation, relative to the feet; null: none.</summary>
        private Vector2? Spot(UOData data, int anim, int action, int fileDir, int frame)
        {
            if (!_spots.TryGetValue((anim, action, fileDir), out Vector2?[] spots))
            {
                AnimationFrame[] frames = data.AnimationLibrary.Read(anim, action, fileDir);
                spots = new Vector2?[frames.Length];
                for (int i = 0; i < frames.Length; i++)
                    spots[i] = FireIn(frames[i]);
                _spots[(anim, action, fileDir)] = spots;
            }
            return spots.Length == 0 ? null : spots[frame % spots.Length];
        }

        private const int FireBand = 6;

        /// <summary>The middle of a frame's topmost band of fire-coloured pixels, relative to the feet; null: none.</summary>
        private static Vector2? FireIn(AnimationFrame f)
        {
            Bitmap32 img = f.Image;
            if (img == null || img.Width == 0)
                return null;
            static bool Fiery(uint p)
            {
                int r = (int)(p >> 16) & 0xFF, g = (int)(p >> 8) & 0xFF, b = (int)p & 0xFF;
                return (p >> 24) != 0 && r > 170 && r >= g + 30 && g >= b && g > 60;
            }
            int top = -1;
            for (int y = 0; y < img.Height && top < 0; y++)
                for (int x = 0; x < img.Width; x++)
                    if (Fiery(img.Pixels[y * img.Width + x]))
                    {
                        top = y;
                        break;
                    }
            if (top < 0)
                return null;
            double sx = 0, sy = 0;
            int n = 0;
            for (int y = top; y < Mathf.Min(img.Height, top + FireBand); y++)
                for (int x = 0; x < img.Width; x++)
                    if (Fiery(img.Pixels[y * img.Width + x]))
                    {
                        sx += x;
                        sy += y;
                        n++;
                    }
            // The image's top-left sits at (-CenterX, -(CenterY + height)) from the feet (MobileArt.BuildFrames).
            return new Vector2(-f.CenterX + (float)(sx / n), -(f.CenterY + img.Height) + (float)(sy / n));
        }

        public void Clear()
        {
            foreach (ParticleSystem ps in _live.Values)
                FxParticles.Release(ps);
            _live.Clear();
        }
    }
}
