
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
        rng = terrainManager != null ? terrainManager.prng : null;

        // Calling GenerateTerrainMesh() here is safe because we guard with `initialized`.
        // On first Awake() we haven't been initialized (no size/coord), so this is a no-op.
        GenerateTerrainMesh();
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
    public void InitializeMesh(Vector2Int coord, int chunkSize, float noiseScale)
    {
        // Remember which piece we are (global coord)
        this.pieceCoord = coord;

        // Remember the terrain resolution for this chunk
        this.chunkSize = chunkSize;

        // Local copy of current noiseScale (actual noise params are read from GameManager each build)
        this.noiseScale = noiseScale;

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
        float[,] heightMap = new float[chunkSize, chunkSize];

        Vector2 globalSeedOffset = terrainManager.GetGlobalNoiseOffset();
        int stride = terrainManager.ChunkStride;

        // Compute global bounds (so curve input always in [0,1])
        float maxPossible = 0f;
        float ampCheck = terrainManager.amplitude;

        for (int o = 0; o < terrainManager.octaves; o++)
        {
            maxPossible += ampCheck;
            ampCheck *= terrainManager.persistence;
        }
        float globalMin = -maxPossible;
        float globalMax = maxPossible;
        float globalRange = globalMax - globalMin;

        // Strength of slope suppression (tweak in inspector if desired)
        float k = terrainManager.slopeSuppressionStrength; // e.g. 2.0f default

        // --- Generate heights ---
        for (int x = 0; x < chunkSize; x++)
        {
            for (int y = 0; y < chunkSize; y++)
            {
                float noiseHeight = 0f;
                Vector2 runningGradient = Vector2.zero; // cumulative gradient

                float freq = terrainManager.frequency;
                float amp = terrainManager.amplitude;

                float gx = pieceCoord.x * stride + x + globalSeedOffset.x;
                float gz = pieceCoord.y * stride + y + globalSeedOffset.y;

                float sx = gx / terrainManager.noiseScale;
                float sy = gz / terrainManager.noiseScale;

                // --- Sum octaves with slope suppression ---
                for (int o = 0; o < terrainManager.octaves; o++)
                {
                    float perlin = Mathf.PerlinNoise(sx * freq, sy * freq) * 2f - 1f;

                    // Local gradient for this octave
                    Vector2 grad = NoiseGradient(sx, sy, freq);

                    // Combine with running gradient so far
                    Vector2 combinedGrad = runningGradient + grad * amp;
                    float gradMag = combinedGrad.magnitude;

                    // Suppression factor (flatter = closer to 1, steeper = smaller)
                    float suppression = 1f / (1f + k * gradMag);

                    // Add suppressed contribution
                    noiseHeight += perlin * amp * suppression;

                    // Update running gradient
                    runningGradient += grad * amp;

                    // Prepare for next octave
                    freq *= terrainManager.lacunarity;
                    amp *= terrainManager.persistence;
                }

                // Normalize into 0..1
                float t01 = (noiseHeight - globalMin) / globalRange;

                // Apply height shaping curve
                float shaped = terrainManager.heightCurve != null
                    ? terrainManager.heightCurve.Evaluate(t01)
                    : t01;

                // Final scaled height
                heightMap[x, y] = shaped * terrainManager.heightMultiplier;
            }
        }

        currrentHeightMap = heightMap;
        terrainManager.heightMapStorage.SavePieceMap(pieceCoord, heightMap);
        return heightMap;
    }


    public void ApplyNeighborSeams(List<string> allowedDirs)
    {
        if (currrentHeightMap == null || allowedDirs == null || allowedDirs.Count == 0)
            return;

        int size = chunkSize;
        int band = Mathf.Min(terrainManager.edgeBlendWidth, size);
        int stride = terrainManager.ChunkStride;

        // Base world-grid index for this piece (integer coordinates of local (0,0) vertex)
        int pieceWorldBaseX = pieceCoord.x * stride;
        int pieceWorldBaseZ = pieceCoord.y * stride;

        foreach (string dir in allowedDirs)
        {
            Vector2Int neighborOffset =
                dir == "West" ? Vector2Int.left :
                dir == "East" ? Vector2Int.right :
                dir == "South" ? Vector2Int.down :
                dir == "North" ? Vector2Int.up :
                Vector2Int.zero;

            if (neighborOffset == Vector2Int.zero)
                continue;

            Vector2Int neighborCoord = pieceCoord + neighborOffset;
            float[,] neighborMap = terrainManager.heightMapStorage.GetGroupMap(neighborCoord);

            // For each local cell in the band compute the world-grid coordinate and sample the
            // continued neighbor height (i.e. what the noise would produce at that world coordinate).
            if (dir == "West")
            {
                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < band; x++)
                    {
                        float neighborHeight;

                        int neighborBaseX = pieceWorldBaseX - stride; // neighbor is one stride left
                        int neighborBaseZ = pieceWorldBaseZ;

                        if (neighborMap != null)
                        {
                            Debug.Log("Blb:)");

                            // NEW: Compute the world coordinate we want to sample
                            int wx = pieceWorldBaseX + x - stride; // one full chunk stride left
                            int wz = pieceWorldBaseZ + z;

                            // NEW: Convert back into neighborMap's local coordinates
                            int nx = wx - neighborBaseX;
                            int nz = wz - neighborBaseZ;

                            // NEW: Sample from neighborMap using world-space offset,
                            // instead of just taking the last band columns.
                            neighborHeight = neighborMap[nx, nz];
                        }
                        else
                        {
                            // No neighbor saved? Regenerate height deterministically
                            int wx = pieceWorldBaseX + x - stride; // world X just left of this chunk
                            int wz = pieceWorldBaseZ + z;
                            neighborHeight = terrainManager.SampleHeightAtGrid(wx, wz);
                        }

                        float ownHeight = currrentHeightMap[x, z];
                        float t = (band == 1) ? 1f : (x / (float)(band - 1));
                        currrentHeightMap[x, z] = Mathf.Lerp(neighborHeight, ownHeight, t);
                    }
                }
            }
            else if (dir == "East")
            {
                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < band; x++)
                    {
                        int xi = size - 1 - x; // position inside this chunk
                        float continued;

                        if (neighborMap != null)
                        {
                            int nx = x;
                            continued = neighborMap[nx, z];
                        }
                        else
                        {
                            int wx = pieceWorldBaseX + size + x;
                            int wz = pieceWorldBaseZ + z;
                            // Resample noise *as if it was part of the neighbor's chunk*
                            continued = terrainManager.SampleHeightAtGrid(wx, wz);
                        }

                        float own = currrentHeightMap[xi, z];
                        float t = (band == 1) ? 1f : (x / (float)(band - 1));
                        currrentHeightMap[xi, z] = Mathf.Lerp(continued, own, t);
                    }
                }
            }
            else if (dir == "South")
            {
                for (int x = 0; x < size; x++)
                {
                    for (int z = 0; z < band; z++)
                    {
                        float continued;

                        if (neighborMap != null)
                        {
                            // int nz = size - band + z; // inner band from neighbor
                            int nz = size - 1 - z; // inner band from neighbor
                            continued = neighborMap[x, nz];
                        }
                        else
                        {
                            int wx = pieceWorldBaseX + x;
                            // int wz = pieceWorldBaseZ + z - band; // [baseZ - band .. baseZ - 1]
                            int wz = pieceWorldBaseZ - (band - z); // [baseZ - band .. baseZ - 1]
                            continued = terrainManager.SampleHeightAtGrid(wx, wz);
                        }

                        float own = currrentHeightMap[x, z];
                        float t = (band == 1) ? 1f : (z / (float)(band - 1));
                        currrentHeightMap[x, z] = Mathf.Lerp(continued, own, t);
                    }
                }
            }
            else if (dir == "North")
            {
                for (int x = 0; x < size; x++)
                {
                    for (int z = 0; z < band; z++)
                    {
                        int zi = size - 1 - z;
                        float continued;

                        if (neighborMap != null)
                        {
                            int nz = z;
                            continued = neighborMap[x, nz];
                        }
                        else
                        {
                            int wx = pieceWorldBaseX + x;
                            int wz = pieceWorldBaseZ + size + z; // neighbor starts at baseZ + size
                            continued = terrainManager.SampleHeightAtGrid(wx, wz);
                        }

                        float own = currrentHeightMap[x, zi];
                        float t = (band == 1) ? 1f : (z / (float)(band - 1));
                        currrentHeightMap[x, zi] = Mathf.Lerp(continued, own, t);
                    }
                }
            }
        }
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
    public void GenerateTerrainMesh(List<string> blendDirs = null)
    {
        // If we haven’t been initialized yet (no size/coord), there’s nothing we can generate
        if (!initialized) return;

        // Ensure we have an RNG reference if needed
        if (rng == null) rng = terrainManager != null ? terrainManager.prng : null;

        // 1) Generate and SAVE our raw height map based on the CURRENT GameManager noise params
        float[,] raw = GenerateRawHeightMap();
        if (raw == null) return; // Safety

        currrentHeightMap = raw;

        // --- apply seam overrides before building mesh ---
        if (blendDirs != null && blendDirs.Count > 0)
        {
            ApplyNeighborSeams(blendDirs);
        }

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

        // 3) If we were told which edges to blend, apply the seam blending AFTER vertices are made
        if (blendDirs != null && blendDirs.Count > 0)
        {
            //ApplySeamlessBorders(vertices, blendDirs);
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

        //mesh.Clear();                 // Remove old data
        mesh.vertices = vertices;    // Set new vertices
        mesh.triangles = triangles;   // Set new indices
        mesh.uv = uvs;         // Set new UVs
        mesh.RecalculateNormals();    // Shade nicely

        // Refresh collider by reassigning the shared mesh
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}
