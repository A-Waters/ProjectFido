using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainChunk : MonoBehaviour
{
    // Each key is the *global piece coordinate* (not local within the group)
    private readonly Dictionary<Vector2Int, TerrainPiece> pieces = new();

    private Vector2Int groupCoord;   // integer group coordinate
    private TerrainManager terrainManager;

    // Which OUTER edge of this group should blend with already-existing neighbors ("West","East","North","South", or null)
    private string spawnEdge;

    // local offsets for the 3×3 around the center: -1, 0, 1
    private static readonly Vector2Int[] LocalOffsets =
    {
        new Vector2Int(-1, -1), new Vector2Int(0, -1), new Vector2Int(1, -1),
        new Vector2Int(-1,  0), new Vector2Int(0,  0), new Vector2Int(1,  0),
        new Vector2Int(-1,  1), new Vector2Int(0,  1), new Vector2Int(1,  1),
    };

    /// <summary>
    /// Initialize this group and spawn its 3×3 TerrainChunk pieces.
    /// </summary>
    public void Initialize(Vector2Int groupCoord, TerrainManager terrainManager)
    {
        this.groupCoord = groupCoord;
        this.terrainManager = terrainManager;

        int groupMapSize = 3 * terrainManager.ChunkStride + 1; // +1 so seams align

        float[,] groupMap = new float[groupMapSize, groupMapSize];

        name = $"ChunkGroup_{groupCoord.x}_{groupCoord.y}";
        transform.parent = terrainManager.transform;

        // For each local offset, compute the global piece coord and instantiate a TerrainChunk there.
        foreach (var local in LocalOffsets)
        {
            // Piece coord mapping:
            // group g covers piece coords { 3g + (-1,0,1) } in each axis.
            Vector2Int pieceCoord = new Vector2Int(
                groupCoord.x * 3 + local.x,
                groupCoord.y * 3 + local.y
            );

            Vector3 worldPos = new Vector3(
                pieceCoord.x * terrainManager.ChunkStride,
                0f,
                pieceCoord.y * terrainManager.ChunkStride
            );

            GameObject pieceObj = Object.Instantiate(terrainManager.chunkPrefab, worldPos, Quaternion.identity, transform);
            pieceObj.name = $"Piece_{pieceCoord.x}_{pieceCoord.y}";

            var chunk = pieceObj.GetComponent<TerrainPiece>();
            chunk.InitializeMesh(pieceCoord, terrainManager.chunkSize, terrainManager.noiseScale, terrainManager.prng);


            chunk.GenerateTerrainMesh();
            //terrainManager.heightMapStorage.SaveGroupMap(groupCoord, groupMap); //THIS LINE

            pieces[pieceCoord] = chunk;
        }



        foreach (var kvp in pieces)
        {
            Vector2Int pieceCoord = kvp.Key;
            TerrainPiece chunk = kvp.Value;

            // Convert to local coordinates in {-1,0,1}
            Vector2Int local = new Vector2Int(
                pieceCoord.x - groupCoord.x * 3,
                pieceCoord.y - groupCoord.y * 3
            );

        }
    }


    

    /// <summary>
    /// Rebuild every child piece (used when you tweak settings at runtime).
    /// Ensures blending is reapplied on group boundaries.
    /// </summary>
    public void RegenerateAll()
    {
        foreach (var kvp in pieces)
        {
            Vector2Int pieceCoord = kvp.Key;
            TerrainPiece chunk = kvp.Value;
            if (chunk == null) continue;

            Vector2Int local = new Vector2Int(
                pieceCoord.x - groupCoord.x * 3,
                pieceCoord.y - groupCoord.y * 3
            );

            chunk.GenerateTerrainMesh();
        }
    }

    public bool ContainsPiece(Vector2Int pieceCoord) => pieces.ContainsKey(pieceCoord);
}

