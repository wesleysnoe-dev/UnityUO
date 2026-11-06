using System;
using System.Collections.Generic;
using UnityEngine;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// TEST-only sprite sun-shadow receive (<see cref="SpriteShadow"/>): one world-wrapped RHalf texture holding every
    /// attached terrain chunk's <see cref="LiveGroundLighting.ShadowCeiling"/>, ¼ tile per texel, tile (x, y) at texel
    /// (4·x, 4·y) mod <see cref="Size"/> (Repeat wrap, bilinear). Chunks write their block when attached and clear it
    /// when disposed; ground that is not loaded reads <see cref="LiveGroundLighting.NoCeiling"/> (never shadowed).
    ///
    /// While it lives it turns on the WorldSprite keyword <see cref="Keyword"/>, sets the shader globals and
    /// <see cref="MeshBuilder.FootMetadata"/>; <see cref="Dispose"/> puts all three back. Nothing persists: no
    /// QualitySettings, ProjectSettings or material changes.
    /// </summary>
    internal sealed class SpriteShadowCeiling : IDisposable
    {
        public const string Keyword = "LG_SPRITE_SHADOW";
        public const int Size = SpriteShadow.WrapTiles * LiveGroundLighting.ShadowTexelsPerTile; // 1024
        private const int Block = LiveGroundLighting.ShadowSize;                                 // 64: one chunk

        private static readonly int CeilId = Shader.PropertyToID("_LGShadowCeil");
        private static readonly int MapId = Shader.PropertyToID("_LGSpriteShadowMap");
        private static readonly ushort Empty = Mathf.FloatToHalf(LiveGroundLighting.NoCeiling);

        private readonly ushort[] _texels = new ushort[Size * Size];
        private readonly Dictionary<(int, int), object> _owners = new Dictionary<(int, int), object>();
        private readonly bool _keywordWas, _footWas;
        private Texture2D _texture;
        private bool _dirty;

        private SpriteShadowCeiling(WorldStreamer world, int scale)
        {
            for (int i = 0; i < _texels.Length; i++)
                _texels[i] = Empty;
            _texture = new Texture2D(Size, Size, TextureFormat.RHalf, false, true)
            {
                name = "LiveGround sprite shadow ceiling (TEST)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            _dirty = true;
            Apply();

            Vector2 t = world.ToUnity(Vector2.zero); // = (-origin x, origin y)
            Shader.SetGlobalTexture(CeilId, _texture);
            Shader.SetGlobalVector(MapId, new Vector4(-t.x, t.y, scale, 1f / SpriteShadow.WrapTiles));
            _keywordWas = Shader.IsKeywordEnabled(Keyword);
            _footWas = MeshBuilder.FootMetadata;
            Shader.EnableKeyword(Keyword);
            MeshBuilder.FootMetadata = true;
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

        private static (int, int) Slot(int x0, int y0) =>
            (Mod(x0, SpriteShadow.WrapTiles) / TerrainLandBuilder.Tiles, Mod(y0, SpriteShadow.WrapTiles) / TerrainLandBuilder.Tiles);

        private static int Mod(int v, int m) => ((v % m) + m) % m;

        /// <summary>Writes chunk (x0, y0)'s ceilings (<see cref="LiveGroundLighting.ShadowCeiling"/> layout) for <paramref name="owner"/>.</summary>
        public void Publish(int x0, int y0, float[] ceiling, object owner)
        {
            if (ceiling == null || ceiling.Length != Block * Block || _texture == null)
                return;
            (int, int) slot = Slot(x0, y0);
            if (_owners.TryGetValue(slot, out object was) && !ReferenceEquals(was, owner))
                Collisions++;
            _owners[slot] = owner;
            int c0 = slot.Item1 * Block, r0 = slot.Item2 * Block;
            for (int r = 0; r < Block; r++)
            {
                int dst = (r0 + r) * Size + c0, src = r * Block;
                for (int c = 0; c < Block; c++)
                    _texels[dst + c] = Mathf.FloatToHalf(ceiling[src + c]);
            }
            _dirty = true;
        }

        /// <summary>Clears chunk (x0, y0)'s block if <paramref name="owner"/> still owns it.</summary>
        public void Remove(int x0, int y0, object owner)
        {
            (int, int) slot = Slot(x0, y0);
            if (!_owners.TryGetValue(slot, out object was) || !ReferenceEquals(was, owner))
                return;
            _owners.Remove(slot);
            int c0 = slot.Item1 * Block, r0 = slot.Item2 * Block;
            for (int r = 0; r < Block; r++)
                for (int c = 0; c < Block; c++)
                    _texels[(r0 + r) * Size + c0 + c] = Empty;
            _dirty = true;
        }

        /// <summary>Once per frame (main thread, before the cameras): uploads the texture if a chunk changed it.</summary>
        public void Apply()
        {
            if (!_dirty || _texture == null)
                return;
            _dirty = false;
            _texture.SetPixelData(_texels, 0);
            _texture.Apply(false, false);
        }

        public void Dispose()
        {
            if (_texture == null)
                return;
            if (!_keywordWas)
                Shader.DisableKeyword(Keyword);
            MeshBuilder.FootMetadata = _footWas;
            Shader.SetGlobalTexture(CeilId, Texture2D.whiteTexture);
            UnityEngine.Object.Destroy(_texture);
            _texture = null;
            _owners.Clear();
        }
    }
}
