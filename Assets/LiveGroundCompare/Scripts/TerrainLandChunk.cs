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
        private readonly int _scale, _texels;
        private readonly bool[] _water; // null: water off
        private readonly bool[] _staticWater; // ocean statics the water surface draws instead of their sprites
        private readonly bool _occlusion;

        public TerrainLandEmitter(TerrainIsoPresenter presenter, int scale)
        {
            _presenter = presenter;
            _scale = scale;
            _texels = TerrainLandBuilder.TexelsFor(scale);
            _water = presenter.Water?.Table;
            _staticWater = presenter.Water?.StaticTable;
            _occlusion = presenter.DepthShader != null;
        }

        /// <summary>
        /// Around a chunk, for the water's shore distance: whether a map tile is drawn ground that no water covers (not
        /// water land, no ocean static on it, not no-draw land). Off the map counts as not land.
        /// </summary>
        private Func<int, int, bool> OutsideLand()
        {
            WorldStreamer world = _presenter.World;
            IO.Formats.MapFile map = world?.Data?.GetMap(world.Map);
            if (map == null)
                return null;
            var statics = new Dictionary<long, IO.Formats.StaticCell[]>();
            return (x, y) =>
            {
                if (x < 0 || y < 0 || x >= map.Width || y >= map.Height)
                    return false;
                IO.Formats.LandCell cell = map.GetLand(x, y);
                ushort g = cell.Graphic;
                if (g < 3 || g == 0x1DB || (g >= 0x1AE && g <= 0x1B5) || WaterTiles.Lookup(_water, g))
                    return false; // no-draw land (IsoCompositor.IsNoDrawLand) or water land
                long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
                if (!statics.TryGetValue(key, out IO.Formats.StaticCell[] block))
                    statics[key] = block = map.ReadStaticBlock(x >> 3, y >> 3);
                if (block != null)
                    foreach (IO.Formats.StaticCell s in block)
                        if (s.X == (x & 7) && s.Y == (y & 7) && WaterTiles.Lookup(_staticWater, s.Graphic))
                            return false;
                return true;
            };
        }

        /// <summary>The chunk's static columns for the sun mask, or null (terrain-only shadow, reason reported once).</summary>
        private int[,] Occluders(int x0, int y0)
        {
            if (_presenter.OccluderError != null)
                return null;
            try
            {
                WorldStreamer world = _presenter.World;
                if (world?.Data == null)
                {
                    _presenter.ReportOccluderError("no WorldStreamer.Data");
                    return null;
                }
                return StaticOccluders.FromMap(world.Data.GetMap(world.Map), world.Data.TileData, x0, y0);
            }
            catch (Exception e)
            {
                _presenter.ReportOccluderError($"{e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>The map's material at any tile (natural terrain), off the map or unreadable: classic art.</summary>
        private Func<int, int, int> MaterialAt()
        {
            WorldStreamer world = _presenter.World;
            IO.Formats.MapFile map = world?.Data?.GetMap(world.Map);
            TerrainMaterialSet set = _presenter.Materials;
            return (x, y) => map == null || set == null || x < 0 || y < 0 || x >= map.Width || y >= map.Height
                ? TerrainMaterials.Art
                : set.MaterialOf(map.GetLand(x, y).Graphic);
        }

        // Land as before, plus the ocean statics: drawn by the water surface, not as flat tiled sprites over it.
        public bool Diverts(in ArtPipeline.DrawItem item, bool baked) =>
            GroundBackend.Diverts(GroundBackendKind.Terrain, item, baked)
            || !baked && item.Kind == ArtPipeline.DrawKind.Static && WaterTiles.Lookup(_staticWater, item.Graphic);

        public ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land)
        {
            try
            {
                int x0 = chunkX * WorldStreamer.ChunkSize, y0 = chunkY * WorldStreamer.ChunkSize;
                var builder = new TerrainLandBuilder(x0, y0, _texels);
                var water = _water != null ? new WaterSurfaceBuilder(x0, y0) : null;
                water?.SetOutside(OutsideLand());
                foreach (LandPlacement p in land)
                {
                    if (p.Item.Kind == ArtPipeline.DrawKind.Static) // an ocean static (Diverts): water only
                    {
                        water?.AddStaticWater(p.Item, p.Art);
                        continue;
                    }
                    builder.AddTile(p.Item, p.Art, p.Texmap, _occlusion ? p.Depth : float.NaN);
                    water?.AddTile(p.Item, p.Art, p.Texmap, WaterTiles.Lookup(_water, p.Item.Graphic));
                    // Natural terrain: UO's own colour for this ground, for the tint (until enough tiles are seen).
                    TerrainMaterialSet set = _presenter.Materials;
                    if (set != null && p.Art != null)
                    {
                        int material = set.MaterialOf(p.Item.Graphic);
                        if (set.NeedsSamples(material))
                            set.AddUoSample(material, WaterSurfaceBuilder.MeanColor(p.Art, p.Texmap));
                    }
                }
                // Under water the ground bakes flat (no classic water pattern through the surface).
                water?.ForEachBed((x, y, argb) => builder.SetFlat(x, y, argb));
                int[,] occluders = _presenter.SharedLight ? Occluders(x0, y0) : null;
                builder.SetOccluders(occluders);
                TerrainLandData data = builder.Finish();
                if (_presenter.Materials != null && data.TileCount > 0)
                    data.Splat = SplatBuilder.Build(x0, y0, MaterialAt());
                // Sprite receive: the same heights and columns, as heights below which a point is in shadow.
                if (_presenter.SpriteCeiling != null && data.TileCount > 0)
                {
                    data.SunCeiling = LiveGroundLighting.ShadowCeiling(data.CornerZ, occluders);
                    data.OccluderTop = occluders;
                }
                WaterSurfaceData waterData = water != null && water.WaterTileCount > 0 ? water.Finish() : null;
                DepthProxyData proxy = _occlusion ? TerrainDepthProxy.Build(data, _scale) : null;
                return data.TileCount == 0 ? null : new TerrainLandChunk(_presenter, data, waterData, proxy);
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
        private Texture2D[] _splat; // natural terrain weights (3 RGBA), null for the classic art
        private Material _material;
        private GameObject _go;
        private bool _counted;
        private WaterSurfaceData _waterData;
        private TerrainWaterSurface _water;
        private int _waterTiles;
        private DepthProxyData _proxyData;
        private Mesh _proxyMesh;
        private Material _proxyMaterial;
        private GameObject _proxyGo;
        private int _occluderTiles;
        private (int X0, int Y0)? _ceiling; // published into the presenter's sprite shadow ceiling

        public TerrainLandChunk(TerrainIsoPresenter presenter, TerrainLandData data, WaterSurfaceData water, DepthProxyData proxy)
        {
            _presenter = presenter;
            _data = data;
            _waterData = water;
            _proxyData = proxy;
        }

        public void Attach(Transform chunk, Shader shader, IWorldSpace world)
        {
            TerrainLandData d = _data;
            _data = null;
            if (d == null || _presenter.IsDisposed)
                return; // a Terrain must stay at its terrain-space position; only the occlusion proxy uses the chunk's transform
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
                TerrainMaterialSet set = _presenter.Materials;
                if (set != null && d.Splat != null)
                {
                    _material = new Material(set.Shader) { mainTexture = _texture, name = $"UO land terrain (natural) {name}" };
                    _splat = new Texture2D[d.Splat.Length];
                    for (int s = 0; s < d.Splat.Length; s++)
                    {
                        _splat[s] = new Texture2D(SplatBuilder.Size, SplatBuilder.Size, TextureFormat.RGBA32, false, true)
                        {
                            name = $"UO land splat {s} {name}",
                            filterMode = FilterMode.Bilinear,
                            wrapMode = TextureWrapMode.Clamp
                        };
                        _splat[s].SetPixelData(d.Splat[s], 0);
                        _splat[s].Apply(false, true);
                        _material.SetTexture("_Splat" + s, _splat[s]);
                    }
                    set.PublishTint();
                }
                else
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
                _occluderTiles = d.OccluderTiles;
                _presenter.OccluderTileCount += _occluderTiles;
                _counted = true;
                if (d.SunCeiling != null && _presenter.SpriteCeiling != null)
                {
                    _presenter.SpriteCeiling.Publish(d.ChunkX0, d.ChunkY0, d.SunCeiling, d.CornerZ, d.OccluderTop, this);
                    _ceiling = (d.ChunkX0, d.ChunkY0);
                }
            }
            catch (Exception e)
            {
                Dispose();
                _presenter.ReportFailure($"terrain chunk {name}: {e.GetType().Name}: {e.Message}");
                return;
            }
            AttachWater(name);
            AttachProxy(chunk, world, name);
        }

        /// <summary>
        /// The depth-only occlusion proxy (<see cref="TerrainDepthProxy"/>), a child of the streamer's chunk so it gets the
        /// same depth-origin offset as that chunk's sprites, on the chunk's layer so the main camera draws it. A failure
        /// leaves the terrain up (drawn behind every sprite, as before) and is shown.
        /// </summary>
        private void AttachProxy(Transform chunk, IWorldSpace world, string name)
        {
            DepthProxyData p = _proxyData;
            _proxyData = null;
            if (p == null || chunk == null || _presenter.DepthShader == null)
                return;
            try
            {
                int n = p.VertexCount;
                var vertices = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 u = world.ToUnity(new Vector2(p.X[i], p.Y[i]));
                    vertices[i] = new Vector3(u.x, u.y, p.Depth[i]);
                }
                _proxyMesh = new Mesh { name = $"UO land depth proxy {name}", indexFormat = IndexFormat.UInt32 };
                _proxyMesh.vertices = vertices;
                _proxyMesh.triangles = p.Indices;
                _proxyMesh.RecalculateBounds();
                _proxyMaterial = new Material(_presenter.DepthShader) { name = $"UO land depth proxy {name}" };

                _proxyGo = new GameObject("Land depth (LiveGround terrain occlusion)") { layer = chunk.gameObject.layer };
                _proxyGo.transform.SetParent(chunk, false);
                _proxyGo.AddComponent<MeshFilter>().sharedMesh = _proxyMesh;
                var r = _proxyGo.AddComponent<MeshRenderer>();
                r.sharedMaterial = _proxyMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                _presenter.ProxyChunkCount++;
            }
            catch (Exception e)
            {
                DisposeProxy();
                _presenter.OcclusionChunkError ??= $"occlusion chunk {name}: {e.GetType().Name}: {e.Message}";
                Debug.LogWarning($"UOClient LiveGround occlusion: chunk {name} failed: {e.GetType().Name}: {e.Message}");
            }
        }

        private void DisposeProxy()
        {
            if (_proxyGo != null)
            {
                _presenter.ProxyChunkCount--;
                UnityEngine.Object.Destroy(_proxyGo);
            }
            if (_proxyMesh != null) UnityEngine.Object.Destroy(_proxyMesh);
            if (_proxyMaterial != null) UnityEngine.Object.Destroy(_proxyMaterial);
            _proxyGo = null;
            _proxyMesh = null;
            _proxyMaterial = null;
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
            _proxyData = null;
            DisposeProxy();
            if (_ceiling.HasValue)
                _presenter.SpriteCeiling?.Remove(_ceiling.Value.X0, _ceiling.Value.Y0, this);
            _ceiling = null;
            _presenter.OccluderTileCount -= _occluderTiles;
            _occluderTiles = 0;
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
            if (_splat != null)
                foreach (Texture2D s in _splat)
                    if (s != null) UnityEngine.Object.Destroy(s);
            _splat = null;
            _go = null;
            _sunMask = null;
            _terrainData = null;
            _material = null;
            _texture = null;
        }
    }
}
