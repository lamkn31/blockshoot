#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menu: Tools > Water > Build Toon Water Pool.
/// Tạo nhanh một bể bơi (đáy + 4 thành) và mặt nước dùng ToonWaterURP shader.
/// Mặt nước được chia nhỏ (subdivided) để sóng Gerstner trong vertex shader hoạt động.
/// </summary>
public static class ToonWaterPoolBuilder
{
    const string MaterialPath = "Assets/Material/ToonWaterPool.mat";

    [MenuItem("Tools/Water/Build Toon Water Pool")]
    public static void BuildPool()
    {
        // Kích thước bể
        float sizeX = 8f, sizeZ = 6f, depth = 2f, wall = 0.3f;
        float waterLevel = -0.4f; // mặt nước thấp hơn miệng bể một chút

        var root = new GameObject("ToonWaterPool");
        Undo.RegisterCreatedObjectUndo(root, "Build Toon Water Pool");

        var poolMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        poolMat.color = new Color(0.85f, 0.88f, 0.92f);

        // Đáy bể
        CreateBox(root.transform, "Floor",
            new Vector3(0, -depth, 0),
            new Vector3(sizeX, wall, sizeZ), poolMat);

        // 4 thành bể
        CreateBox(root.transform, "Wall_North",
            new Vector3(0, -depth * 0.5f, sizeZ * 0.5f),
            new Vector3(sizeX, depth, wall), poolMat);
        CreateBox(root.transform, "Wall_South",
            new Vector3(0, -depth * 0.5f, -sizeZ * 0.5f),
            new Vector3(sizeX, depth, wall), poolMat);
        CreateBox(root.transform, "Wall_East",
            new Vector3(sizeX * 0.5f, -depth * 0.5f, 0),
            new Vector3(wall, depth, sizeZ), poolMat);
        CreateBox(root.transform, "Wall_West",
            new Vector3(-sizeX * 0.5f, -depth * 0.5f, 0),
            new Vector3(wall, depth, sizeZ), poolMat);

        // Mặt nước
        var water = new GameObject("WaterSurface",
            typeof(MeshFilter), typeof(MeshRenderer));
        water.transform.SetParent(root.transform, false);
        water.transform.localPosition = new Vector3(0, waterLevel, 0);

        var mf = water.GetComponent<MeshFilter>();
        mf.sharedMesh = BuildGridMesh(sizeX - wall, sizeZ - wall, 40, 30);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
            Debug.LogWarning($"[ToonWaterPoolBuilder] Không tìm thấy material tại {MaterialPath}. Gán thủ công.");
        water.GetComponent<MeshRenderer>().sharedMaterial = mat;

        Selection.activeObject = root;
        Debug.Log("[ToonWaterPoolBuilder] Đã tạo bể bơi toon water. " +
                  "Nhớ bật Depth Texture trên URP asset để foam hiển thị.");
    }

    static void CreateBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>Tạo mesh phẳng (mặt XZ) chia lưới để sóng vertex hoạt động mượt.</summary>
    static Mesh BuildGridMesh(float width, float length, int cols, int rows)
    {
        var mesh = new Mesh { name = "WaterGrid" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        int vx = cols + 1, vz = rows + 1;
        var verts = new Vector3[vx * vz];
        var uvs = new Vector2[vx * vz];
        var norms = new Vector3[vx * vz];

        for (int z = 0; z < vz; z++)
        for (int x = 0; x < vx; x++)
        {
            float fx = (float)x / cols, fz = (float)z / rows;
            int i = z * vx + x;
            verts[i] = new Vector3((fx - 0.5f) * width, 0, (fz - 0.5f) * length);
            uvs[i]   = new Vector2(fx, fz);
            norms[i] = Vector3.up;
        }

        var tris = new int[cols * rows * 6];
        int t = 0;
        for (int z = 0; z < rows; z++)
        for (int x = 0; x < cols; x++)
        {
            int i = z * vx + x;
            tris[t++] = i;
            tris[t++] = i + vx;
            tris[t++] = i + 1;
            tris[t++] = i + 1;
            tris[t++] = i + vx;
            tris[t++] = i + vx + 1;
        }

        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = norms;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
#endif
