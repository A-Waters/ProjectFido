using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TerrainManager : MonoBehaviour
{
    [Header("Terrain Prefabs & Settings")]
    public GameObject chunkPrefab;   // Assign your TerrainChunk prefab
    public int chunkSize = 100;
    public float noiseScale = 20f;
    public int ChunkStride => chunkSize - 1;

    [Header("Noise Settings")]
    public float frequency = 1f;
    public float amplitude = 1f;
    public int octaves = 4;
    public float lacunarity = 2f;
    public float persistence = 0.5f;

    [Header("Randomization / Seeding")]
    [Tooltip("Set a fixed seed for deterministic world generation. Leave -1 for a random seed.")]
    public int seed = -1;
    public System.Random prng;
    public int GetSeed() => seed;
    public System.Random GetRNG() => prng;

    public float heightMultiplier;

    public AnimationCurve heightCurve;

    public float minHeight;
    public float maxHeight;

    public Transform player;

    public float slopeSuppressionStrength;

    [Header("Debug")]
    public DebugDirection debugSpawnDirection = DebugDirection.None;

    // Keep track of existing chunk groups
    private Dictionary<Vector2Int, TerrainChunk> groups = new Dictionary<Vector2Int, TerrainChunk>();

    public HeightMapStorage heightMapStorage = new HeightMapStorage();

    public int edgeBlendWidth;

    private void Awake()
    {
        InitializeSeed();
        //CreateChunk(Vector2Int.zero);

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
            //// Clean up existing generated groups so we don't duplicate
            //foreach (var kvp in groups)
            //{
            //    if (kvp.Value != null)
            //        Destroy(kvp.Value.gameObject);
            //}
            //groups.Clear();

            //// Reinitialize seed so noise is consistent with inspector values
            //InitializeSeed();

            //// Spawn central group again (at origin if no player reference)
            //var startPos = player != null ? player.position : Vector3.zero;
            //var startGroup = WorldToGroupCoord(startPos);
            //CreateChunkGroup(startGroup);

            if (debugSpawnDirection != DebugDirection.None && Application.isPlaying && player != null)
            {
                Vector2Int baseGroup = WorldToGroupCoord(player.position);
                Vector2Int delta = Vector2Int.zero;

                switch (debugSpawnDirection)
                {
                    case DebugDirection.North: delta = new Vector2Int(0, 1); break;
                    case DebugDirection.South: delta = new Vector2Int(0, -1); break;
                    case DebugDirection.East: delta = new Vector2Int(1, 0); break;
                    case DebugDirection.West: delta = new Vector2Int(-1, 0); break;
                }

                Vector2Int targetGroup = baseGroup + delta;
                CreateChunkGroup(targetGroup);

                debugSpawnDirection = DebugDirection.None; // reset
            }
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
    public enum DebugDirection
    {
        None,
        North,
        South,
        East,
        West
    }


    public void SpawnNeighbor(DebugDirection direction)
    {
        if (!Application.isPlaying || player == null)
            return;

        // 1. Get the player's current group coordinate
        Vector2Int baseGroup = WorldToGroupCoord(player.position);

        // 2. Choose an offset based on the requested direction
        Vector2Int delta = Vector2Int.zero;
        switch (direction)
        {
            case DebugDirection.North: delta = new Vector2Int(0, 1); break;
            case DebugDirection.South: delta = new Vector2Int(0, -1); break;
            case DebugDirection.East: delta = new Vector2Int(1, 0); break;
            case DebugDirection.West: delta = new Vector2Int(-1, 0); break;
            case DebugDirection.None: return; // no spawn
        }

        // 3. Add offset to base group  target neighbor
        Vector2Int targetGroup = baseGroup + delta;

        // 4. Create the chunk group if it doesn’t exist
        CreateChunkGroup(targetGroup);
    }

    // ---------------- CHUNK GROUP CREATION ----------------
    public TerrainChunk CreateChunkGroup(Vector2Int groupCoord)
    {
        if (groups.ContainsKey(groupCoord))
            return groups[groupCoord];

        // Detect which outer edge of THIS group should blend
        string spawnEdge = ComputeGroupSpawnEdge(groupCoord);

        GameObject groupObj = new GameObject($"ChunkGroup_{groupCoord.x}_{groupCoord.y}");
        var group = groupObj.AddComponent<TerrainChunk>();

        group.Initialize(groupCoord, this, spawnEdge);

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
    public string ComputeGroupSpawnEdge(Vector2Int newGroupCoord)
    {
        if (HasGroup(newGroupCoord + Vector2Int.left)) return "West";
        if (HasGroup(newGroupCoord + Vector2Int.right)) return "East";
        if (HasGroup(newGroupCoord + Vector2Int.down)) return "South";
        if (HasGroup(newGroupCoord + Vector2Int.up)) return "North";
        return null;
    }

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
    }

    // ---------------- GLOBAL NOISE OFFSET ----------------
    public Vector2 GetGlobalNoiseOffset()
    {
        var r = new System.Random(seed);
        float ox = (float)r.NextDouble() * 10000f;
        float oy = (float)r.NextDouble() * 10000f;
        return new Vector2(ox, oy);
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
