using System;
using UnityEngine;
using UOClient.ArtPipeline;
using UOClient.Core;
using UOClient.IO;
using UOClient.IO.Formats;

namespace UOClient.Unity
{
    /// <summary>
    /// One running graphic effect: a missile flying from source to target, or an animation playing on a
    /// mobile or a spot. Frames come from animdata.mul (base graphic + per-frame offsets), drawn as item
    /// sprites above whatever stands on the tile. Lightning is not a sprite; the HUD draws its gump frames.
    /// </summary>
    public sealed class EffectView : IDisposable
    {
        private const float FrameStep = 0.05f; // the classic client's effect frame delay
        private const int FlyHeight = 10;      // missiles fly at about chest height

        private readonly GraphicEffect _e;
        private readonly MobileArt _art;
        private readonly int _scale;
        private readonly AnimDataEntry _anim;
        private readonly Func<uint, Vector3Int?> _locate;
        private readonly float _born, _life;
        private readonly GameObject _go;
        private readonly Mesh _mesh;
        private readonly Material _material;
        private readonly MeshRenderer _renderer;
        private Vector3Int _from, _to;
        private readonly Fx.FxCatalog.EffectStyle _style;
        private ParticleSystem _trail;
        private bool _burst;
        private bool _motes;

        public EffectView(UOData data, MobileArt art, Transform parent, Shader shader, int scale, GraphicEffect e, Func<uint, Vector3Int?> locate)
        {
            _e = e;
            _art = art;
            _scale = scale;
            _locate = locate;
            _anim = data.AnimData.Get(e.Graphic);
            _born = Time.realtimeSinceStartup;
            _from = new Vector3Int(e.SourceX, e.SourceY, e.SourceZ);
            _to = new Vector3Int(e.TargetX, e.TargetY, e.TargetZ);
            Track();

            float frameTime = FrameTime;
            float once = Mathf.Max(1, _anim.Offsets?.Length ?? 1) * frameTime;
            if (e.Type == GraphicEffect.Moving)
            {
                float tiles = Mathf.Max(Mathf.Abs(_to.x - _from.x), Mathf.Abs(_to.y - _from.y));
                _life = Mathf.Clamp(tiles * 0.06f, 0.15f, 1.5f);
            }
            else if (e.Type == GraphicEffect.Lightning)
                _life = 10 * FrameStep;
            else
                _life = e.Duration > 0 ? Mathf.Max(once, e.Duration * FrameStep) : once;

            _go = new GameObject($"Effect 0x{e.Graphic:X4}");
            _go.transform.SetParent(parent, false);
            _mesh = new Mesh();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _material = new Material(shader);
            _style = Fx.FxCatalog.Effect(e.Graphic);
            if (_style.Glow)
            {
                // Spell light is light: added onto the scene, not lit by it (a fireball at night stays bright).
                _material.SetFloat("_Unlit", 1f);
                _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _material.renderQueue = 3050;
            }
            else if (_style.Unlit) // explosions: full colour at night, but solid (additive art this big blows out to white)
                _material.SetFloat("_Unlit", 1f);
            _renderer = _go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.enabled = false;
        }

        public GraphicEffect Effect => _e;
        public bool Done => Time.realtimeSinceStartup - _born >= _life;
        public bool IsLightning => _e.Type == GraphicEffect.Lightning;

        /// <summary>For lightning: 0..9, the gump frame to draw now.</summary>
        public int LightningFrame => Mathf.Clamp((int)((Time.realtimeSinceStartup - _born) / FrameStep), 0, 9);

        /// <summary>Where the effect is now, in tiles (fractional while flying).</summary>
        public Vector3 Position
        {
            get
            {
                if (_e.Type == GraphicEffect.Moving)
                {
                    float t = Mathf.Clamp01((Time.realtimeSinceStartup - _born) / _life);
                    return Vector3.Lerp(_from, _to, t) + new Vector3(0, 0, FlyHeight);
                }
                return _e.Type == GraphicEffect.AtLocation ? _from : (Vector3)_to;
            }
        }

        private float FrameTime => Mathf.Max(1, _anim.Interval) * FrameStep;

        /// <summary>Follows the mobiles involved, so a missile hits a target that moved and an aura moves with its wearer.</summary>
        private void Track()
        {
            if (_e.Source != 0 && _locate(_e.Source) is Vector3Int s)
                _from = s;
            if (_e.Target != 0 && _locate(_e.Target) is Vector3Int t)
                _to = t;
            else if (_e.Type == GraphicEffect.OnMobile && _e.Source != 0 && _locate(_e.Source) is Vector3Int s2)
                _to = s2; // fixed effects name their mobile as the source
        }

        public void Render(IWorldSpace world)
        {
            if (IsLightning || Done)
            {
                _renderer.enabled = false;
                return;
            }
            Track();
            int frame = _anim.IsAnimated ? (int)((Time.realtimeSinceStartup - _born) / FrameTime) % _anim.Offsets.Length : 0;
            int graphic = _e.Graphic + (_anim.IsAnimated ? _anim.Offsets[frame] : 0);
            ItemSprite sprite = _art.GetItem(graphic, _e.Hue);
            if (sprite == null)
                return; // still building (or no art); try again next frame

            Vector3 p = Position;
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y);
            DrawItem At(int x, int y) => new DrawItem
            {
                Kind = DrawKind.Static, Graphic = (ushort)graphic, X = x, Y = y,
                Z = (sbyte)Mathf.Clamp(Mathf.RoundToInt(p.z), -128, 127), PriorityZ = Mathf.RoundToInt(p.z) + 30
            };
            // Place on the tile under it, shifted by the fraction of a tile travelled (iso: x right-down, y left-down).
            DrawItem item = At(x0, y0);
            var (left, top, w, h) = IsoCompositor.PlacementRect(item, _scale, sprite.OriginalWidth, sprite.OriginalHeight);
            float fx = p.x - x0, fy = p.y - y0;
            float half = 22f * _scale;
            Rect r = world.ToUnityRect(left + (fx - fy) * half, top + (fx + fy) * half, w, h);
            _material.mainTexture = _art.Atlas.Page(sprite.Slot.Page);
            float depth = world.DepthFor(At(x0 + 1, y0 + 1));
            MeshBuilder.SetQuad(_mesh, r, _art.Atlas.Uv(sprite.Slot), depth, false);
            _renderer.enabled = true;

            // FX: a trail behind missiles, a burst when an explosion starts, and the effect's own light.
            // just in front of the effect's own art (as an outline is), so whatever hides the art hides its FX too
            var centre = new Vector3(r.center.x, r.center.y, depth - DepthKey.Step * 0.2f);
            if (_style.Trail)
            {
                if (_trail == null)
                    _trail = Fx.FxParticles.Trail(centre, _style.Light, _scale);
                _trail.transform.position = centre;
            }
            if (_style.Burst && !_burst)
            {
                _burst = true;
                Fx.FxParticles.Burst(centre, _style.Light, _scale);
            }
            if (_style.Motes.a > 0f && !_motes)
            {
                _motes = true;
                Fx.FxParticles.Motes(centre, _style.Motes, _scale);
            }
            if (_style.Radius > 0f)
            {
                float age = Time.realtimeSinceStartup - _born;
                float fade = _style.Burst ? Mathf.Clamp01(1f - age / Mathf.Max(0.2f, _life)) : 1f;
                ItemLights.Glows.Add((Position, _style.Light * (0.6f + 0.6f * fade), _style.Radius * (0.6f + 0.4f * fade)));
            }
        }

        public void Dispose()
        {
            Fx.FxParticles.Release(_trail);
            UnityEngine.Object.Destroy(_material);
            UnityEngine.Object.Destroy(_mesh);
            UnityEngine.Object.Destroy(_go);
        }
    }
}
