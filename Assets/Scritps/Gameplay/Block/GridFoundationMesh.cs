using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    /// <summary>Builds a single, optional island/foundation below a block grid.</summary>
    internal static class GridFoundationMesh
    {
        public static void Create(BlockGridData grid, Transform parent)
        {
            if (grid == null || parent == null || !grid.GenerateFoundation || grid.Rows < 1) return;

            var go = new GameObject("Foundation");
            go.transform.SetParent(parent, false);
            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            // No implicit fallback material: projects may use URP where "Standard" is absent.
            // Leaving it null makes the missing setup obvious and avoids leaking a material each rebuild.
            renderer.sharedMaterial = grid.FoundationMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            Mesh source = grid.FoundationSourceMesh;
            if (source != null && !source.isReadable)
            {
                Debug.LogWarning($"[GridFoundationMesh] Mesh '{source.name}' is not readable. Enable Read/Write on its import settings; using generated foundation for '{parent.name}'.");
                source = null;
            }
            // Active-cell mode is intentionally the default footprint: holes/inactive cells must
            // remain water. A source mesh is still supported as a full custom slab when assigned.
            filter.sharedMesh = grid.FoundationWaypoints != null && grid.FoundationWaypoints.Count >= 3
                ? BuildDrawnPolygon(grid, parent)
                : source != null
                ? BuildBentSource(grid, parent, source)
                : BuildActiveCells(grid, parent);
        }

        private static Mesh BuildDrawnPolygon(BlockGridData grid, Transform parent)
        {
            var samples = RoundedPolylinePath.BuildSamples(grid.FoundationWaypoints, true,
                grid.FoundationCornerRadius, 8, grid.FoundationStyle);
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            if (samples == null || samples.Length < 4) return new Mesh { name = "EmptyFoundation" };
            for (int i = 0; i < samples.Length - 1; i++)
            {
                Vector3 p = grid.FoundationWorldPoint(samples[i]);
                p.y += grid.FoundationYOffset;
                vertices.Add(parent.InverseTransformPoint(p));
                uvs.Add(new Vector2(p.x, p.z));
            }
            int count = vertices.Count;
            // Top face. Points are authored around the perimeter in order; fan triangulation keeps
            // the generated mesh lightweight for the intended island-like shapes.
            for (int i = 1; i < count - 1; i++) { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); }
            if (grid.FoundationThickness > 0f)
            {
                int bottom = vertices.Count;
                Vector3 down = parent.InverseTransformVector(Vector3.down).normalized * grid.FoundationThickness;
                for (int i = 0; i < count; i++) { vertices.Add(vertices[i] + down); uvs.Add(uvs[i]); }
                for (int i = 1; i < count - 1; i++) { triangles.Add(bottom); triangles.Add(bottom + i + 1); triangles.Add(bottom + i); }
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    triangles.Add(i); triangles.Add(bottom + i); triangles.Add(bottom + next);
                    triangles.Add(i); triangles.Add(bottom + next); triangles.Add(next);
                }
            }
            var mesh = new Mesh { name = "DrawnFoundation" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildActiveCells(BlockGridData grid, Transform parent)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            float halfW = Mathf.Max(0.05f, grid.BlockWidth > 0f ? grid.BlockWidth * grid.CellScale.x * 0.5f : 0.5f);
            float halfD = Mathf.Max(0.05f, grid.BlockWidth > 0f ? grid.BlockWidth * grid.CellScale.z * 0.5f : 0.5f);
            float y = grid.FoundationYOffset;

            for (int row = 0; row < grid.Rows; row++)
            {
                int count = grid.ElementsInRow(row);
                for (int e = 0; e < count; e++)
                {
                    var cell = grid.GetCell(row, e);
                    if (cell == null || cell.BlockStackCt <= 0) continue;
                    Vector3 center = grid.CellPosAt(row, e, count);
                    Vector3 prev = grid.CellPosAt(row, Mathf.Max(0, e - 1), count);
                    Vector3 next = grid.CellPosAt(row, Mathf.Min(count - 1, e + 1), count);
                    Vector3 tangent = next - prev; tangent.y = 0f;
                    if (tangent.sqrMagnitude < 1e-6f) tangent = grid.Forward;
                    tangent.Normalize();
                    Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;
                    // Along-path width follows the tangent; depth points toward the next row.
                    Vector3 a = center - tangent * halfW - normal * halfD + Vector3.up * y;
                    Vector3 b = center + tangent * halfW - normal * halfD + Vector3.up * y;
                    Vector3 c = center + tangent * halfW + normal * halfD + Vector3.up * y;
                    Vector3 d = center - tangent * halfW + normal * halfD + Vector3.up * y;
                    int baseIndex = vertices.Count;
                    vertices.Add(parent.InverseTransformPoint(a)); vertices.Add(parent.InverseTransformPoint(b));
                    vertices.Add(parent.InverseTransformPoint(c)); vertices.Add(parent.InverseTransformPoint(d));
                    uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2);
                    triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
                }
            }
            var mesh = new Mesh { name = "GridActiveCellFoundation" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Bends a designer mesh into the grid footprint. Source convention: X runs along the
        /// spline, Z runs from row 0 toward the last row, Y remains the authored vertical detail.
        /// This keeps bevels, rocks and custom side silhouettes from an FBX instead of replacing
        /// them with the generated slab.
        /// </summary>
        private static Mesh BuildBentSource(BlockGridData grid, Transform parent, Mesh source)
        {
            var srcVertices = source.vertices;
            var srcUvs = source.uv;
            var vertices = new List<Vector3>(srcVertices.Length);
            var uvs = new List<Vector2>(srcVertices.Length);
            Bounds bounds = source.bounds;
            float sizeX = Mathf.Max(0.0001f, bounds.size.x);
            float sizeZ = Mathf.Max(0.0001f, bounds.size.z);
            float firstRow = -0.5f - grid.FoundationMargin / Mathf.Max(0.01f, grid.RowSpacing);
            float lastRow = grid.Rows - 0.5f + grid.FoundationMargin / Mathf.Max(0.01f, grid.RowSpacing);

            for (int i = 0; i < srcVertices.Length; i++)
            {
                Vector3 v = srcVertices[i];
                float s = Mathf.Clamp01((v.x - bounds.min.x) / sizeX);
                float row = Mathf.Lerp(firstRow, lastRow, (v.z - bounds.min.z) / sizeZ);
                Vector3 p = grid.SurfacePosition(row, s);
                p.y += v.y + grid.FoundationYOffset;
                vertices.Add(parent.InverseTransformPoint(p));
                uvs.Add(srcUvs != null && srcUvs.Length == srcVertices.Length ? srcUvs[i] : Vector2.zero);
            }

            var mesh = new Mesh { name = source.name + "_BentFoundation", indexFormat = source.vertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(source.triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildGenerated(BlockGridData grid, Transform parent)
        {
            bool closed = grid.IsFullRing;
            int segments = Mathf.Max(4, grid.FoundationSegments);
            int columns = closed ? segments : segments + 1;
            float firstRow = -0.5f - grid.FoundationMargin / Mathf.Max(0.01f, grid.RowSpacing);
            float lastRow = grid.Rows - 0.5f + grid.FoundationMargin / Mathf.Max(0.01f, grid.RowSpacing);
            var vertices = new List<Vector3>(columns * 4);
            var uvs = new List<Vector2>(columns * 4);
            var triangles = new List<int>();

            // Two rails define the footprint.  SurfacePosition uses the same rounded/Bezier spline
            // as cells, so a designer only adjusts the existing grid spline waypoints.
            for (int r = 0; r < 2; r++)
            {
                float row = r == 0 ? firstRow : lastRow;
                for (int i = 0; i < columns; i++)
                {
                    float s = i / (float)segments;
                    Vector3 p = grid.SurfacePosition(row, s) + Vector3.up * grid.FoundationYOffset;
                    vertices.Add(parent.InverseTransformPoint(p));
                    uvs.Add(new Vector2(s, r));
                }
            }

            int bottomStart = -1;
            if (grid.FoundationThickness > 0f)
            {
                bottomStart = vertices.Count;
                Vector3 localDown = parent.InverseTransformVector(Vector3.down).normalized * grid.FoundationThickness;
                for (int i = 0; i < columns * 2; i++)
                {
                    vertices.Add(vertices[i] + localDown);
                    uvs.Add(uvs[i]);
                }
            }

            int spanCount = closed ? columns : columns - 1;
            for (int i = 0; i < spanCount; i++)
            {
                int next = (i + 1) % columns;
                AddQuad(triangles, i, next, columns + next, columns + i); // top
                if (bottomStart < 0) continue;
                AddQuad(triangles, bottomStart + next, bottomStart + i, bottomStart + columns + i, bottomStart + columns + next); // bottom
                AddQuad(triangles, i, bottomStart + i, bottomStart + next, next); // front rail
                AddQuad(triangles, columns + next, bottomStart + columns + next, bottomStart + columns + i, columns + i); // back rail
            }
            if (bottomStart >= 0 && !closed)
            {
                int end = columns - 1;
                AddQuad(triangles, 0, columns, bottomStart + columns, bottomStart);
                AddQuad(triangles, columns + end, end, bottomStart + end, bottomStart + columns + end);
            }

            var mesh = new Mesh { name = "GridFoundation" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(List<int> triangles, int a, int b, int c, int d)
        {
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
        }
    }
}
