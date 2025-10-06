#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public static class TerrainExporter
{
    [MenuItem("Tools/Terrain/Export Combined Terrain Mesh %#e")] // Ctrl+Shift+E
    public static void ExportCombinedTerrainMesh()
    {
        //  Unity 6-safe object finding
        var pieces = Object.FindObjectsByType<TerrainPiece>(FindObjectsSortMode.None);

        if (pieces == null || pieces.Length == 0)
        {
            Debug.LogWarning("No TerrainPieces found in scene to export!");
            return;
        }

        Debug.Log($"Found {pieces.Length} terrain pieces — combining...");

        List<MeshFilter> meshFilters = new List<MeshFilter>();

        foreach (var piece in pieces)
        {
            MeshFilter mf = piece.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                meshFilters.Add(mf);

            //  COLLIDER STRIPPING SECTION 
            // ---------------------------------
            // This will REMOVE colliders from the pieces to save memory during export.
            // Comment or delete this section if you want to KEEP colliders.
            Collider col = piece.GetComponent<Collider>();
            if (col != null)
            {
                Object.DestroyImmediate(col);
            }
            // ---------------------------------
        }

        if (meshFilters.Count == 0)
        {
            Debug.LogWarning("No valid MeshFilters found on TerrainPieces!");
            return;
        }

        // Combine all meshes
        List<CombineInstance> combineInstances = new List<CombineInstance>();

        foreach (var mf in meshFilters)
        {
            var mesh = mf.sharedMesh;
            var transform = mf.transform;

            CombineInstance ci = new CombineInstance
            {
                mesh = mesh,
                transform = transform.localToWorldMatrix
            };

            combineInstances.Add(ci);
        }

        Mesh combinedMesh = new Mesh();
        combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // large terrain support
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, false);

        Debug.Log($" Combined mesh built — {combinedMesh.vertexCount} vertices total.");

        // Create folder for exports
        string folderPath = "Assets/Exports";
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        // Save as .asset
        string assetPath = Path.Combine(folderPath, "CombinedTerrain.asset");
        AssetDatabase.CreateAsset(combinedMesh, assetPath);
        AssetDatabase.SaveAssets();

        Debug.Log($" Saved combined terrain mesh at: {assetPath}");

        // Optionally, export to OBJ for external 3D tools
        if (EditorUtility.DisplayDialog("Export OBJ?",
            "Would you also like to export this mesh as a .obj file?", "Yes", "No"))
        {
            string objPath = EditorUtility.SaveFilePanel("Save OBJ", "", "CombinedTerrain.obj", "obj");
            if (!string.IsNullOrEmpty(objPath))
            {
                ExportToOBJ(combinedMesh, objPath);
                Debug.Log($" OBJ exported to {objPath}");
            }
        }
    }

    // Minimal OBJ exporter (fast & clean)
    private static void ExportToOBJ(Mesh mesh, string path)
    {
        using (StreamWriter sw = new StreamWriter(path))
        {
            sw.WriteLine("# Combined Terrain Export");
            foreach (Vector3 v in mesh.vertices)
                sw.WriteLine($"v {v.x} {v.y} {v.z}");
            foreach (Vector3 n in mesh.normals)
                sw.WriteLine($"vn {n.x} {n.y} {n.z}");
            foreach (Vector2 uv in mesh.uv)
                sw.WriteLine($"vt {uv.x} {uv.y}");

            int[] triangles = mesh.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                sw.WriteLine($"f {triangles[i] + 1}/{triangles[i] + 1}/{triangles[i] + 1} " +
                             $"{triangles[i + 1] + 1}/{triangles[i + 1] + 1}/{triangles[i + 1] + 1} " +
                             $"{triangles[i + 2] + 1}/{triangles[i + 2] + 1}/{triangles[i + 2] + 1}");
            }
        }
    }
}
#endif