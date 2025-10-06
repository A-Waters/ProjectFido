
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class TerrainPiece : MonoBehaviour
{
    // ---------- COMPONENT & DATA REFERENCES ----------
    private Mesh mesh;                   // The Runtime mesh we upload vertices/triangles into
    private MeshFilter meshFilter;       // Displays the mesh
    private MeshCollider meshCollider;   // Updates collider to match mesh after we rebuild

    // Deterministic RNG from GameManager (not strictly needed here, but kept for parity with your code)
    private System.Random rng;

    // ---------- NOISE SETTINGS (read from GameManager at generation time) ----------
    [Header("Noise Settings")]
    public float noiseScale = 20f;       // Local copy; GameManager.noiseScale is the source of truth
    public int seed = 0;                 // Unused in this file; seeding handled by GameManager

    // ---------- PROCEDURAL TERRAIN SETTINGS ----------
    public int chunkSize;               // Number of vertices per side (same for X and Z)
    public int stride;
    public float frequency;              // Not used directly; we read from GameManager each generation
    public float amplitude;              // Not used directly; we read from GameManager each generation
    public int octaves;                  // Not used directly; we read from GameManager each generation
    public float minHeight;              // Not used directly; we read from GameManager each generation
    public float maxHeight;              // Not used directly; we read from GameManager each generation
    public float lacunarity;             // Not used directly; we read from GameManager each generation
    public float persistence;            // Not used directly; we read from GameManager each generation

    // Which global "piece" coordinate this TerrainChunk represents (the grid index used by storage)
    private Vector2Int pieceCoord;

    // Gate to prevent generating before InitializeMesh is called
    private bool initialized;

    float[,] currrentHeightMap;
    float[,] lastHeightMap;

    // ---------- EDGE BLENDING ----------

    public TerrainManager terrainManager;

    // ---------- LIFECYCLE ----------

    void Awake()
    {
        terrainManager = Object.FindAnyObjectByType<TerrainManager>();
        // Grab the deterministic RNG from GameManager as soon as possible (if GM exists).
        //rng = terrainManager != null ? terrainManager.prng : null;

        // Calling GenerateTerrainMesh() here is safe because we guard with `initialized`.
        // On first Awake() we haven't been initialized (no size/coord), so this is a no-op.










        //GenerateTerrainMesh();
    }

    private void OnValidate()
    {
        // This is called when you tweak values in the inspector.
        // Only do heavy work while the game is running and object still exists in the scene.






        if (Application.isPlaying && gameObject != null)
        {
            // Rebuild *this* chunk. Note: group-level "RegenerateAll" (added below) ensures
            // neighbors also update their heightmaps first, so seam blending uses up-to-date data.
            GenerateTerrainMesh();
        }
    }

    /// <summary>
    /// Called by TerrainChunkGroup right after instantiation to wire size/coord references and mesh components.
    /// </summary>
    public void InitializeMesh(Vector2Int coord, int chunkSize, float noiseScale, System.Random prng)
    {
        // Remember which piece we are (global coord)
        this.pieceCoord = coord;

        // Remember the terrain resolution for this chunk
        this.chunkSize = chunkSize;

        // Local copy of current noiseScale (actual noise params are read from GameManager each build)
        this.noiseScale = noiseScale;

        this.rng = prng; // deterministic RNG for this piece

        if (terrainManager == null)
            terrainManager = Object.FindAnyObjectByType<TerrainManager>();

        if (terrainManager != null)
        {
            stride = terrainManager.ChunkStride;
            frequency = terrainManager.frequency;
            amplitude = terrainManager.amplitude;
            octaves = terrainManager.octaves;
            lacunarity = terrainManager.lacunarity;
            persistence = terrainManager.persistence;
            minHeight = terrainManager.minHeight;
            maxHeight = terrainManager.maxHeight;
        }

        

        // Ensure references to mesh-bearing components exist
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();

        // Create a mesh object if this is the first time we’re initializing
        if (mesh == null)
        {
            mesh = new Mesh();                                               // New runtime mesh container
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;     // Support large index counts
            meshFilter.sharedMesh = mesh;                                    // Show in MeshFilter
            meshCollider.sharedMesh = mesh;                                  // Use for collisions
        }

        // Mark as ready; subsequent GenerateTerrainMesh() calls will proceed
        initialized = true;
    }

    


    // ---------- HEIGHTMAP GENERATION ----------

    public float[,] GenerateRawHeightMap()
    {
        

        int size = terrainManager.chunkSize;
        int stride = terrainManager.ChunkStride;

        float[,] heightMap = new float[size, size];

        Vector2 globalOffset = terrainManager.GetGlobalNoiseOffset();

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // --- Convert to world space ---
                float worldX = pieceCoord.x * stride + x;
                float worldZ = pieceCoord.y * stride + y;

                // Scale to world units
                float nx = (worldX + globalOffset.x) / terrainManager.noiseScale;
                float nz = (worldZ + globalOffset.y) / terrainManager.noiseScale;

                // --- Fractal noise ---
                float noiseHeight = 0f;
                float frequency = 1f;
                float amplitude = 1f;

                for (int o = 0; o < terrainManager.octaves; o++)
                {
                    float sample = Mathf.PerlinNoise(nx * frequency, nz * frequency) * 2f - 1f;
                    noiseHeight += sample * amplitude;

                    amplitude *= terrainManager.persistence;
                    frequency *= terrainManager.lacunarity;
                }

                // Normalize to 0..1
                float t01 = Mathf.InverseLerp(-terrainManager.amplitude, terrainManager.amplitude, noiseHeight);

                // Apply height curve and multiplier
                float shaped = terrainManager.heightCurve != null
                    ? terrainManager.heightCurve.Evaluate(t01)
                    : t01;

                float height = shaped * terrainManager.heightMultiplier;

                float finalHeight = height;

                if (terrainManager.useFalloff)
                {
                    // Apply falloff *after* shaping
                    float falloff = terrainManager.SampleFalloff(worldX, worldZ);

                    finalHeight *= 1f - falloff;
                }
                

                heightMap[x, y] = finalHeight;
            }
        }

        currrentHeightMap = heightMap;
        terrainManager.heightMapStorage.SavePieceMap(pieceCoord, heightMap);

        return heightMap;

    }


    



    // Approximate gradient of Perlin noise at (x,y) by finite differences
    private Vector2 NoiseGradient(float x, float y, float freq)
    {
        float eps = 0.001f; // small offset
        float f = Mathf.PerlinNoise(x * freq, y * freq);
        float fx = Mathf.PerlinNoise((x + eps) * freq, y * freq);
        float fy = Mathf.PerlinNoise(x * freq, (y + eps) * freq);

        // Derivatives scaled by epsilon
        float dx = (fx - f) / eps;
        float dy = (fy - f) / eps;

        return new Vector2(dx, dy);
    }





    // ---------- MESH BUILD ----------

    /// <summary>
    /// Build/rebuild the mesh for this piece. If blendDirs are provided, blend only those edges.
    /// </summary>
    public void GenerateTerrainMesh()
    {
        // If we haven’t been initialized yet (no size/coord), there’s nothing we can generate
        if (!initialized) return;

        // Ensure we have an RNG reference if needed
        if (rng == null) rng = terrainManager != null ? terrainManager.prng : null;

        // 1) Generate and SAVE our raw height map based on the CURRENT GameManager noise params
        float[,] raw = GenerateRawHeightMap();
        if (raw == null) return; // Safety

        currrentHeightMap = raw;


        // 2) Convert the height map into a vertex/UV/triangle mesh
        Vector3[] vertices = new Vector3[chunkSize * chunkSize];                                   // Vertex array
        Vector2[] uvs = new Vector2[vertices.Length];                                         // UV array
        int[] triangles = new int[(chunkSize - 1) * (chunkSize - 1) * 6];

        // Use the final blended height map
        float[,] finalMap = currrentHeightMap;// Two tris per quad

        // Write vertices and UVs in row-major order: i = x + z*chunkSize
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int i = x + z * chunkSize;                                                         // Index into flat arrays
                float height = finalMap[x, z];                                                          // Height from height map
                vertices[i] = new Vector3(x, height, z);                                           // Position in local space
                uvs[i] = new Vector2((float)x / (chunkSize - 1), (float)z / (chunkSize - 1)); // Simple [0,1] UVs
            }
        }

        // Write index buffer (two triangles per grid cell)
        int t = 0;
        for (int z = 0; z < chunkSize - 1; z++)
        {
            for (int x = 0; x < chunkSize - 1; x++)
            {
                int i = x + z * chunkSize;                     // Top-left corner of current quad

                triangles[t++] = i;                            // Triangle 1: TL
                triangles[t++] = i + chunkSize;                //            BL
                triangles[t++] = i + 1;                        //            TR

                triangles[t++] = i + 1;                        // Triangle 2: TR
                triangles[t++] = i + chunkSize;                //            BL
                triangles[t++] = i + chunkSize + 1;            //            BR
            }
        }


        // 4) Upload the mesh to the GPU and refresh the collider
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
            if (meshCollider == null) meshCollider = GetComponent<MeshCollider>();
            meshFilter.sharedMesh = mesh;
            meshCollider.sharedMesh = mesh;
        }

        mesh.Clear();                 // Remove old data
        mesh.vertices = vertices;    // Set new vertices
        mesh.triangles = triangles;   // Set new indices
        mesh.uv = uvs;         // Set new UVs
        mesh.RecalculateNormals();    // Shade nicely

        // Refresh collider by reassigning the shared mesh
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}
