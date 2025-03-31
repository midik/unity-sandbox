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
    [Header("Core Settings")] [Tooltip("Объект игрока (или камеры), за которым следим")]
    public Transform playerTransform;

    [Tooltip("Радиус загрузки чанков вокруг игрока (в чанках)")]
    public int loadRadius = 3; // Например, 3 -> загружена область 7x7 чанков

    [Tooltip("Как часто проверять необходимость загрузки/выгрузки (в секундах)")]
    public float checkInterval = 0.5f;

    private Dictionary<Vector2Int, GameObject> activeChunkObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> chunkPool = new Dictionary<Vector2Int, GameObject>();

    // Координаты чанка, в котором игрок находился при последней проверке
    private Vector2Int lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);
    private float chunkSize; // Размер чанка для расчетов
    private Transform chunkParent; // Родительский объект для порядка в иерархии
    private ChunkedTerrainGenerator terrainGenerator;

    void Start()
    {
        if (!playerTransform)
        {
            Debug.LogError("WorldStreamer: Player Transform не назначен!", this);
            enabled = false;
            return;
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

        // Очистка перед стартом (удаляем старый родитель и чанки от редактора)
        Transform existingParent = transform.Find("Active Terrain Chunks");

        if (existingParent) Destroy(existingParent.gameObject);
        terrainGenerator.ClearChunks(); // Удаляем дочерние у генератора
        activeChunkObjects.Clear();
        loadingChunks.Clear();
        chunkPool.Clear(); // Очищаем пул

        // Создаем новый родительский объект
        chunkParent = new GameObject("Active Terrain Chunks").transform;
        chunkParent.parent = this.transform;

        // Кешируем сплайны - ВАЖНО сделать это до первой UpdateChunks
        Debug.Log("WorldStreamer: Calling CacheSplines...");
        terrainGenerator.CacheSplines();

        Debug.Log("World Streamer Initialized. Starting chunk check loop.");
        StartCoroutine(ChunkCheckLoop());

        // Первая проверка чанков сразу, чтобы не ждать checkInterval
        UpdateChunks();

        // Генерация дорог (после первой проверки)
        if (terrainGenerator.generateRoad)
        {
            StartCoroutine(GenerateRoadsAfterDelay(checkInterval + 0.1f));
        }
    }

    // Корутина для периодической проверки чанков
    IEnumerator ChunkCheckLoop()
    {
        // Пропускаем первый кадр, чтобы UpdateChunks в Start успел отработать
        yield return null;

        while (true) // Бесконечный цикл
        {
            UpdateChunks(); // Выполняем проверку
            yield return new WaitForSeconds(checkInterval); // Ждем интервал
        }
    }

    // Основная логика проверки и обновления чанков
    void UpdateChunks()
    {
        Vector2Int newPlayerChunkCoord = GetChunkCoordFromPos(playerTransform.position);
        if (newPlayerChunkCoord == lastPlayerChunkCoord) return;

        lastPlayerChunkCoord = newPlayerChunkCoord;

        // 1. Определяем необходимые координаты
        HashSet<Vector2Int> requiredCoords = new HashSet<Vector2Int>();
        for (int x = -loadRadius; x <= loadRadius; x++)
        {
            for (int z = -loadRadius; z <= loadRadius; z++)
            {
                requiredCoords.Add(new Vector2Int(lastPlayerChunkCoord.x + x, lastPlayerChunkCoord.y + z));
            }
        }

        // 2. Находим чанки для выгрузки
        List<Vector2Int> coordsToUnload = new List<Vector2Int>();
        foreach (Vector2Int activeCoord in activeChunkObjects.Keys)
        {
            if (!requiredCoords.Contains(activeCoord))
            {
                coordsToUnload.Add(activeCoord);
            }
        }

        // 3. Выгружаем (деактивируем и помещаем в пул-СЛОВАРЬ)
        foreach (Vector2Int coordToUnload in coordsToUnload)
        {
            if (activeChunkObjects.TryGetValue(coordToUnload, out GameObject chunkObject))
            {
                chunkObject.SetActive(false);
                if (!chunkPool.ContainsKey(coordToUnload))
                {
                    chunkPool.Add(coordToUnload, chunkObject);
                }
                else
                {
                    Debug.LogWarning($"Chunk {coordToUnload} already in pool? Destroying instead.");
                    Destroy(chunkObject); // Если уже есть в пуле, уничтожаем дубликат
                }

                // ----------------------------------------------
                activeChunkObjects.Remove(coordToUnload);
            }
        }

        // 4. Загружаем/активируем новые необходимые чанки
        foreach (Vector2Int coordToLoad in requiredCoords)
        {
            if (!activeChunkObjects.ContainsKey(coordToLoad)) // Не активен?
            {
                if (!loadingChunks.Contains(coordToLoad)) // Не загружается?
                {
                    // ---> ИЗМЕНЕНО: Проверяем наличие в пуле-СЛОВАРЕ <---
                    if (chunkPool.ContainsKey(coordToLoad))
                    {
                        // --- Используем объект из пула ---
                        GameObject pooledChunk = chunkPool[coordToLoad]; // Берем по ключу
                        chunkPool.Remove(coordToLoad); // Удаляем из пула

                        // Убедимся, что позиция правильная (хотя она не должна была меняться)
                        Vector3 expectedPosition = new Vector3(coordToLoad.x * chunkSize, 0, coordToLoad.y * chunkSize);
                        if (pooledChunk.transform.position != expectedPosition)
                        {
                            pooledChunk.transform.position = expectedPosition;
                        }

                        pooledChunk.transform.parent = chunkParent;
                        pooledChunk.name = $"Chunk_{coordToLoad.x}_{coordToLoad.y} (Pooled)";
                        pooledChunk.SetActive(true); // Активируем
                        activeChunkObjects.Add(coordToLoad, pooledChunk); // Добавляем в активные
                    }
                    // -----------------------------------------
                    else
                    {
                        // --- Пул пуст или нет нужного чанка - генерируем новый ---
                        loadingChunks.Add(coordToLoad);
                        StartCoroutine(LoadChunkCoroutine(coordToLoad));
                    }
                }
            }
        }
    }

    IEnumerator LoadChunkCoroutine(Vector2Int coord)
    {
        int chunkX = coord.x;
        int chunkZ = coord.y;

        GameObject chunkObject = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunkObject.layer = LayerMask.NameToLayer("TerrainChunk");
        chunkObject.transform.position = new Vector3(chunkX * terrainGenerator.sizePerChunk, 0, chunkZ * terrainGenerator.sizePerChunk);
        chunkObject.transform.parent = chunkParent;

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

        // Подготовка данных для Job
        NativeArray<float> heights = new NativeArray<float>(totalVertices, Allocator.TempJob);
        
        // Предвычисление кривой высот
        int curveSamples = 256;
        NativeArray<float> heightCurveValues = new NativeArray<float>(curveSamples, Allocator.TempJob);
        for (int i = 0; i < curveSamples; i++)
        {
            float t = (float)i / (curveSamples - 1);
            heightCurveValues[i] = terrainGenerator.heightCurve.Evaluate(t);
        }

        // Предвычисление влияния сплайнов - вынесено в главный поток корутины для распределения нагрузки
        NativeArray<float> splineFactors = new NativeArray<float>(totalVertices, Allocator.TempJob);
        float step = terrainGenerator.sizePerChunk / terrainGenerator.resolutionPerChunk;
        
        // Рассчитываем splineFactors в главном потоке, но распределенно по времени
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
                    if (minDistanceToSpline <= halfWidth)
                    {
                        splineFactor = 1.0f;
                    }
                    else if (minDistanceToSpline < halfWidth + terrainGenerator.splineValleyFalloff)
                    {
                        float t = (minDistanceToSpline - halfWidth) / terrainGenerator.splineValleyFalloff;
                        float smoothT = t * t * (3f - 2f * t); // Smoothstep
                        splineFactor = 1f - smoothT;
                    }

                    int index = x + z * vertsPerLine;
                    splineFactors[index] = splineFactor;

                    // Распределение нагрузки: пауза каждые 100 вершин
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

        // Запуск основного Job с предвычисленными splineFactors
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
            splineFactors = splineFactors, // Передаём предвычисленные значения влияния сплайнов
            splineValleyDepth = terrainGenerator.splineValleyDepth,
            heights = heights
        };

        // Запуск задачи асинхронно
        JobHandle handle = job.Schedule(totalVertices, 64);

        // Асинхронное ожидание завершения Job
        while (!handle.IsCompleted)
        {
            yield return null;
        }
        handle.Complete();

        // Создание меша
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

        // Освобождение памяти
        heights.Dispose();
        heightCurveValues.Dispose();
        splineFactors.Dispose(); // Обязательно освобождаем память

        // Проверка актуальности чанка
        Vector2Int latestPlayerChunkCoord = GetChunkCoordFromPos(playerTransform.position);
        bool stillRequired = false;
        int checkRadiusSq = loadRadius * loadRadius;
        if (SqrMagnitude(coord - latestPlayerChunkCoord) <= checkRadiusSq * 2)
        {
            stillRequired = true;
        }

        if (!chunkObject)
        {
            Debug.LogError($"Chunk object for {coord} is null after generation attempt.");
        }
        else if (stillRequired)
        {
            chunkObject.name = $"Chunk_{coord.x}_{coord.y} (Generated)";
            activeChunkObjects.Add(coord, chunkObject);
        }
        else
        {
            Debug.Log($"Chunk {coord} no longer required after generation, destroying.");
            Destroy(chunkObject);
        }

        loadingChunks.Remove(coord);
    }

    // Вспомогательная функция для получения координат чанка из мировой позиции
    Vector2Int GetChunkCoordFromPos(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x / chunkSize);
        int z = Mathf.FloorToInt(pos.z / chunkSize);
        return new Vector2Int(x, z);
    }

    // Вспомогательная функция для квадрата расстояния между Vector2Int
    float SqrMagnitude(Vector2Int vec)
    {
        return vec.x * vec.x + vec.y * vec.y;
    }

    IEnumerator GenerateRoadsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        Debug.Log("Generating roads after delay...");
        terrainGenerator.GenerateRoads();
    }
}