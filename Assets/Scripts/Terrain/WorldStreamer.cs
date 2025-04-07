using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Splines;

public class WorldStreamer : MonoBehaviour
{
    [Header("Core Settings")]
    [Tooltip("Объекты (игрок и NPC), за которыми следим")]
    public List<Transform> trackedTransforms = new List<Transform>();

    [Tooltip("Радиус загрузки чанков вокруг объектов (в чанках)")]
    public int loadRadius = 3; // Например, 3 -> загружена область 7x7 чанков

    [Tooltip("Как часто проверять необходимость загрузки/выгрузки (в секундах)")]
    public float checkInterval = 0.5f;

    [Tooltip("Сколько чанков активировать/деактивировать максимум за кадр")]
    public int chunksPerFrame = 2; // Настрой это значение для баланса

    private Dictionary<Vector2Int, GameObject> activeChunkObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> chunkPool = new Dictionary<Vector2Int, GameObject>();

    private Queue<Vector2Int> activationQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> deactivationQueue = new Queue<Vector2Int>();

    private Dictionary<Transform, Vector2Int> lastObjectChunkCoords = new Dictionary<Transform, Vector2Int>();
    private float chunkSize; // Размер чанка для расчетов
    private ChunkedTerrainGenerator terrainGenerator;

    void Start()
    {
        if (trackedTransforms.Count == 0)
        {
            Debug.LogError("WorldStreamer: No tracked transforms assigned!", this);
            enabled = false;
            return;
        }

        // Initialize last known positions for all tracked transforms
        foreach (var t in trackedTransforms)
        {
            if (!t)
            {
                Debug.LogWarning("WorldStreamer: Null transform in trackedTransforms list!", this);
                continue;
            }
            lastObjectChunkCoords[transform] = new Vector2Int(int.MinValue, int.MinValue);
        }

        terrainGenerator = GetComponent<ChunkedTerrainGenerator>();
        if (!terrainGenerator)
        {
            Debug.LogError("WorldStreamer: Terrain Generator (на этом же объекте) не найден!", this);
            enabled = false;
            return;
        }

        chunkSize = terrainGenerator.sizePerChunk;
        if (chunkSize <= 0)
        {
            Debug.LogError("WorldStreamer: Chunk Size is zero or negative!", this);
            enabled = false;
            return;
        }

        HandlePreGeneratedChunks();

        Debug.Log("World Streamer Initialized. Starting chunk check loop.");
        StartCoroutine(ChunkCheckLoop());

        if (terrainGenerator.generateRoad)
        {
            // StartCoroutine(GenerateRoadsAfterDelay(checkInterval + 0.1f));
        }
    }

    private void HandlePreGeneratedChunks()
    {
        activeChunkObjects.Clear();
        loadingChunks.Clear();
        chunkPool.Clear();
        
        // Get primary tracked transform (usually player) for initial chunk handling
        Transform primaryTransform = trackedTransforms.Count > 0 ? trackedTransforms[0] : null;
        if (primaryTransform == null) return;
        
        Vector2Int layerChunkCoord = GetChunkCoordFromPos(primaryTransform.position); 

        Debug.Log("Searching for pre-generated chunks...");
        foreach (Transform child in terrainGenerator.transform)
        {
            if (child.name.StartsWith("Chunk_"))
            {
                Vector2Int coord = GetChunkCoordFromPos(child.position);
                if (!chunkPool.ContainsKey(coord))
                {
                    if (Mathf.Abs(coord.x - layerChunkCoord.x) > loadRadius ||
                        Mathf.Abs(coord.y - layerChunkCoord.y) > loadRadius)
                    {
                        child.gameObject.SetActive(false);
                    }
                    
                    chunkPool.Add(coord, child.gameObject);
                }
                else
                {
                    Debug.LogWarning($"Duplicate chunk found at coord {coord} during pre-population? Object: {child.name}. Keeping the one already in pool.", child.gameObject);
                }
            }
        }
        Debug.Log($"Found and pooled {chunkPool.Count} pre-generated chunks.");
    }

    IEnumerator ChunkCheckLoop()
    {
        terrainGenerator.CacheSplines();
        Debug.Log("World Streamer loop starting.");

        yield return null;

        while (true)
        {
            UpdateChunks();

            yield return ProcessActivationQueue();

            yield return ProcessDeactivationQueue();

            yield return new WaitForSeconds(checkInterval);
        }
    }

    void UpdateChunks()
    {
        bool anyTransformMoved = false;
        HashSet<Vector2Int> requiredCoords = new HashSet<Vector2Int>();

        // Check each tracked transform for movement and collect required chunks
        foreach (var t in trackedTransforms)
        {
            if (!t) continue;

            Vector2Int newChunkCoord = GetChunkCoordFromPos(t.position);
            Vector2Int lastChunkCoord = Vector2Int.zero;
            
            if (!lastObjectChunkCoords.TryGetValue(t, out lastChunkCoord))
            {
                lastObjectChunkCoords[t] = newChunkCoord;
                lastChunkCoord = newChunkCoord;
                anyTransformMoved = true;
            }
            
            if (newChunkCoord != lastChunkCoord)
            {
                lastObjectChunkCoords[t] = newChunkCoord;
                anyTransformMoved = true;
            }

            // Add all chunks in radius around this transform to required set
            for (int x = -loadRadius; x <= loadRadius; x++)
            {
                for (int z = -loadRadius; z <= loadRadius; z++)
                {
                    requiredCoords.Add(new Vector2Int(newChunkCoord.x + x, newChunkCoord.y + z));
                }
            }
        }

        // If no transform moved, nothing to update
        if (!anyTransformMoved) return;

        // Determine chunks to deactivate
        List<Vector2Int> activeCoordsToCheck = activeChunkObjects.Keys.ToList();
        foreach (Vector2Int activeCoord in activeCoordsToCheck)
        {
            if (!requiredCoords.Contains(activeCoord))
            {
                if (!deactivationQueue.Contains(activeCoord) && !loadingChunks.Contains(activeCoord))
                {
                    deactivationQueue.Enqueue(activeCoord);
                }
            }
        }

        // Determine chunks to activate
        foreach (Vector2Int coordToLoad in requiredCoords)
        {
            if (activeChunkObjects.ContainsKey(coordToLoad) || loadingChunks.Contains(coordToLoad)) continue;
            
            if (activationQueue.Contains(coordToLoad)) continue;
            
            if (!IsCoordinatesInWorld(coordToLoad)) continue;
            
            if (chunkPool.ContainsKey(coordToLoad))
            {
                activationQueue.Enqueue(coordToLoad);
            }
            else
            {
                loadingChunks.Add(coordToLoad);
                StartCoroutine(LoadChunkCoroutine(coordToLoad));
            }
        }
    }

    IEnumerator ProcessActivationQueue() {
        int processedCount = 0;
        while (activationQueue.Count > 0 && processedCount < chunksPerFrame) {
            Vector2Int coordToActivate = activationQueue.Dequeue();

            if (!IsChunkStillRequired(coordToActivate) || activeChunkObjects.ContainsKey(coordToActivate) ||
                loadingChunks.Contains(coordToActivate)) continue;
            
            if (chunkPool.TryGetValue(coordToActivate, out GameObject pooledChunk))
            {
                chunkPool.Remove(coordToActivate);

                Vector3 expectedPosition = new Vector3(coordToActivate.x * chunkSize, 0, coordToActivate.y * chunkSize);
                if (pooledChunk.transform.position != expectedPosition) {
                    Debug.LogWarning($"Chunk {coordToActivate} position incorrect in pool. Resetting.");
                    pooledChunk.transform.position = expectedPosition;
                }
                pooledChunk.transform.SetParent(this.transform, true);
                pooledChunk.name = $"Chunk_{coordToActivate.x}_{coordToActivate.y} (Pooled)";

                pooledChunk.SetActive(true);

                activeChunkObjects.Add(coordToActivate, pooledChunk);
                processedCount++;
                yield return null;
            } else {
                if (!activeChunkObjects.ContainsKey(coordToActivate))
                    Debug.LogWarning($"Chunk {coordToActivate} was in activation queue but not found in pool and not active!");
            }
        }
    }

    IEnumerator ProcessDeactivationQueue() {
         int processedCount = 0;
         while (deactivationQueue.Count > 0 && processedCount < chunksPerFrame) {
            Vector2Int coordToDeactivate = deactivationQueue.Dequeue();

            if (activeChunkObjects.TryGetValue(coordToDeactivate, out GameObject chunkObject)
                && !IsChunkStillRequired(coordToDeactivate)
                && !activationQueue.Contains(coordToDeactivate))
            {
                 chunkObject.SetActive(false);

                 activeChunkObjects.Remove(coordToDeactivate);

                if (!chunkPool.ContainsKey(coordToDeactivate)) {
                    chunkPool.Add(coordToDeactivate, chunkObject);
                } else {
                    Debug.LogWarning($"Chunk {coordToDeactivate} already in pool during deactivation? Destroying instead.");
                    Destroy(chunkObject);
                }
                processedCount++;
            }
         }
         if (processedCount > 0) yield return null;
    }

    IEnumerator LoadChunkCoroutine(Vector2Int coord)
    {
        int chunkX = coord.x;
        int chunkZ = coord.y;

        GameObject chunkObject = new GameObject($"Chunk_{chunkX}_{chunkZ} (Generating)");
        chunkObject.layer = LayerMask.NameToLayer("TerrainChunk");
        chunkObject.transform.position = new Vector3(chunkX * terrainGenerator.sizePerChunk, 0, chunkZ * terrainGenerator.sizePerChunk);
        chunkObject.transform.parent = this.transform;

        MeshFilter mf = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer mr = chunkObject.AddComponent<MeshRenderer>();
        mr.material = terrainGenerator.terrainMaterial;
        MeshCollider mc = chunkObject.AddComponent<MeshCollider>();
        mc.material = terrainGenerator.physicsMaterial;

        if (!chunkObject.GetComponent<MeshDeformer>())
        {
            MeshDeformer md = chunkObject.AddComponent<MeshDeformer>();
            md.deformRadius = terrainGenerator.deformRadius;
            md.deformStrength = terrainGenerator.deformStrength;
            md.maxDeformDepth = terrainGenerator.maxDeformDepth;
        }
        if (!chunkObject.GetComponent<ChunkDeformerManager>())
        {
            chunkObject.AddComponent<ChunkDeformerManager>();
        }

        Mesh mesh = new Mesh();
        mesh.name = $"TerrainMesh_{chunkX}_{chunkZ}";
        int vertsPerLine = terrainGenerator.resolutionPerChunk + 1;
        int totalVertices = vertsPerLine * vertsPerLine;

        NativeArray<float> heights = new NativeArray<float>(totalVertices, Allocator.Persistent);
        int curveSamples = 256;
        NativeArray<float> heightCurveValues = new NativeArray<float>(curveSamples, Allocator.Persistent);
        NativeArray<float> splineFactors = new NativeArray<float>(totalVertices, Allocator.Persistent);

        for (int i = 0; i < curveSamples; i++)
        {
            float t = (float)i / (curveSamples - 1);
            heightCurveValues[i] = terrainGenerator.heightCurve.Evaluate(t);
        }

        float step = terrainGenerator.sizePerChunk / terrainGenerator.resolutionPerChunk;
        if (terrainGenerator.useSplineValleys && terrainGenerator.cachedSplines != null && terrainGenerator.cachedSplines.Count > 0)
        {
            for (int z = 0; z < vertsPerLine; z++)
            {
                for (int x = 0; x < vertsPerLine; x++)
                {
                    float localX = x * step;
                    float localZ = z * step;
                    float worldX = chunkObject.transform.position.x + localX;
                    float worldZ = chunkObject.transform.position.z + localZ;
                    float3 worldPos = new float3(worldX, 0, worldZ);

                    float minDistSq = float.MaxValue;
                    foreach (var spline in terrainGenerator.cachedSplines)
                    {
                        if (spline == null || spline.Knots == null || spline.Knots.Count() < 2) continue;
                        SplineUtility.GetNearestPoint(spline, worldPos, out float3 nearestPoint, out _, 3, 8);
                        float distSq = math.distancesq(new float2(worldPos.x, worldPos.z), new float2(nearestPoint.x, nearestPoint.z));
                        minDistSq = math.min(minDistSq, distSq);
                    }

                    float minDistanceToSpline = math.sqrt(minDistSq);
                    float splineFactor = 0f;
                    float halfWidth = terrainGenerator.splineValleyWidth / 2f;
                    if (terrainGenerator.cutoffToBottom)
                    {
                        splineFactor = 0f;
                    }
                    else if (minDistanceToSpline <= halfWidth)
                    {
                        splineFactor = 1.0f;
                    }
                    else if (minDistanceToSpline < halfWidth + terrainGenerator.splineValleyFalloff)
                    {
                        float t = (minDistanceToSpline - halfWidth) / terrainGenerator.splineValleyFalloff;
                        float smoothT = t * t * (3f - 2f * t);
                        splineFactor = 1f - smoothT;
                    }

                    int index = x + z * vertsPerLine;
                    splineFactors[index] = splineFactor;

                    if ((index % 100) == 0) yield return null;
                }
            }
        }
        else
        {
            for (int i = 0; i < totalVertices; i++)
            {
                splineFactors[i] = 0f;
            }
        }

        TerrainHeightJob job = new TerrainHeightJob
        {
            vertsPerLine = vertsPerLine,
            step = step,
            chunkWorldX = chunkObject.transform.position.x,
            chunkWorldZ = chunkObject.transform.position.z,
            maxHeight = terrainGenerator.maxHeight,
            terrainScale = terrainGenerator.terrainScale,
            octaves = terrainGenerator.octaves,
            persistence = terrainGenerator.persistence,
            lacunarity = terrainGenerator.lacunarity,
            noiseOffsetX = terrainGenerator.noiseOffsetX,
            noiseOffsetZ = terrainGenerator.noiseOffsetZ,
            useDomainWarping = terrainGenerator.useDomainWarping,
            domainWarpScale = terrainGenerator.domainWarpScale,
            domainWarpStrength = terrainGenerator.domainWarpStrength,
            domainWarpOffsetX = terrainGenerator.domainWarpOffsetX,
            domainWarpOffsetZ = terrainGenerator.domainWarpOffsetZ,
            heightCurveValues = heightCurveValues,
            heightCurveSamples = curveSamples,
            useValleys = terrainGenerator.useValleys,
            valleyNoiseScale = terrainGenerator.valleyNoiseScale,
            valleyDepth = terrainGenerator.valleyDepth,
            valleyWidthFactor = terrainGenerator.valleyWidthFactor,
            valleyNoiseOffsetX = terrainGenerator.valleyNoiseOffsetX,
            valleyNoiseOffsetZ = terrainGenerator.valleyNoiseOffsetZ,
            useSplineValleys = terrainGenerator.useSplineValleys,
            splineFactors = splineFactors,
            splineValleyDepth = terrainGenerator.splineValleyDepth,
            heights = heights
        };
        JobHandle handle = job.Schedule(totalVertices, 64);

        while (!handle.IsCompleted) yield return null;
        handle.Complete();

        Vector3[] vertices = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];
        for (int i = 0; i < totalVertices; i++)
        {
            int x = i % vertsPerLine;
            int z = i / vertsPerLine;
            float localX = x * step;
            float localZ = z * step;
            vertices[i] = new Vector3(localX, heights[i], localZ);
            uvs[i] = new Vector2((float)x / terrainGenerator.resolutionPerChunk, (float)z / terrainGenerator.resolutionPerChunk);
        }

        int[] triangles = new int[terrainGenerator.resolutionPerChunk * terrainGenerator.resolutionPerChunk * 6];
        int tri = 0;
        for (int z = 0; z < terrainGenerator.resolutionPerChunk; z++)
        {
            for (int x = 0; x < terrainGenerator.resolutionPerChunk; x++)
            {
                int row = z * vertsPerLine;
                int nextRow = (z + 1) * vertsPerLine;
                int current = row + x;
                triangles[tri++] = current;
                triangles[tri++] = nextRow + x;
                triangles[tri++] = current + 1;
                triangles[tri++] = current + 1;
                triangles[tri++] = nextRow + x;
                triangles[tri++] = nextRow + x + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.mesh = mesh;
        mc.sharedMesh = mesh;

        heights.Dispose();
        heightCurveValues.Dispose();
        splineFactors.Dispose();

        bool stillRequired = IsChunkStillRequired(coord);

        if (!chunkObject)
        {
            Debug.LogError($"Chunk object {coord} was destroyed during generation!");
            loadingChunks.Remove(coord);
            yield break;
        }

        if (stillRequired)
        {
            chunkObject.name = $"Chunk_{coord.x}_{coord.y} (Generated)";
            chunkObject.SetActive(true);
            activeChunkObjects.Add(coord, chunkObject);
        }
        else
        {
            Debug.Log($"Chunk {coord} no longer required after generation, adding to pool.");
            chunkObject.SetActive(false);
            if (!chunkPool.ContainsKey(coord))
            {
                chunkObject.transform.SetParent(this.transform, true);
                chunkPool.Add(coord, chunkObject);
            }
            else
            {
                Destroy(chunkObject);
            }
        }

        loadingChunks.Remove(coord);
    }

    bool IsChunkStillRequired(Vector2Int coord) {
        foreach (var transform in trackedTransforms)
        {
            if (transform == null) continue;
            
            Vector2Int objectChunk = GetChunkCoordFromPos(transform.position);
            int deltaX = Mathf.Abs(coord.x - objectChunk.x);
            int deltaY = Mathf.Abs(coord.y - objectChunk.y);
            
            // If chunk is within load radius of any tracked transform, it's required
            if (deltaX <= loadRadius && deltaY <= loadRadius)
                return true;
        }
        return false;
    }

    Vector2Int GetChunkCoordFromPos(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x / chunkSize);
        int z = Mathf.FloorToInt(pos.z / chunkSize);
        return new Vector2Int(x, z);
    }

    IEnumerator GenerateRoadsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Debug.Log("Generating roads after delay...");
        if (terrainGenerator)
        {
             terrainGenerator.GenerateRoads();
        } else {
             Debug.LogError("Cannot generate roads, TerrainGenerator is missing!");
        }
    }
    
    bool IsCoordinatesInWorld(Vector2Int coord)
    {
        return coord.x >= 0 && coord.y >= 0 && coord.x < terrainGenerator.chunksX && coord.y < terrainGenerator.chunksZ;
    }
}
