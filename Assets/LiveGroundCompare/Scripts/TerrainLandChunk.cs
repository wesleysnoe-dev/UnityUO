using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UOClient.Unity;

namespace UOClient.LiveGroundCompare
{
    /// <summary>Worker-side: turns a chunk's diverted land into <see cref="TerrainLandData"/> (no Unity API).</summary>
    internal sealed class TerrainLandEmitter : ILandEmitter
    {
        private readonly TerrainIsoPresenter _presenter;
        private readonly int _texels;
        private readonly bool[] _water; // null: water off

        public TerrainLandEmitter(TerrainIsoPresenter presenter, int scale)
        {
            _presenter = presenter;
            _texels = TerrainLandBuilder.TexelsFor(scale);
            _water = presenter.Water?.Table;
        }

        public bool Diverts(in ArtPipeline.DrawItem item, bool baked) => GroundBackend.Diverts(GroundBackendKind.Terrain, item, baked);

        public ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land)
        {
            try
            {
                int x0 = chunkX * WorldStreamer.ChunkSize, y0 = chunkY * WorldStreamer.ChunkSize;
                var builder = new TerrainLandBuilder(x0, y0, _texels);
                var water = _water != null ? new WaterSurfaceBuilder(x0, y0) : null;
                foreach (LandPlacement p in land)
                {
                    builder.AddTile(p.Item, p.Art, p.Texmap);
                    water?.AddTile(p.Item, p.Art, p.Texmap, WaterTiles.Lookup(_water, p.Item.Graphic));
                }
                TerrainLandData data = builder.Finish();
                WaterSurfaceData waterData = water != null && water.WaterTileCount > 0 ? water.Finish() : null;
                return data.TileCount == 0 ? null : new TerrainLandChunk(_presenter, data, waterData);
            }
            catch (Exception e)
            {
                _presenter.ReportFailure($"terrain chunk {chunkX},{chunkY}: {e.GetType().Name}: {e.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Main-thread upload of one chunk as a real UnityEngine.Terrain (TerrainData heightmap + holes, TerrainCollider)
    /// under the presenter's root and layer. Owns its TerrainData, textures (art, sun mask), material and object. Never a mesh stand-in:
    /// if the Terrain cannot be made it reports the failure and the controller returns to Original.
    /// </summary>
    internal sealed class TerrainLandChunk : ILandChunk
    {
        private readonly TerrainIsoPresenter _presenter;
        private TerrainLandData _data;
        private TerrainData _terrainData;
        private Texture2D _texture;
        private Texture2D _sunMask;
        private Material _material;
        private GameObject _go;
        private bool _counted;
        private WaterSurfaceData _waterData;
        private TerrainWaterSurface _water;
        private int _waterTiles;

        public TerrainLandChunk(TerrainIsoPresenter presenter, TerrainLandData data, WaterSurfaceData water)
        {
            _presenter = presenter;
            _data = data;
            _waterData = water;
        }

        public void Attach(Transform chunk, Shader shader, IWorldSpace world)
        {
            TerrainLandData d = _data;
            _data = null;
            if (d == null || _presenter.IsDisposed)
                return; // the chunk's own transform is not used: a Terrain must stay at its terrain-space position
            string name = $"{d.ChunkX0 / WorldStreamer.ChunkSize},{d.ChunkY0 / WorldStreamer.ChunkSize}";
            try
            {
                (float sx, float sy, float sz) = d.Size;
                _terrainData = new TerrainData { name = $"UO land terrain {name}" };
                _terrainData.heightmapResolution = TerrainLandBuilder.HeightmapResolution;
                _terrainData.size = new Vector3(sx, sy, sz);
                _terrainData.SetHeights(0, 0, d.Heights);
                _terrainData.SetHoles(0, 0, d.Solid);

                _texture = new Texture2D(d.TextureSize, d.TextureSize, TextureFormat.RGBA32, false)
                {
                    name = $"UO land terrain {name}",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                _texture.SetPixelData(d.Rgba, 0);
                _texture.Apply(false, true);
                _material = new Material(_presenter.Shader) { mainTexture = _texture, name = $"UO land terrain {name}" };
                if (_presenter.SharedLight && d.SunMask != null)
                {
                    _sunMask = new Texture2D(LiveGroundLighting.ShadowSize, LiveGroundLighting.ShadowSize, TextureFormat.R8, false, true)
                    {
                        name = $"UO land sun mask {name}",
                        filterMode = FilterMode.Bilinear, // soft ¼-tile shadow edge
                        wrapMode = TextureWrapMode.Clamp
                    };
                    _sunMask.SetPixelData(d.SunMask, 0);
                    _sunMask.Apply(false, true);
                    _material.SetTexture(TerrainIsoPresenter.SunMaskProperty, _sunMask);
                }

                _go = Terrain.CreateTerrainGameObject(_terrainData); // Terrain + TerrainCollider
                _go.name = "Land (LiveGround terrain)";
                _go.layer = TerrainIsoPresenter.Layer;
                _go.transform.SetParent(_presenter.Root, false);
                (float px, float py, float pz) = d.Position;
                _go.transform.localPosition = new Vector3(px, py, pz);

                Terrain t = _go.GetComponent<Terrain>();
                t.materialTemplate = _material;
                t.drawInstanced = false;
                t.heightmapPixelError = 1f;
                t.heightmapMaximumLOD = 0;
                t.basemapDistance = 20000f; // the base map would need a terrain shader's BaseMap pass
                t.drawTreesAndFoliage = false;
                t.shadowCastingMode = ShadowCastingMode.Off;
                t.allowAutoConnect = false;

                _presenter.ChunkCount++;
                _counted = true;
            }
            catch (Exception e)
            {
                Dispose();
                _presenter.ReportFailure($"terrain chunk {name}: {e.GetType().Name}: {e.Message}");
                return;
            }
            AttachWater(name);
        }

        /// <summary>The chunk's water surface, if it has water tiles. A failure here leaves the terrain up and is shown.</summary>
        private void AttachWater(string name)
        {
            WaterSurfaceData w = _waterData;
            _waterData = null;
            if (w == null || _presenter.Water == null)
                return;
            try
            {
                _water = TerrainWaterSurface.Attach(_presenter.Root, _presenter.Water.Material, w, name);
                if (_water == null)
                    return;
                _waterTiles = w.TileCount;
                _presenter.WaterTileCount += _waterTiles;
                _presenter.WaterChunkCount++;
                if (!_presenter.LoggedWaterChunk)
                {
                    _presenter.LoggedWaterChunk = true;
                    Debug.Log($"UOClient LiveGround water: first water chunk {name}: {w.TileCount} water tiles, " +
                              $"{w.VertexCount} vertices, land graphics {string.Join(",", w.Graphics.ConvertAll(g => $"0x{g:X4}"))}");
                }
            }
            catch (Exception e)
            {
                _water?.Dispose();
                _water = null;
                _presenter.WaterChunkError ??= $"water chunk {name}: {e.GetType().Name}: {e.Message}";
                Debug.LogWarning($"UOClient LiveGround water: chunk {name} failed: {e.GetType().Name}: {e.Message}");
            }
        }

        public void Dispose()
        {
            _data = null;
            _waterData = null;
            if (_water != null)
            {
                _presenter.WaterTileCount -= _waterTiles;
                _presenter.WaterChunkCount--;
                _water.Dispose();
            }
            _water = null;
            _waterTiles = 0;
            if (_counted)
                _presenter.ChunkCount--;
            _counted = false;
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_terrainData != null) UnityEngine.Object.Destroy(_terrainData);
            if (_material != null) UnityEngine.Object.Destroy(_material);
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            if (_sunMask != null) UnityEngine.Object.Destroy(_sunMask);
            _go = null;
            _sunMask = null;
            _terrainData = null;
            _material = null;
            _texture = null;
        }
    }
}
