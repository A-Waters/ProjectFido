using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainManager : MonoBehaviour
{
    // =======================
    // TERRAIN & PREFAB SETTINGS
    // =======================
    [Header("Terrain Prefabs & Settings")]
    public GameObject chunkPrefab;      // Prefab for a single terrain chunk
    public int chunkSize = 100;         // Vertices per side of a chunk
    public float noiseScale = 20f;      // Global scale applied to Perlin noise
    public int ChunkStride => chunkSize - 1; // Number of units covered by one chunk

    // =======================
    // FRACTAL NOISE SETTINGS
    // =======================
    [Header("Noise Settings")]
    public float frequency = 1f;    // Base frequency for Perlin noise
    public float amplitude = 1f;    // Base amplitude for noise
    public int octaves = 4;         // Number of noise layers to sum
    public float lacunarity = 2f;   // Frequency multiplier per octave
    public float persistence = 0.5f;// Amplitude multiplier per octave

    // =======================
    // RANDOMIZATION / SEEDING
    // =======================
    [Header("Randomization / Seeding")]
    [Tooltip("Set a fixed seed for deterministic generation. Leave -1 for random.")]
    public int seed = -1;
    public System.Random prng;      // Deterministic RNG based on seed

    public int GetSeed() => seed;
    public System.Random GetRNG() => prng;

    // =======================
    // HEIGHT CONTROL
    // =======================
    public float heightMultiplier;           // Multiplies the final terrain height
    public AnimationCurve heightCurve;       // Optional curve for shaping heights
    public float minHeight;                  // Not used directly; placeholder
    public float maxHeight;                  // Not used directly; placeholder
    public float slopeSuppressionStrength;   // Suppresses high slope noise

    public Transform player;                 // Reference to player for central chunk spawning

    private Vector2 globalNoiseOffset;       // Random global offset for deterministic noise

    // =======================
    // FALLOFF SETTINGS
    // =======================
    [Header("Falloff Settings")]
    public bool useFalloff = true;          // Toggle falloff on/off
    public int falloffResolution = 256;     // Resolution of the falloff map
    public float falloffScale = 100f;       // World-space scaling for falloff map
    public Vector2 falloffScrollOffset;     // Offset to scroll falloff map
    public float[,] falloffMap;             // Precomputed falloff values
    public int falloffMapMoveSpeed = 10;    // Speed at which falloff scrolls with input

    // =======================
    // NOISE SCROLLING
    // =======================
    [Header("Runtime Noise Scroll")]
    public Vector2 runtimeNoiseOffset = Vector2.zero; // Scroll offset applied at runtime
    public float scrollSpeed = 1f;                   // Movement speed for runtime scrolling


    // =======================
    // INTERNAL DATA STRUCTURES
    // =======================
    private Dictionary<Vector2Int, TerrainChunk> groups = new(); // Tracks active chunk groups
    public HeightMapStorage heightMapStorage = new HeightMapStorage(); // Stores raw height maps
    public int edgeBlendWidth;  // Width of edge blending (for seamless neighbors)


    private void Awake()
    {
        InitializeSeed();
        //CreateChunk(Vector2Int.zero);

        if (falloffMap == null)
            falloffMap = GenerateFalloffMap(falloffResolution, 3f);

        // NEW: spawn the central 3×3 group where the player initially is (or at origin if player null)
        var startPos = player != null ? player.position : Vector3.zero;
        var startGroup = WorldToGroupCoord(startPos);
        CreateChunkGroup(startGroup);

        
    }
    private void OnValidate()
    {
        //Only run in the Editor, not during play mode
        if (Application.isPlaying)
        {
            // Clean up existing generated groups so we don't duplicate
            foreach (var kvp in groups)
            {
                if (kvp.Value != null)
                    Destroy(kvp.Value.gameObject);
            }
            groups.Clear();

            // Reinitialize seed so noise is consistent with inspector values
            InitializeSeed();

            if (falloffMap == null)
                falloffMap = GenerateFalloffMap(falloffResolution, 3f);

            // Spawn central group again (at origin if no player reference)
            var startPos = player != null ? player.position : Vector3.zero;
            var startGroup = WorldToGroupCoord(startPos);
            CreateChunkGroup(startGroup);

            
        }
    }

    private void Update()
    {
        if (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0)
        {
            // Simple WASD scroll for debugging
            float dx = Input.GetAxis("Horizontal") * scrollSpeed * Time.deltaTime;
            float dz = Input.GetAxis("Vertical") * scrollSpeed * Time.deltaTime;

            runtimeNoiseOffset += new Vector2(dx, dz);

            // If you want to rebuild visible chunks on the fly:
            foreach (var kvp in groups)
            {
                kvp.Value.RegenerateAll();
            }
        }

        if (Input.GetKey(KeyCode.I)) falloffScrollOffset.y -= falloffMapMoveSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.K)) falloffScrollOffset.y += falloffMapMoveSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.J)) falloffScrollOffset.x += falloffMapMoveSpeed * Time.deltaTime;
        if (Input.GetKey(KeyCode.L)) falloffScrollOffset.x -= falloffMapMoveSpeed * Time.deltaTime;

        if (Input.GetKey(KeyCode.I) || Input.GetKey(KeyCode.K) || Input.GetKey(KeyCode.J) || Input.GetKey(KeyCode.L))
        {
            foreach (var group in groups.Values)
                group.RegenerateAll();
        }
    }


    // ---------------- HEIGHT MAP STORAGE ----------------
    public class HeightMapStorage
    {
        public Dictionary<Vector2Int, float[,]> pieceMaps = new();


        public void SavePieceMap(Vector2Int pieceCoord, float[,] map)
        {
            if (map == null) return;
            pieceMaps[pieceCoord] = map;
        }

        public float[,] GetGroupMap(Vector2Int pieceCoord)
        {
            return pieceMaps.TryGetValue(pieceCoord, out var map) ? map : null;
        }

        public bool HasGroupMap(Vector2Int pieceCoord)
        {
            return pieceMaps.ContainsKey(pieceCoord);
        }
    }


    // ---------------- CHUNK GROUP CREATION ----------------
    public TerrainChunk CreateChunkGroup(Vector2Int groupCoord)
    {
        if (groups.ContainsKey(groupCoord))
            return groups[groupCoord];


        GameObject groupObj = new GameObject($"ChunkGroup_{groupCoord.x}_{groupCoord.y}");
        var group = groupObj.AddComponent<TerrainChunk>();

        group.Initialize(groupCoord, this);

        groups.Add(groupCoord, group);
        return group;
    }

    public bool HasGroup(Vector2Int coord)
    {
        return groups != null && groups.ContainsKey(coord);
    }

    /// <summary>
    /// Returns which OUTER edge of the *new* group should blend against existing world.
    /// "West", "East", "North", "South", or null if no neighbor group exists yet.
    /// </summary>
    

    // ---------------- COORD CONVERSIONS ----------------
    public Vector2Int WorldToChunkCoord(Vector3 worldPos)
    {
        int cx = Mathf.FloorToInt(worldPos.x / ChunkStride);
        int cy = Mathf.FloorToInt(worldPos.z / ChunkStride);
        return new Vector2Int(cx, cy);
    }

    // World -> piece coordinate (what one TerrainChunk covers)
    public Vector2Int WorldToPieceCoord(Vector3 worldPos)
    {
        int px = Mathf.FloorToInt(worldPos.x / ChunkStride);
        int pz = Mathf.FloorToInt(worldPos.z / ChunkStride);
        return new Vector2Int(px, pz);
    }

    public float SampleFalloff(float worldX, float worldZ)
    {
        Vector2 center = falloffScrollOffset;

        // Convert world space to normalized [0,1] UV space
        float nx = (worldX - center.x) / falloffScale + 0.5f;
        float ny = (worldZ - center.y) / falloffScale + 0.5f;

        // Outside of mask = no suppression (return 1)
        if (nx < 0f || nx > 1f || ny < 0f || ny > 1f)
            return 1f;

        // Bilinear interpolation for smooth sampling
        float fx = nx * (falloffResolution - 1);
        float fy = ny * (falloffResolution - 1);

        int x0 = Mathf.FloorToInt(fx);
        int x1 = Mathf.Min(x0 + 1, falloffResolution - 1);
        int y0 = Mathf.FloorToInt(fy);
        int y1 = Mathf.Min(y0 + 1, falloffResolution - 1);

        float tx = fx - x0;
        float ty = fy - y0;

        // Sample 4 nearest pixels and bilerp
        float c00 = falloffMap[x0, y0];
        float c10 = falloffMap[x1, y0];
        float c01 = falloffMap[x0, y1];
        float c11 = falloffMap[x1, y1];

        float cx0 = Mathf.Lerp(c00, c10, tx);
        float cx1 = Mathf.Lerp(c01, c11, tx);

        return Mathf.Lerp(cx0, cx1, ty);
    }

    public float[,] GenerateFalloffMap(int size, float softness = 3f)
    {
        float[,] map = new float[size, size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Convert to [-1, 1] range, so center = 0, edge = ±1
                float nx = (x / (size - 1f)) * 2f - 1f;
                float ny = (y / (size - 1f)) * 2f - 1f;

                // Distance from center, but circular not square
                float dist = Mathf.Sqrt(nx * nx + ny * ny);

                // Clamp to [0,1] and apply softness (controls steepness)
                float t = Mathf.Clamp01(dist);
                t = Mathf.Pow(t, softness);

                // Smoothstep for gradual fade-out
                float smooth = t * t * (3f - 2f * t);

                // 0 at center, 1 at edges
                map[x, y] = smooth;
            }
        }
        return map;
    }

    // Piece -> group coordinate (each group is 3×3 pieces)
    // NOTE: using floor((p + 1)/3) so group 0 covers piece coords {-1,0,1}, group 1 covers {2,3,4}, etc.
    public Vector2Int PieceToGroupCoord(Vector2Int pieceCoord)
    {
        int gx = Mathf.FloorToInt((pieceCoord.x + 1) / 3f);
        int gz = Mathf.FloorToInt((pieceCoord.y + 1) / 3f);
        return new Vector2Int(gx, gz);
    }

    public Vector2Int WorldToGroupCoord(Vector3 worldPos)
    {
        return PieceToGroupCoord(WorldToPieceCoord(worldPos));
    }

    // ---------------- SEED INITIALIZATION ----------------
    public void InitializeSeed()
    {
        if (seed == -1)
        {
            seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            Debug.Log("Generated random seed: " + seed);
        }
        else
        {
            Debug.Log("Using fixed seed: " + seed);
        }

        prng = new System.Random(seed);

        globalNoiseOffset = new Vector2(
        (float)prng.NextDouble() * 10000f,
        (float)prng.NextDouble() * 10000f
        );
    }

    // ---------------- GLOBAL NOISE OFFSET ----------------
    public Vector2 GetGlobalNoiseOffset()
    {
        return globalNoiseOffset + runtimeNoiseOffset;
    }

    [ContextMenu("Debug Print HeightMap Keys")]
    public void DebugPrintHeightMaps()
    {
        foreach (var kvp in heightMapStorage.pieceMaps)
        {
            Debug.Log($"Stored map at coord {kvp.Key}, size = {kvp.Value.GetLength(0)}x{kvp.Value.GetLength(1)}");
        }
    }

    public Texture2D GetDebugTexture(Vector2Int pieceCoord)
    {
        var map = heightMapStorage.GetGroupMap(pieceCoord);
        if (map == null) return null;

        int size = map.GetLength(0);
        Texture2D tex = new Texture2D(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float h = map[x, y];
                tex.SetPixel(x, y, new Color(h, h, h));
            }
        }
        tex.Apply();
        return tex;
    }


    /// <summary>
    /// Sample the terrain generation (noise + curve + multiplier) at a discrete world-grid coordinate.
    /// worldGridX / worldGridZ are the same integer coordinates you produce in chunks:
    ///   gx = pieceCoord.x * ChunkStride + x + globalSeedOffset.x
    /// BUT this method expects the grid indices WITHOUT the globalSeedOffset baked in.
    /// It will add the global offset internally so call with plain integer grid coords.
    /// </summary>
    public float SampleHeightAtGrid(int worldGridX, int worldGridZ)
    {
        Vector2 globalSeedOffset = GetGlobalNoiseOffset();

        float gx = worldGridX + globalSeedOffset.x;
        float gz = worldGridZ + globalSeedOffset.y;

        float sx = gx / noiseScale;
        float sy = gz / noiseScale;

        // ---- mirror chunk gen precisely ----
        float noiseHeight = 0f;
        float freq = frequency;
        float amp = amplitude;

        Vector2 runningGradient = Vector2.zero;
        float k = slopeSuppressionStrength;

        for (int o = 0; o < octaves; o++)
        {
            float perlin = Mathf.PerlinNoise(sx * freq, sy * freq) * 2f - 1f;

            // same gradient approx you use in TerrainChunk
            float eps = 0.001f;
            float f = Mathf.PerlinNoise((sx) * freq, (sy) * freq);
            float fx = Mathf.PerlinNoise((sx + eps) * freq, (sy) * freq);
            float fy = Mathf.PerlinNoise((sx) * freq, (sy + eps) * freq);
            Vector2 grad = new Vector2((fx - f) / eps, (fy - f) / eps);

            Vector2 combined = runningGradient + grad * amp;
            float suppression = 1f / (1f + k * combined.magnitude);

            noiseHeight += perlin * amp * suppression;
            runningGradient += grad * amp;

            freq *= lacunarity;
            amp *= persistence;
        }

        // same normalization + curve
        float maxPossible = 0f;
        float ampCheck = amplitude;
        for (int o = 0; o < octaves; o++) { maxPossible += ampCheck; ampCheck *= persistence; }
        float t01 = (noiseHeight + maxPossible) / (2f * maxPossible);

        float shaped = heightCurve != null ? heightCurve.Evaluate(t01) : t01;
        return shaped * heightMultiplier;
    }
}
