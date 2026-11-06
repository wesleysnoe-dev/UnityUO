using System.Collections.Generic;
using UnityEngine;

namespace UOClient.GroundPrototype
{
    /// <summary>
    /// Builds the custom-mesh side from the same corner heights as the Terrain side.
    ///
    /// Triangulation (honest note): two planar triangles per quad, always split on the
    /// SW(0,0)-NE(1,1) diagonal. Corners match the terrain heightmap samples, but interiors do NOT
    /// match Unity Terrain in general (see HeightParityCheck). Vertices are duplicated per tile so
    /// each tile owns one material (one submesh per palette entry); normals are taken from shared
    /// corner central differences so lighting stays smooth across tile seams.
    ///
    /// P2: when a blend material (MeshGroundBlend) is given, every tile goes into one submesh and the
    /// shader blends the four layers from the shared control mask; UV0 holds local map coordinates
    /// (world units from the side origin), and the shader derives the same coordinates from
    /// worldPos - _MapOrigin.
    /// </summary>
    public static class MeshSideBuilder
    {
        public static GameObject Build(SampleElevationGrid grid, Material[] palette, Vector3 origin, Transform parent,
            Material blendMaterial = null, GroundVisualFactory visuals = null)
        {
            if (blendMaterial != null) palette = new[] { blendMaterial };
            var cornerNormals = CornerNormals(grid);
            var verts = new List<Vector3>(grid.TilesX * grid.TilesY * 4);
            var normals = new List<Vector3>(verts.Capacity);
            var uvs = new List<Vector2>(verts.Capacity);
            var subTris = new List<int>[palette.Length];
            for (int i = 0; i < subTris.Length; i++) subTris[i] = new List<int>();

            for (int ty = 0; ty < grid.TilesY; ty++)
            for (int tx = 0; tx < grid.TilesX; tx++)
            {
                int b = verts.Count;
                AddCorner(grid, cornerNormals, tx, ty, verts, normals, uvs);         // b+0 SW (0,0)
                AddCorner(grid, cornerNormals, tx + 1, ty, verts, normals, uvs);     // b+1 SE (1,0)
                AddCorner(grid, cornerNormals, tx, ty + 1, verts, normals, uvs);     // b+2 NW (0,1)
                AddCorner(grid, cornerNormals, tx + 1, ty + 1, verts, normals, uvs); // b+3 NE (1,1)

                var tris = subTris[Mathf.Clamp(grid.TileMaterial(tx, ty), 0, palette.Length - 1)];
                // Clockwise seen from above (Unity front faces). Diagonal SW-NE.
                tris.Add(b + 0); tris.Add(b + 2); tris.Add(b + 3);
                tris.Add(b + 0); tris.Add(b + 3); tris.Add(b + 1);
            }

            var mesh = new Mesh { name = "GroundPrototype_SampleMesh (in-memory)" };
            visuals?.Track(mesh);
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = palette.Length;
            for (int i = 0; i < palette.Length; i++) mesh.SetTriangles(subTris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            var go = new GameObject("Side_CustomMesh");
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = palette;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            if (blendMaterial != null) GroundVisualFactory.SetMapOrigin(mr, origin);
            return go;
        }

        private static void AddCorner(SampleElevationGrid grid, Vector3[,] cornerNormals, int cx, int cy,
            List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs)
        {
            verts.Add(new Vector3(cx * grid.tileSize, (float)grid.CornerWorldY(cx, cy), cy * grid.tileSize));
            normals.Add(cornerNormals[cx, cy]);
            uvs.Add(new Vector2(cx * grid.tileSize, cy * grid.tileSize));
        }

        private static Vector3[,] CornerNormals(SampleElevationGrid grid)
        {
            var n = new Vector3[grid.cornersX, grid.cornersY];
            for (int cy = 0; cy < grid.cornersY; cy++)
            for (int cx = 0; cx < grid.cornersX; cx++)
            {
                int x0 = Mathf.Max(0, cx - 1), x1 = Mathf.Min(grid.cornersX - 1, cx + 1);
                int y0 = Mathf.Max(0, cy - 1), y1 = Mathf.Min(grid.cornersY - 1, cy + 1);
                float dhdx = (float)(grid.CornerWorldY(x1, cy) - grid.CornerWorldY(x0, cy)) / ((x1 - x0) * grid.tileSize);
                float dhdz = (float)(grid.CornerWorldY(cx, y1) - grid.CornerWorldY(cx, y0)) / ((y1 - y0) * grid.tileSize);
                n[cx, cy] = new Vector3(-dhdx, 1f, -dhdz).normalized;
            }
            return n;
        }
    }
}
