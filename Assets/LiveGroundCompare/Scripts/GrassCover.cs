using System;
using UnityEngine;
using UOClient.IO.Formats;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>
    /// The tiles of house and boat parts (server multis) where the grass must not stand: a world-wrapped R8 mask
    /// (<see cref="Wrap"/> tiles a side, one texel a tile) the grass shader reads as <c>_LGGrassCover</c>. Map statics are
    /// handled on the worker when the chunk is built (GrassBuilder.CoverFromMap); houses come from the server later, so
    /// they are written here as they arrive and go (Walkability.PartsChanged), with no chunk rebuilt.
    /// </summary>
    public sealed class GrassCover : IDisposable
    {
        /// <summary>Tiles a side; more than the widest loaded area (twice the 96-tile load radius), so nothing on screen aliases.</summary>
        public const int Wrap = 256;

        private static readonly int CoverId = Shader.PropertyToID("_LGGrassCover"), OnId = Shader.PropertyToID("_LGGrassCoverOn");

        private readonly Texture2D _texture;
        private readonly byte[] _pixels = new byte[Wrap * Wrap];
        private bool _dirty = true;

        public GrassCover()
        {
            _texture = new Texture2D(Wrap, Wrap, TextureFormat.R8, false, true)
            {
                name = "LiveGround grass cover",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat
            };
            Shader.SetGlobalTexture(CoverId, _texture);
            Shader.SetGlobalFloat(OnId, 1f);
            Walkability.PartsChanged += OnParts;
        }

        /// <summary>Re-reads the tiles a house's parts came to or left (main thread).</summary>
        private void OnParts(Walkability walk, int x0, int y0, int x1, int y1)
        {
            if (walk == null || x1 - x0 > Wrap || y1 - y0 > Wrap)
                return;
            TileData tileData = walk.TileData;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int landZ = walk.LandZ(x, y);
                    bool covered = false;
                    foreach (StaticCell c in walk.StaticsOn(x, y))
                        if (GrassBuilder.Covers(tileData.GetStatic(c.Graphic), c.Z, landZ))
                        {
                            covered = true;
                            break;
                        }
                    int i = ((y % Wrap + Wrap) % Wrap) * Wrap + (x % Wrap + Wrap) % Wrap;
                    byte v = covered ? (byte)255 : (byte)0;
                    if (_pixels[i] != v)
                    {
                        _pixels[i] = v;
                        _dirty = true;
                    }
                }
        }

        /// <summary>Uploads the mask if a house came or went (once a frame at most).</summary>
        public void Apply()
        {
            if (!_dirty || _texture == null)
                return;
            _dirty = false;
            _texture.SetPixelData(_pixels, 0);
            _texture.Apply(false);
        }

        public void Dispose()
        {
            Walkability.PartsChanged -= OnParts;
            Shader.SetGlobalFloat(OnId, 0f);
            if (_texture != null)
                UnityEngine.Object.Destroy(_texture);
        }
    }
}
