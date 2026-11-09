using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UOClient.IO;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// TEST-only sprite sun-shadow receive (<see cref="SpriteShadow"/>): one world-wrapped RHalf texture holding every
    /// attached terrain chunk's <see cref="LiveGroundLighting.ShadowCeiling"/>, ¼ tile per texel, tile (x, y) at texel
    /// (4·x, 4·y) mod <see cref="Size"/> (Repeat wrap, bilinear). Chunks write their block when attached and clear it
    /// when disposed; ground that is not loaded reads <see cref="LiveGroundLighting.NoCeiling"/> (never shadowed).
    ///
    /// On top of the chunks' own ceilings (<see cref="ShadowCeilingCore"/>, Unity-free and tested): the cross-chunk
    /// margin from loaded west/north neighbours, and mobiles and items casting as columns (a dynamic moving
    /// approximation, not a Unity shadow map). Mobiles and items report themselves through
    /// <see cref="MeshBuilder.ShadowCasters"/> while they are drawn; one not reported for a frame is dropped. Only the
    /// chunks a change touches are recomposed and uploaded (per-chunk <see cref="Graphics.CopyTexture(Texture, int, int, int, int, int, int, Texture, int, int, int, int)"/>
    /// when the GPU can, else one full upload).
    ///
    /// While it lives it turns on the WorldSprite keyword <see cref="Keyword"/>, sets the shader globals,
    /// <see cref="MeshBuilder.FootMetadata"/> and <see cref="MeshBuilder.ShadowCasters"/>; <see cref="Dispose"/> puts
    /// all of them back. Nothing persists: no QualitySettings, ProjectSettings or material changes.
    /// </summary>
    internal sealed class SpriteShadowCeiling : IDisposable, IShadowCasterSink
    {
        public const string Keyword = "LG_SPRITE_SHADOW";
        public const int Size = SpriteShadow.WrapTiles * LiveGroundLighting.ShadowTexelsPerTile; // 1024
        private const int Block = LiveGroundLighting.ShadowSize;                                 // 64: one chunk

        /// <summary>More changed chunks than this in one frame are sent as one full upload instead of per-chunk copies.</summary>
        public const int MaxPartialUploads = 8;

        private static readonly int CeilId = Shader.PropertyToID("_LGShadowCeil");
        private static readonly int MapId = Shader.PropertyToID("_LGSpriteShadowMap");
        private static readonly int CeilOnId = Shader.PropertyToID("_LGCeilOn");
        private static readonly ushort Empty = Mathf.FloatToHalf(LiveGroundLighting.NoCeiling);

        private readonly ushort[] _texels = new ushort[Size * Size];
        private readonly Dictionary<(int, int), object> _owners = new Dictionary<(int, int), object>();
        private readonly ShadowCeilingCore _core = new ShadowCeilingCore();
        private readonly List<ShadowCeilingCore.Chunk> _changed = new List<ShadowCeilingCore.Chunk>();
        private readonly List<(int, int)> _uploads = new List<(int, int)>();
        private readonly HashSet<(int, int)> _uploadSet = new HashSet<(int, int)>();
        private readonly Dictionary<int, int> _itemHeights = new Dictionary<int, int>();
        private readonly Texture2D[] _staging = new Texture2D[MaxPartialUploads];
        private readonly ushort[] _block = new ushort[Block * Block];
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly WorldStreamer _world;
        private readonly int _scale;
        private readonly bool _keywordWas, _footWas, _partial;
        private readonly IShadowCasterSink _castersWas;
        private Texture2D _texture;
        private bool _fullUpload;

        private SpriteShadowCeiling(WorldStreamer world, int scale)
        {
            _world = world;
            _scale = scale;
            for (int i = 0; i < _texels.Length; i++)
                _texels[i] = Empty;
            _texture = new Texture2D(Size, Size, TextureFormat.RHalf, false, true)
            {
                name = "LiveGround sprite shadow ceiling (TEST)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            _partial = (SystemInfo.copyTextureSupport & UnityEngine.Rendering.CopyTextureSupport.Basic) != 0;
            _fullUpload = true;
            Upload();

            Vector2 t = world.ToUnity(Vector2.zero); // = (-origin x, origin y)
            Shader.SetGlobalTexture(CeilId, _texture);
            Shader.SetGlobalVector(MapId, new Vector4(-t.x, t.y, scale, 1f / SpriteShadow.WrapTiles));
            Shader.SetGlobalFloat(CeilOnId, 1f);
            _keywordWas = Shader.IsKeywordEnabled(Keyword);
            _footWas = MeshBuilder.FootMetadata;
            _castersWas = MeshBuilder.ShadowCasters;
            Shader.EnableKeyword(Keyword);
            MeshBuilder.FootMetadata = true;
            MeshBuilder.ShadowCasters = this;
        }

        /// <summary>Null (and why) when this GPU cannot filter an RHalf texture; sprites then stay unshadowed.</summary>
        public static SpriteShadowCeiling TryCreate(WorldStreamer world, int scale, out string error)
        {
            error = null;
            try
            {
                if (!SystemInfo.SupportsTextureFormat(TextureFormat.RHalf))
                {
                    error = "RHalf textures not supported";
                    return null;
                }
                return new SpriteShadowCeiling(world, scale);
            }
            catch (Exception e)
            {
                error = $"{e.GetType().Name}: {e.Message}";
                return null;
            }
        }

        /// <summary>Chunks whose ceiling is in the texture right now (main thread).</summary>
        public int ChunkCount => _owners.Count;

        /// <summary>Attaches that overwrote a still-owned block (the loaded window grew past <see cref="SpriteShadow.WrapTiles"/>).</summary>
        public int Collisions { get; private set; }

        /// <summary>The Unity-free state (casters, margins, last frame's counts) for the indicator and tests.</summary>
        public ShadowCeilingCore Core => _core;

        /// <summary>Main-thread CPU time of the last <see cref="Apply"/> (recompose + upload calls), ms; and its running mean.</summary>
        public double LastApplyMs { get; private set; }
        public double MeanApplyMs { get; private set; }

        /// <summary>Last <see cref="Apply"/>: blocks uploaded, and whether it was one full upload.</summary>
        public int LastUploads { get; private set; }
        public bool LastFull { get; private set; }

        private static (int, int) Slot(int x0, int y0) =>
            (Mod(x0, SpriteShadow.WrapTiles) / TerrainLandBuilder.Tiles, Mod(y0, SpriteShadow.WrapTiles) / TerrainLandBuilder.Tiles);

        private static int Mod(int v, int m) => ((v % m) + m) % m;

        /// <summary>
        /// Writes chunk (x0, y0)'s ceilings (<see cref="LiveGroundLighting.ShadowCeiling"/> layout) for
        /// <paramref name="owner"/>, and keeps its heights and columns for its neighbours' margins.
        /// </summary>
        public void Publish(int x0, int y0, float[] ceiling, int[,] cornerZ, int[,] occluderTop, object owner)
        {
            if (ceiling == null || ceiling.Length != Block * Block || _texture == null)
                return;
            (int, int) slot = Slot(x0, y0);
            if (_owners.TryGetValue(slot, out object was) && !ReferenceEquals(was, owner))
                Collisions++;
            _owners[slot] = owner;
            ShadowCeilingCore.Chunk c = _core.Add(x0, y0, ceiling, cornerZ, occluderTop, owner);
            WriteBlock(slot, c.Base); // own ceiling now; margin and casters on the next Apply
        }

        /// <summary>Clears chunk (x0, y0)'s block if <paramref name="owner"/> still owns it.</summary>
        public void Remove(int x0, int y0, object owner)
        {
            _core.Remove(x0, y0, owner);
            (int, int) slot = Slot(x0, y0);
            if (!_owners.TryGetValue(slot, out object was) || !ReferenceEquals(was, owner))
                return;
            _owners.Remove(slot);
            WriteBlock(slot, null);
        }

        private void WriteBlock((int, int) slot, float[] ceiling)
        {
            int c0 = slot.Item1 * Block, r0 = slot.Item2 * Block;
            for (int r = 0; r < Block; r++)
            {
                int dst = (r0 + r) * Size + c0, src = r * Block;
                for (int c = 0; c < Block; c++)
                    _texels[dst + c] = ceiling == null ? Empty : Mathf.FloatToHalf(ceiling[src + c]);
            }
            if (_uploadSet.Add(slot))
                _uploads.Add(slot);
        }

        public void Mobile(object key, Vector2 anchorPixels, float footZ)
        {
            // Characters cast their own frame (CharacterShadow), not a tile-wide column (that read as a blob off the feet).
            if (CharacterShadow.Enabled)
                return;
            (int x, int y) = DynamicShadow.FootTile(anchorPixels.x, anchorPixels.y, footZ, _scale);
            _core.Report(key, x, y, Mathf.RoundToInt(footZ), DynamicShadow.MobileHeight, mobile: true);
        }

        public void Item(object key, int x, int y, int z, int graphic)
        {
            if (!_itemHeights.TryGetValue(graphic, out int height))
                _itemHeights[graphic] = height = ItemHeight(graphic);
            _core.Report(key, x, y, z, height);
        }

        /// <summary>An item casts as a map static would (<see cref="StaticOccluders.Casts"/>): its tiledata height, else 0.</summary>
        private int ItemHeight(int graphic)
        {
            try
            {
                UOData data = _world?.Data;
                if (data?.TileData == null)
                    return 0;
                var td = data.TileData.GetStatic(graphic);
                return StaticOccluders.Casts(td) ? td.Height : 0;
            }
            catch (Exception)
            {
                return 0; // unknown graphic: tracked, casts nothing
            }
        }

        /// <summary>
        /// Once per frame (main thread, before the cameras): drops casters that were not drawn, computes queued margins,
        /// recomposes the chunks a change touched and uploads only those blocks.
        /// </summary>
        public void Apply()
        {
            if (_texture == null)
                return;
            _watch.Restart();
            _changed.Clear();
            _core.Frame(_changed);
            foreach (ShadowCeilingCore.Chunk c in _changed)
            {
                (int, int) slot = Slot(c.X0, c.Y0);
                if (_owners.TryGetValue(slot, out object o) && ReferenceEquals(o, c.Owner))
                    WriteBlock(slot, c.Composite);
            }
            Upload();
            _watch.Stop();
            LastApplyMs = _watch.Elapsed.TotalMilliseconds;
            MeanApplyMs = MeanApplyMs <= 0 ? LastApplyMs : MeanApplyMs * 0.95 + LastApplyMs * 0.05;
        }

        private void Upload()
        {
            LastUploads = 0;
            LastFull = false;
            if (_uploads.Count == 0 && !_fullUpload)
                return;
            if (_fullUpload || !_partial || _uploads.Count > MaxPartialUploads)
            {
                _texture.SetPixelData(_texels, 0);
                _texture.Apply(false, false);
                LastFull = true;
                LastUploads = _uploads.Count;
            }
            else
            {
                for (int k = 0; k < _uploads.Count; k++)
                {
                    (int sx, int sy) = _uploads[k];
                    int c0 = sx * Block, r0 = sy * Block;
                    for (int r = 0; r < Block; r++)
                        Array.Copy(_texels, (r0 + r) * Size + c0, _block, r * Block, Block);
                    Texture2D s = _staging[k] ??= new Texture2D(Block, Block, TextureFormat.RHalf, false, true)
                    {
                        name = "LiveGround sprite shadow staging (TEST)",
                        hideFlags = HideFlags.DontSave
                    };
                    s.SetPixelData(_block, 0);
                    s.Apply(false, false);
                    Graphics.CopyTexture(s, 0, 0, 0, 0, Block, Block, _texture, 0, 0, c0, r0);
                }
                LastUploads = _uploads.Count;
            }
            _fullUpload = false;
            _uploads.Clear();
            _uploadSet.Clear();
        }

        public void Dispose()
        {
            if (_texture == null)
                return;
            if (!_keywordWas)
                Shader.DisableKeyword(Keyword);
            MeshBuilder.FootMetadata = _footWas;
            if (ReferenceEquals(MeshBuilder.ShadowCasters, this))
                MeshBuilder.ShadowCasters = _castersWas;
            Shader.SetGlobalTexture(CeilId, Texture2D.whiteTexture);
            Shader.SetGlobalFloat(CeilOnId, 0f);
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
            for (int k = 0; k < _staging.Length; k++)
            {
                if (_staging[k] != null)
                    UnityEngine.Object.Destroy(_staging[k]);
                _staging[k] = null;
            }
            _owners.Clear();
            _core.Clear(); // drops every caster reference (avatars and items of the world being left)
            _itemHeights.Clear();
            _uploads.Clear();
            _uploadSet.Clear();
        }
    }
}
