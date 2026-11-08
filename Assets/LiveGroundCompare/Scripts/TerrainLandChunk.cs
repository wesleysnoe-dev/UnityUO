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
            // Natural terrain cuts the shoreline from the ground itself: the classic shoreline edge statics (the other
            // "water" statics, 0x346E… / 0x55F0…, part foam, part rock) would draw a sawtooth over it, so they go.
            if (presenter.Materials != null && _staticWater != null && presenter.World?.Data?.TileData?.Statics is { } statics)
            {
                _shoreEdge = new bool[statics.Length];
                for (int i = 0; i < statics.Length; i++)
                    _shoreEdge[i] = WaterTiles.IsWater(statics[i].Flags, statics[i].Name) && !WaterTiles.Lookup(_staticWater, i);
            }
        }

        private readonly bool[] _shoreEdge; // natural terrain: shoreline edge statics, dropped

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
                IO.Formats.LandCell cell = Land(map, x, y);
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

        /// <summary>
        /// Natural terrain, per chunk build: any world tile → (water, its level): land water at its lowest corner, or an
        /// ocean static at the static's Z (the higher one if several). Cached for the build; off the map: dry.
        /// </summary>
        private Func<int, int, (bool Water, int Level)> TileWater(IO.Formats.MapFile map)
        {
            var tiles = new Dictionary<long, (bool, int)>();
            var blocks = new Dictionary<long, IO.Formats.StaticCell[]>();
            return (x, y) =>
            {
                if (map == null || x < 0 || y < 0 || x >= map.Width - 1 || y >= map.Height - 1)
                    return (false, 0);
                long key = ((long)x << 32) | (uint)y;
                if (tiles.TryGetValue(key, out var hit))
                    return hit;
                (bool, int) result = (false, 0);
                long bk = ((long)(x >> 3) << 32) | (uint)(y >> 3);
                if (!blocks.TryGetValue(bk, out IO.Formats.StaticCell[] block))
                    blocks[bk] = block = map.ReadStaticBlock(x >> 3, y >> 3);
                int? top = null;
                if (block != null)
                    foreach (IO.Formats.StaticCell s in block)
                        if (s.X == (x & 7) && s.Y == (y & 7) && WaterTiles.Lookup(_staticWater, s.Graphic))
                            top = Math.Max(top ?? int.MinValue, s.Z);
                if (top.HasValue)
                    result = (true, top.Value);
                else if (WaterTiles.Lookup(_water, Land(map, x, y).Graphic))
                    // Its own height: a neighbour's corner may be the sea floor under ocean statics (Z -15 against -5).
                    result = (true, Land(map, x, y).Z);
                tiles[key] = result;
                return result;
            };
        }

        /// <summary>
        /// World editor: a chunk's splat images straight from its painted region's weights (the same material ids as
        /// <see cref="TerrainMaterials"/>, classic = the art weight), in <see cref="SplatBuilder"/>'s layout.
        /// </summary>
        private static byte[][] PaintedSplat(WorldLayer.WorldRegion region, int x0, int y0)
        {
            const int size = SplatBuilder.Size, sp = WorldLayer.WorldRegion.SamplesPerTile;
            var images = new[] { new byte[size * size * 4], new byte[size * size * 4], new byte[size * size * 4] };
            var w = new float[WorldLayer.WorldMaterials.Count];
            for (int sy = 0; sy < size; sy++)
                for (int sx = 0; sx < size; sx++)
                {
                    int lx = x0 * sp + sx - region.SampleX0, ly = y0 * sp + sy - region.SampleY0;
                    region.GetWeights(WorldLayer.WorldRegion.Index(lx, ly), w);
                    int o = ((size - 1 - sy) * size + sx) * 4;
                    for (int k = 0; k <= TerrainMaterials.Art && k < w.Length; k++)
                        images[k / 4][o + k % 4] = (byte)Math.Round(255f * Math.Min(1f, w[k]));
                }
            return images;
        }

        /// <summary>The map's material at any tile (natural terrain), off the map or unreadable: classic art.</summary>
        private Func<int, int, int> MaterialAt()
        {
            WorldStreamer world = _presenter.World;
            IO.Formats.MapFile map = world?.Data?.GetMap(world.Map);
            TerrainMaterialSet set = _presenter.Materials;
            return (x, y) => map == null || set == null || x < 0 || y < 0 || x >= map.Width || y >= map.Height
                ? TerrainMaterials.Art
                : set.MaterialOf(Land(map, x, y).Graphic);
        }

        // Land as before, plus the ocean statics: drawn by the water surface, not as flat tiled sprites over it.
        public bool Diverts(in ArtPipeline.DrawItem item, bool baked) =>
            GroundBackend.Diverts(GroundBackendKind.Terrain, item, baked)
            || !baked && item.Kind == ArtPipeline.DrawKind.Static
               && (WaterTiles.Lookup(_staticWater, item.Graphic) || _shoreEdge != null && WaterTiles.Lookup(_shoreEdge, item.Graphic));

        // One chunk build's land blocks (worker thread): MapFile.GetLand reads and copies a whole 8x8 block on every
        // call, and a build asks for the same tiles tens of thousands of times (smooth heights, water bed, coast).
        [ThreadStatic] private static Dictionary<long, IO.Formats.LandCell[]> _buildBlocks;

        private static IO.Formats.LandCell Land(IO.Formats.MapFile map, int x, int y)
        {
            Dictionary<long, IO.Formats.LandCell[]> blocks = _buildBlocks;
            if (blocks == null)
                return map.GetLand(x, y);
            long key = ((long)(x >> 3) << 32) | (uint)(y >> 3);
            if (!blocks.TryGetValue(key, out IO.Formats.LandCell[] cells))
                blocks[key] = cells = map.ReadLandBlock(x >> 3, y >> 3);
            return cells[((y & 7) << 3) | (x & 7)];
        }

        public ILandChunk BuildLand(int chunkX, int chunkY, List<LandPlacement> land)
        {
            _buildBlocks = new Dictionary<long, IO.Formats.LandCell[]>();
            try
            {
                int x0 = chunkX * WorldStreamer.ChunkSize, y0 = chunkY * WorldStreamer.ChunkSize;
                var builder = new TerrainLandBuilder(x0, y0, _texels);
                var water = _water != null ? new WaterSurfaceBuilder(x0, y0) : null;
                water?.SetOutside(OutsideLand());
                WorldLayer.WorldRegion painted = null; // world editor: this chunk's painted region, if any
                Func<float, float, float> bedAt = null; // natural terrain: the water bed's drop at a point
                // Natural terrain: smooth heights, the bed lowered under water, and a flat water surface over it.
                if (_presenter.Materials != null)
                {
                    WorldStreamer world = _presenter.World;
                    IO.Formats.MapFile map = world?.Data?.GetMap(world.Map);
                    if (map != null)
                    {
                        Func<int, int, (bool Water, int Level)> tileWater = TileWater(map);
                        var raw = new Dictionary<long, float>();
                        Func<int, int, float> rawZ = (x, y) =>
                        {
                            int cx = Math.Max(0, Math.Min(map.Width - 1, x)), cy = Math.Max(0, Math.Min(map.Height - 1, y));
                            long key = ((long)cx << 32) | (uint)cy;
                            if (raw.TryGetValue(key, out float hit))
                                return hit;
                            int mapZ = Land(map, cx, cy).Z;
                            float z = _water != null ? WaterBed.Corner(mapZ, tileWater, cx, cy) : mapZ;
                            // Under water the bed and the bank wander a little (world-fixed noise), so the shallows
                            // and the waterline are not straight tile rows.
                            if (z < mapZ)
                                z += (SplatBuilder.Noise(cx * 0.37f, cy * 0.37f, 5) - 0.5f) * 3.5f;
                            raw[key] = z;
                            return z;
                        };
                        // Banks: a corner with water within a tile but not under open water is averaged with its
                        // neighbours (tent 3x3), which rounds the map's tile-staircase coastlines off.
                        var corners = new Dictionary<long, float>();
                        Func<int, int, float> cornerZ = (x, y) =>
                        {
                            long key = ((long)x << 32) | (uint)y;
                            if (corners.TryGetValue(key, out float hit))
                                return hit;
                            float z = rawZ(x, y);
                            int near = 0;
                            for (int tx = x - 2; tx <= x + 1; tx++)
                                for (int ty = y - 2; ty <= y + 1; ty++)
                                    if (tileWater(tx, ty).Water)
                                        near++;
                            if (near > 0 && near < 16)
                            {
                                float sum = 0, weight = 0;
                                for (int dx = -1; dx <= 1; dx++)
                                    for (int dy = -1; dy <= 1; dy++)
                                    {
                                        float w = (2 - Math.Abs(dx)) * (2 - Math.Abs(dy));
                                        sum += rawZ(x + dx, y + dy) * w;
                                        weight += w;
                                    }
                                z = sum / weight;
                            }
                            corners[key] = z;
                            return z;
                        };
                        builder.UseSmoothHeights(cornerZ);
                        // Sun mask: a corner under water is shaded as the water's surface (no shadows of the bed's steps).
                        builder.UseShadeHeights((x, y) =>
                        {
                            float ground = cornerZ(x, y);
                            float? level = WaterSurfaceBuilder.FlatLevel(tileWater, x, y);
                            return level.HasValue && ground < level.Value ? Math.Max(ground, level.Value) : (float?)null;
                        });
                        water?.UseFlatLevel(tileWater);
                        // The water's depth at any point = its level less this same smooth ground.
                        Func<float, float, float> ground = (wx, wy) => TerrainLandBuilder.SmoothZ(cornerZ, wx, wy);
                        // The water bed's drop at a point (0 away from water): the painted shape is drawn this much lower.
                        Func<int, int, float> pure = (x, y) =>
                            Land(map, Math.Max(0, Math.Min(map.Width - 1, x)), Math.Max(0, Math.Min(map.Height - 1, y))).Z;
                        bedAt = (wx, wy) => Math.Min(0f, TerrainLandBuilder.SmoothZ(cornerZ, wx, wy) - TerrainLandBuilder.SmoothZ(pure, wx, wy));

                        // World editor: a painted region draws its own shape (plus the same water bed under water).
                        (int prx, int pry) = WorldLayer.WorldRegion.RegionOfTile(x0, y0);
                        painted = WorldLayerClient.Current?.Region(world.Map, prx, pry);
                        if (painted != null)
                        {
                            WorldLayer.WorldRegion region = painted;
                            Func<float, float, float> bedDrop = bedAt;
                            const int sp = WorldLayer.WorldRegion.SamplesPerTile;
                            Func<int, int, float> sampleZ = (sx, sy) =>
                            {
                                int lx = Math.Max(0, Math.Min(WorldLayer.WorldRegion.N - 1, sx - region.SampleX0));
                                int ly = Math.Max(0, Math.Min(WorldLayer.WorldRegion.N - 1, sy - region.SampleY0));
                                return region.Height[WorldLayer.WorldRegion.Index(lx, ly)] / (float)WorldLayer.WorldRegion.ZScale
                                       + bedDrop(sx / (float)sp, sy / (float)sp);
                            };
                            builder.UseSampleHeights(sampleZ);
                            ground = (wx, wy) =>
                            {
                                float fx = wx * sp, fy = wy * sp;
                                int ix = (int)Math.Floor(fx), iy = (int)Math.Floor(fy);
                                float u = fx - ix, v = fy - iy;
                                return (sampleZ(ix, iy) * (1 - u) + sampleZ(ix + 1, iy) * u) * (1 - v)
                                     + (sampleZ(ix, iy + 1) * (1 - u) + sampleZ(ix + 1, iy + 1) * u) * v;
                            };
                        }
                        water?.UseGround(ground);
                    }
                }
                foreach (LandPlacement p in land)
                {
                    if (p.Item.Kind == ArtPipeline.DrawKind.Static) // an ocean static (Diverts): water only; a shoreline edge: dropped
                    {
                        if (WaterTiles.Lookup(_staticWater, p.Item.Graphic))
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
                // Natural terrain: water over no-draw land still gets a bed (no hole showing through the water).
                if (_presenter.Materials != null && water != null && _presenter.World?.Data?.GetMap(_presenter.World.Map) is { } bedMap)
                {
                    Func<int, int, (bool Water, int Level)> wet = TileWater(bedMap);
                    uint bed = water.ChunkBed;
                    for (int i = 0; i < WorldStreamer.ChunkSize; i++)
                        for (int j = 0; j < WorldStreamer.ChunkSize; j++)
                            if (wet(x0 + i, y0 + j).Water)
                                builder.ForceBed(x0 + i, y0 + j, bed);
                }
                int[,] occluders = _presenter.SharedLight ? Occluders(x0, y0) : null;
                builder.SetOccluders(occluders);
                TerrainLandData data = builder.Finish();
                if (_presenter.Materials != null && data.TileCount > 0)
                    data.Splat = painted != null ? PaintedSplat(painted, x0, y0) : SplatBuilder.Build(x0, y0, MaterialAt());
                if (_presenter.GrassShader != null && data.Splat != null)
                {
                    data.Grass = GrassBuilder.Build(data);
                    GrassBuilder.Project(data.Grass, data.TileDepth, x0, y0, _scale);
                }
                // World editor: the bed's drop per heightmap sample, kept so a stroke can rewrite heights in place.
                if (bedAt != null && data.HeightmapResolution == TerrainLandBuilder.Tiles * TerrainLandBuilder.SmoothSamplesPerTile + 1)
                {
                    int n = data.HeightmapResolution, sp = TerrainLandBuilder.SmoothSamplesPerTile;
                    data.BedDelta = new float[n, n];
                    for (int row = 0; row < n; row++)
                        for (int col = 0; col < n; col++)
                            data.BedDelta[row, col] = bedAt(x0 + col / (float)sp, y0 + (n - 1 - row) / (float)sp);
                }
                // Sprite receive: the same heights and columns, as heights below which a point is in shadow.
                if (_presenter.SpriteCeiling != null && data.TileCount > 0)
                {
                    data.SunCeiling = LiveGroundLighting.ShadowCeiling(data.CornerZ, occluders);
                    data.OccluderTop = occluders;
                }
                // Natural terrain also finishes chunks with no water of their own: a skirt may reach in from the next chunk.
                WaterSurfaceData waterData = water != null && (water.WaterTileCount > 0 || _presenter.Materials != null) ? water.Finish() : null;
                if (waterData != null && waterData.VertexCount == 0)
                    waterData = null;
                DepthProxyData proxy = _occlusion ? TerrainDepthProxy.Build(data, _scale) : null;
                return data.TileCount == 0 ? null : new TerrainLandChunk(_presenter, data, waterData, proxy);
            }
            catch (Exception e)
            {
                _presenter.ReportFailure($"terrain chunk {chunkX},{chunkY}: {e.GetType().Name}: {e.Message}");
                return null;
            }
            finally
            {
                _buildBlocks = null;
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
                _terrainData.heightmapResolution = d.HeightmapResolution;
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
                        _splat[s].Apply(false, false); // kept readable: the world editor rewrites it in place (Repaint)
                        _material.SetTexture("_LGWeights" + s, _splat[s]); // not _SplatN: Unity's Terrain overwrites those every frame
                    }
                    set.PublishTint();
                    _splatBytes = d.Splat;
                }
                else
                    _material = new Material(_presenter.Shader) { mainTexture = _texture, name = $"UO land terrain {name}" };
                _bedDelta = d.BedDelta;
                _x0 = d.ChunkX0;
                _y0 = d.ChunkY0;
                _resolution = d.HeightmapResolution;
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
                if (_bedDelta != null && _splatBytes != null)
                    Painter.Chunks[(_x0, _y0)] = this; // the world editor repaints it in place
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
            AttachGrass(d.Grass, chunk, world, name);
            AttachProxy(chunk, world, name);
        }

        private TerrainGrass _grass;

        private void AttachGrass(GrassData g, Transform chunk, IWorldSpace world, string name)
        {
            if (g == null || _presenter.GrassShader == null || chunk == null)
                return;
            try
            {
                _grass = TerrainGrass.Attach(chunk, world, _presenter.GrassShader, _sunMask, g, name);
            }
            catch (Exception e)
            {
                _grass?.Dispose();
                _grass = null;
                Debug.LogWarning($"UOClient LiveGround grass: chunk {name} failed: {e.GetType().Name}: {e.Message}");
            }
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

        // ---- world editor: repaint in place ----

        private byte[][] _splatBytes;
        private float[,] _bedDelta;
        private int _x0, _y0, _resolution;

        /// <summary>
        /// The world editor's in-place repaint (WorldLayerClient.PaintedTerrain): the chunks on screen that can be
        /// rewritten, by their first tile. Main thread only.
        /// </summary>
        internal sealed class Painter : IPaintedTerrain
        {
            public static readonly Dictionary<(int, int), TerrainLandChunk> Chunks = new Dictionary<(int, int), TerrainLandChunk>();
            public static readonly Painter Instance = new Painter();

            public void Repaint(int map, RectInt tiles)
            {
                WorldLayerClient layer = WorldLayerClient.Current;
                if (layer == null)
                    return;
                const int c = WorldStreamer.ChunkSize;
                for (int cy = Mathf.FloorToInt(tiles.yMin / (float)c); cy <= tiles.yMax / c; cy++)
                    for (int cx = Mathf.FloorToInt(tiles.xMin / (float)c); cx <= tiles.xMax / c; cx++)
                        if (Chunks.TryGetValue((cx * c, cy * c), out TerrainLandChunk chunk))
                            chunk.Repaint(layer, map, tiles);
            }
        }

        /// <summary>
        /// Rewrites this chunk's heights and material weights under a tile rectangle from the editor's live layer (no
        /// rebuild): heights into the TerrainData, weights into the splat textures. Water, the sun mask and the occlusion
        /// proxy catch up at the next full rebuild, a moment after the painting stops.
        /// </summary>
        private void Repaint(WorldLayerClient layer, int map, RectInt tiles)
        {
            if (_terrainData == null || _bedDelta == null || _splatBytes == null || _splat == null)
                return;
            const int sp = TerrainLandBuilder.SmoothSamplesPerTile, size = SplatBuilder.Size;
            int n = _resolution;
            (int rx, int ry) = WorldLayer.WorldRegion.RegionOfTile(_x0, _y0);
            WorldLayer.WorldRegion region = layer.LiveRegion(map, rx, ry);
            if (region == null)
                return;
            // Sample range (chunk-local, inclusive) under the rectangle.
            int s0x = Math.Max(0, (tiles.xMin - _x0) * sp), s1x = Math.Min(n - 1, (tiles.xMax - _x0) * sp);
            int s0y = Math.Max(0, (tiles.yMin - _y0) * sp), s1y = Math.Min(n - 1, (tiles.yMax - _y0) * sp);
            if (s0x > s1x || s0y > s1y)
                return;

            // Heights: rows run along +Z, i.e. tile y descending (row = n - 1 - local sample y).
            int r0 = n - 1 - s1y, r1 = n - 1 - s0y;
            var heights = new float[r1 - r0 + 1, s1x - s0x + 1];
            for (int row = r0; row <= r1; row++)
                for (int col = s0x; col <= s1x; col++)
                {
                    int sy = n - 1 - row;
                    int i = WorldLayer.WorldRegion.Index(_x0 * sp + col - region.SampleX0, _y0 * sp + sy - region.SampleY0);
                    float z = region.Height[i] / (float)WorldLayer.WorldRegion.ZScale + _bedDelta[row, col];
                    heights[row - r0, col - s0x] = TerrainLandBuilder.Normalize(z);
                }
            _terrainData.SetHeights(s0x, r0, heights);

            // Weights: the splat's texels are the samples 0..63 (rows bottom-up, as PaintedSplat writes them).
            var w = new float[WorldLayer.WorldMaterials.Count];
            for (int sy = s0y; sy <= Math.Min(size - 1, s1y); sy++)
                for (int sx = s0x; sx <= Math.Min(size - 1, s1x); sx++)
                {
                    region.GetWeights(WorldLayer.WorldRegion.Index(_x0 * sp + sx - region.SampleX0, _y0 * sp + sy - region.SampleY0), w);
                    int o = ((size - 1 - sy) * size + sx) * 4;
                    for (int k = 0; k <= TerrainMaterials.Art && k < w.Length; k++)
                        _splatBytes[k / 4][o + k % 4] = (byte)Math.Round(255f * Math.Min(1f, w[k]));
                }
            for (int s = 0; s < _splat.Length; s++)
            {
                _splat[s].SetPixelData(_splatBytes[s], 0);
                _splat[s].Apply(false, false);
            }
        }

        public void Dispose()
        {
            if (Painter.Chunks.TryGetValue((_x0, _y0), out TerrainLandChunk registered) && registered == this)
                Painter.Chunks.Remove((_x0, _y0));
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
            _grass?.Dispose();
            _grass = null;
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
