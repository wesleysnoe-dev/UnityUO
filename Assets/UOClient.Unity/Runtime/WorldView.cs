using UnityEngine;
using UOClient.ArtPipeline;

namespace UOClient.Unity
{
    /// <summary>What sprites need from the world renderer: where to put them and how deep.</summary>
    public interface IWorldSpace
    {
        /// <summary>World-screen pixel rect (top-left origin) to a Unity rect.</summary>
        Rect ToUnityRect(float left, float top, float width, float height);

        Vector2 ToUnity(Vector2 worldPixels);

        /// <summary>Depth that sorts a mobile or item among the map's placements.</summary>
        float DepthFor(DrawItem item);
    }

    /// <summary>
    /// Depth from a placement's own sort key, so any chunk can be built independently and still sort
    /// against every other chunk and against characters: diagonal (x + y), then priority Z, then kind
    /// (land, static, mobile), then order among ties on the same diagonal. The camera looks along +Z, so
    /// later in draw order means a smaller (nearer) Z. Diagonals are counted from a movable origin that
    /// is re-based as the player travels, keeping values small enough for the depth buffer.
    /// </summary>
    public static class DepthKey
    {
        // Priority Z spans the whole UO height range: Z is -128..127, land sits a step or two below it, characters +2 and
        // effects +30 above it. A narrower range clamped low towns (Luna on Malas stands at Z -90) to one priority, so a
        // house's foundation, floor and walls on one tile tied and drew in arrival order (floors over walls).
        public const int PriorityMin = -132, PriorityLevels = 292, Kinds = 3, TieLevels = 8;
        public const int PerDiagonal = PriorityLevels * Kinds * TieLevels;
        public const float Step = 0.005f;

        public static int Within(in DrawItem it, int tie)
        {
            int p = Mathf.Clamp(it.PriorityZ - PriorityMin, 0, PriorityLevels - 1);
            return (p * Kinds + (int)it.Kind) * TieLevels + Mathf.Clamp(tie, 0, TieLevels - 1);
        }

        /// <summary>Z relative to a base diagonal.</summary>
        public static float Relative(in DrawItem it, int baseDiagonal, int tie)
        {
            return -(((it.X + it.Y) - baseDiagonal) * (float)PerDiagonal + Within(it, tie)) * Step;
        }

        /// <summary>Offset that moves a chunk built against <paramref name="chunkDiagonal"/> into the current depth origin.</summary>
        public static float ChunkOffset(int chunkDiagonal, int originDiagonal) =>
            -(chunkDiagonal - originDiagonal) * (float)PerDiagonal * Step;
    }

    /// <summary>LiveGroundCompare (TEST) sun-shadow casters (<see cref="MeshBuilder.ShadowCasters"/>); main thread, once per drawn frame.</summary>
    public interface IShadowCasterSink
    {
        /// <summary>A mobile drawn with its feet at <paramref name="anchorPixels"/> (world-screen pixels at the art scale) and Z <paramref name="footZ"/>.</summary>
        void Mobile(object key, Vector2 anchorPixels, float footZ);

        /// <summary>A world item of <paramref name="graphic"/> drawn on tile (x, y) at Z <paramref name="z"/>.</summary>
        void Item(object key, int x, int y, int z, int graphic);
    }

    /// <summary>Accumulates sprite quads into one mesh.</summary>
    public sealed class MeshBuilder
    {
        private readonly System.Collections.Generic.List<Vector3> _vertices = new System.Collections.Generic.List<Vector3>();
        private readonly System.Collections.Generic.List<Vector2> _uvs = new System.Collections.Generic.List<Vector2>();
        private readonly System.Collections.Generic.List<int> _indices = new System.Collections.Generic.List<int>();
        private readonly System.Collections.Generic.List<Vector2> _heights = new System.Collections.Generic.List<Vector2>();
        private System.Collections.Generic.List<Vector4> _feet; // made by the first foot quad; null keeps today's mesh

        public int QuadCount => _vertices.Count / 4;

        /// <summary>
        /// LiveGroundCompare (TEST) only: when set, quads given a foot anchor carry it in UV3 for the sprite shadow
        /// receive (WorldSprite.shader, LG_SPRITE_SHADOW). Off by default; the presenter sets it and clears it on
        /// dispose. While off, every mesh is exactly what it was without the foot overloads.
        /// </summary>
        public static bool FootMetadata
        {
            get => _footMetadata;
            set
            {
                _footMetadata = value;
                _footEverSet |= value;
            }
        }

        private static bool _footMetadata, _footEverSet; // the second spares the normal client any check per quad

        /// <summary>
        /// LiveGroundCompare (TEST) only: where mobiles and items say where they stand each frame they are drawn, so they
        /// can cast into the CPU sun-shadow ceiling. Null by default (nothing is called); only read inside the
        /// <see cref="FootMetadata"/> branches. The presenter's ceiling sets it and puts it back on dispose.
        /// </summary>
        public static IShadowCasterSink ShadowCasters { get; set; }

        /// <summary>
        /// Tile height passed to the shader (second UV channel) for cutting away upper floors, offset by 1000 so a
        /// real height is never 0; <see cref="NeverCut"/> (0, also what meshes without the channel read) always draws.
        /// </summary>
        public const float NeverCut = 0f;

        public static float CutHeight(int tileZ) => tileZ + 1000f;

        /// <param name="uv">x = left, y = bottom, z = right, w = top.</param>
        /// <param name="height">The tile height for the upper-floor cut (<see cref="CutHeight"/>), or <see cref="NeverCut"/>.</param>
        public void AddQuad(Rect r, Vector4 uv, float z, bool mirror, float height = NeverCut, bool water = false, float plant = 0f)
        {
            int i = _vertices.Count;
            // y: 1 UO water art (WorldSprite's water shimmer); a plant (PlantKind): its kind at the foot, kind + sway on top.
            var h = new Vector2(height, water ? 1f : plant > 0f ? PlantKind.AtFoot(plant) : 0f);
            var top = new Vector2(height, water ? 1f : plant);
            _heights.Add(h);
            _heights.Add(top);
            _heights.Add(top);
            _heights.Add(h);
            _vertices.Add(new Vector3(r.xMin, r.yMin, z));
            _vertices.Add(new Vector3(r.xMin, r.yMax, z));
            _vertices.Add(new Vector3(r.xMax, r.yMax, z));
            _vertices.Add(new Vector3(r.xMax, r.yMin, z));

            float u0 = mirror ? uv.z : uv.x, u1 = mirror ? uv.x : uv.z;
            _uvs.Add(new Vector2(u0, uv.y));
            _uvs.Add(new Vector2(u0, uv.w));
            _uvs.Add(new Vector2(u1, uv.w));
            _uvs.Add(new Vector2(u1, uv.y));

            _indices.Add(i);
            _indices.Add(i + 1);
            _indices.Add(i + 2);
            _indices.Add(i);
            _indices.Add(i + 2);
            _indices.Add(i + 3);
            if (_feet != null)
                for (int k = 0; k < 4; k++)
                    _feet.Add(Vector4.zero);
        }

        /// <param name="foot">
        /// LiveGroundCompare sprite shadow: the foot anchor (Unity x, y; UO Z; kind, 0 = none). Written only while
        /// <see cref="FootMetadata"/> is set; otherwise this is the overload without it.
        /// </param>
        public void AddQuad(Rect r, Vector4 uv, float z, bool mirror, float height, Vector4 foot, bool water = false, float plant = 0f)
        {
            AddQuad(r, uv, z, mirror, height, water, plant);
            if (!FootMetadata)
                return;
            if (_feet == null)
            {
                _feet = new System.Collections.Generic.List<Vector4>(_vertices.Count);
                for (int k = 0; k < _vertices.Count; k++)
                    _feet.Add(Vector4.zero);
            }
            for (int k = _feet.Count - 4; k < _feet.Count; k++)
                _feet[k] = foot;
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(_vertices);
            mesh.SetUVs(0, _uvs);
            mesh.SetUVs(1, _heights);
            if (_feet != null)
                mesh.SetUVs(2, _feet);
            mesh.SetTriangles(_indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Shared scratch buffers for single-quad updates (main thread only): no allocation per frame.
        private static readonly Vector3[] QuadVertices = new Vector3[4];
        private static readonly Vector2[] QuadUvs = new Vector2[4];
        private static readonly Vector2[] QuadHeights = new Vector2[4];
        private static readonly Vector4[] QuadFeet = new Vector4[4];
        private static readonly int[] QuadIndices = { 0, 1, 2, 0, 2, 3 };

        /// <summary>
        /// As the overload without a foot, and while <see cref="FootMetadata"/> is set the quad also carries
        /// <paramref name="foot"/> in UV3 (LiveGroundCompare sprite shadow). When it is not set the mesh is the same as
        /// without it (a foot channel left from before is removed).
        /// </summary>
        public static void SetQuad(Mesh mesh, Rect r, Vector4 uv, float z, bool mirror, float height, Vector4 foot, float code = 0f)
        {
            if (!FootMetadata)
            {
                SetQuad(mesh, r, uv, z, mirror, height, code);
                return;
            }
            SetQuad(mesh, r, uv, z, mirror, height, code);
            for (int k = 0; k < 4; k++)
                QuadFeet[k] = foot;
            mesh.SetUVs(2, QuadFeet);
        }

        /// <summary>
        /// Rewrites the single quad of a one-sprite mesh (characters, items) without allocating. <paramref name="code"/>:
        /// the sprite code in the second UV's y (PlantKind: a house's window glows at night), 0 for none.
        /// </summary>
        public static void SetQuad(Mesh mesh, Rect r, Vector4 uv, float z, bool mirror, float height = NeverCut, float code = 0f)
        {
            if (_footEverSet && !_footMetadata && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord2))
                mesh.Clear(); // a foot left by the TEST sprite shadow: back to the plain quad (rebuilt below)
            for (int k = 0; k < 4; k++)
                QuadHeights[k] = new Vector2(height, code);
            QuadVertices[0] = new Vector3(r.xMin, r.yMin, z);
            QuadVertices[1] = new Vector3(r.xMin, r.yMax, z);
            QuadVertices[2] = new Vector3(r.xMax, r.yMax, z);
            QuadVertices[3] = new Vector3(r.xMax, r.yMin, z);
            float u0 = mirror ? uv.z : uv.x, u1 = mirror ? uv.x : uv.z;
            QuadUvs[0] = new Vector2(u0, uv.y);
            QuadUvs[1] = new Vector2(u0, uv.w);
            QuadUvs[2] = new Vector2(u1, uv.w);
            QuadUvs[3] = new Vector2(u1, uv.y);

            if (mesh.vertexCount != 4)
            {
                mesh.Clear();
                mesh.vertices = QuadVertices;
                mesh.uv = QuadUvs;
                mesh.uv2 = QuadHeights;
                mesh.triangles = QuadIndices;
            }
            else
            {
                mesh.vertices = QuadVertices;
                mesh.uv = QuadUvs;
                mesh.uv2 = QuadHeights;
            }
            mesh.RecalculateBounds();
        }
    }
}
