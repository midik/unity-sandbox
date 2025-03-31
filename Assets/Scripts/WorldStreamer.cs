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
    [Tooltip("Объект игрока (или камеры), за которым следим")]
    public Transform playerTransform;

    [Tooltip("Радиус загрузки чанков вокруг игрока (в чанках)")]
    public int loadRadius = 3; // Например, 3 -> загружена область 7x7 чанков

    [Tooltip("Как часто проверять необходимость загрузки/выгрузки (в секундах)")]
    public float checkInterval = 0.5f;

    // --- НОВОЕ: Добавляем настройку и очереди ---
    [Tooltip("Сколько чанков активировать/деактивировать максимум за кадр")]
    public int chunksPerFrame = 2; // Настрой это значение для баланса

    private Dictionary<Vector2Int, GameObject> activeChunkObjects = new Dictionary<Vector2Int, GameObject>();
    private HashSet<Vector2Int> loadingChunks = new HashSet<Vector2Int>();
    private Dictionary<Vector2Int, GameObject> chunkPool = new Dictionary<Vector2Int, GameObject>();

    private Queue<Vector2Int> activationQueue = new Queue<Vector2Int>();
    private Queue<Vector2Int> deactivationQueue = new Queue<Vector2Int>();
    // --- Конец новых полей ---

    // Координаты чанка, в котором игрок находился при последней проверке
    private Vector2Int lastPlayerChunkCoord = new Vector2Int(int.MinValue, int.MinValue);
    private float chunkSize; // Размер чанка для расчетов
    private ChunkedTerrainGenerator terrainGenerator;

    void Start()
    {
        // --- Проверки и инициализация ---
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

        HandlePreGeneratedChunks();

        // Запускаем основной цикл проверки чанков
        Debug.Log("World Streamer Initialized. Starting chunk check loop.");
        StartCoroutine(ChunkCheckLoop());

        // Генерация дорог запускается с задержкой
        if (terrainGenerator.generateRoad)
        {
            // StartCoroutine(GenerateRoadsAfterDelay(checkInterval + 0.1f));
        }
    }

    private void HandlePreGeneratedChunks()
    {
        // --- Логика инициализации с предзагрузкой из редактора ---
        activeChunkObjects.Clear();
        loadingChunks.Clear();
        chunkPool.Clear(); // Очищаем пул перед заполнением
        
        Vector2Int layerChunkCoord = GetChunkCoordFromPos(playerTransform.position); 

        Debug.Log("Searching for pre-generated chunks...");
        // Ищем существующие чанки, которые являются дочерними для TerrainGenerator
        foreach (Transform child in terrainGenerator.transform)
        {
            if (child.name.StartsWith("Chunk_"))
            {
                Vector2Int coord = GetChunkCoordFromPos(child.position);
                if (!chunkPool.ContainsKey(coord))
                {
                    // если чанк вне радиуса загрузки, деактивируем его
                    if (Mathf.Abs(coord.x - layerChunkCoord.x) > loadRadius ||
                        Mathf.Abs(coord.y - layerChunkCoord.y) > loadRadius)
                    {
                        // Деактивируем объект, чтобы не мешал
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
        // --- Конец инициализации ---
    }

    // --- ИЗМЕНЕНО: Корутина для периодической проверки и обработки очередей ---
    IEnumerator ChunkCheckLoop()
    {
        // Инициализация перед циклом
        terrainGenerator.CacheSplines(); // Кешируем сплайны здесь, один раз перед началом цикла
        Debug.Log("World Streamer loop starting.");

        // Пропускаем первый кадр
        yield return null;

        while (true) // Бесконечный цикл
        {
            // 1. Определяем, какие чанки нужны/не нужны и заполняем очереди
            UpdateChunks();

            // 2. Обрабатываем очередь активации (порциями)
            yield return ProcessActivationQueue();

            // 3. Обрабатываем очередь деактивации (порциями)
            yield return ProcessDeactivationQueue();

            // 4. Ждем перед следующей проверкой
            yield return new WaitForSeconds(checkInterval);
        }
    }

    // --- ИЗМЕНЕНО: Основная логика теперь только заполняет очереди ---
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

        // 2. Находим активные чанки для выгрузки -> добавляем в очередь деактивации
        List<Vector2Int> activeCoordsToCheck = activeChunkObjects.Keys.ToList(); // Копия ключей
        foreach (Vector2Int activeCoord in activeCoordsToCheck)
        {
            if (!requiredCoords.Contains(activeCoord))
            {
                // Проверяем, не в очереди ли уже или не грузится ли
                if (!deactivationQueue.Contains(activeCoord) && !loadingChunks.Contains(activeCoord))
                {
                    deactivationQueue.Enqueue(activeCoord);
                }
            }
        }

        // 3. Находим необходимые неактивные чанки -> добавляем в очередь активации или генерируем
        foreach (Vector2Int coordToLoad in requiredCoords)
        {
            // Если не активен и не грузится...
            if (activeChunkObjects.ContainsKey(coordToLoad) || loadingChunks.Contains(coordToLoad)) continue;
            
            // ...и не в очереди на активацию...
            if (activationQueue.Contains(coordToLoad)) continue;
            
            // .. и координаты в пределах мира
            if (!IsCoordinatesInWorld(coordToLoad)) continue;
            
            // ...проверяем пул.
            if (chunkPool.ContainsKey(coordToLoad))
            {
                // Есть в пуле -> в очередь активации
                activationQueue.Enqueue(coordToLoad);
            }
            else
            {
                // Нет в пуле -> запускаем генерацию
                loadingChunks.Add(coordToLoad);
                StartCoroutine(LoadChunkCoroutine(coordToLoad));
            }
        }
    }

    // --- НОВОЕ: Корутина для обработки очереди активации ---
    IEnumerator ProcessActivationQueue() {
        int processedCount = 0;
        while (activationQueue.Count > 0 && processedCount < chunksPerFrame) {
            Vector2Int coordToActivate = activationQueue.Dequeue();

            // Проверяем актуальность ПЕРЕД действием
            if (!IsChunkStillRequired(coordToActivate) || activeChunkObjects.ContainsKey(coordToActivate) ||
                loadingChunks.Contains(coordToActivate)) continue;
            
            if (chunkPool.TryGetValue(coordToActivate, out GameObject pooledChunk))
            {
                chunkPool.Remove(coordToActivate); // Удаляем из пула

                // Настраиваем объект
                Vector3 expectedPosition = new Vector3(coordToActivate.x * chunkSize, 0, coordToActivate.y * chunkSize);
                if (pooledChunk.transform.position != expectedPosition) {
                    Debug.LogWarning($"Chunk {coordToActivate} position incorrect in pool. Resetting.");
                    pooledChunk.transform.position = expectedPosition;
                }
                pooledChunk.transform.SetParent(this.transform, true);
                pooledChunk.name = $"Chunk_{coordToActivate.x}_{coordToActivate.y} (Pooled)";

                // Активация
                pooledChunk.SetActive(true);

                activeChunkObjects.Add(coordToActivate, pooledChunk);
                processedCount++;
                yield return null; // Пауза ПОСЛЕ активации
            } else {
                // Нет в пуле, хотя был в очереди. Логируем.
                if (!activeChunkObjects.ContainsKey(coordToActivate))
                    Debug.LogWarning($"Chunk {coordToActivate} was in activation queue but not found in pool and not active!");
            }
        }
    }

    // --- НОВОЕ: Корутина для обработки очереди деактивации ---
    IEnumerator ProcessDeactivationQueue() {
         int processedCount = 0;
         while (deactivationQueue.Count > 0 && processedCount < chunksPerFrame) {
            Vector2Int coordToDeactivate = deactivationQueue.Dequeue();

            // Проверяем актуальность ПЕРЕД действием
            if (activeChunkObjects.TryGetValue(coordToDeactivate, out GameObject chunkObject)
                && !IsChunkStillRequired(coordToDeactivate)
                && !activationQueue.Contains(coordToDeactivate)) // Не деактивируем то, что ждет активации
            {
                 // Деактивация
                 chunkObject.SetActive(false);

                 activeChunkObjects.Remove(coordToDeactivate);

                // Возвращаем в пул
                if (!chunkPool.ContainsKey(coordToDeactivate)) {
                    chunkPool.Add(coordToDeactivate, chunkObject);
                } else {
                    Debug.LogWarning($"Chunk {coordToDeactivate} already in pool during deactivation? Destroying instead.");
                    Destroy(chunkObject);
                }
                processedCount++;
            }
         }
         // Пауза в конце, если что-то обработали
         if (processedCount > 0) yield return null;
    }

    // --- ИЗМЕНЕНО: Корутина генерации (полная версия с измененным финалом) ---
    IEnumerator LoadChunkCoroutine(Vector2Int coord)
    {
        int chunkX = coord.x;
        int chunkZ = coord.y;

        // Создаем объект и добавляем базовые компоненты
        GameObject chunkObject = new GameObject($"Chunk_{chunkX}_{chunkZ} (Generating)");
        chunkObject.layer = LayerMask.NameToLayer("TerrainChunk");
        chunkObject.transform.position = new Vector3(chunkX * terrainGenerator.sizePerChunk, 0, chunkZ * terrainGenerator.sizePerChunk);
        chunkObject.transform.parent = this.transform;

        MeshFilter mf = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer mr = chunkObject.AddComponent<MeshRenderer>();
        mr.material = terrainGenerator.terrainMaterial;
        MeshCollider mc = chunkObject.AddComponent<MeshCollider>();
        mc.material = terrainGenerator.physicsMaterial;

        // Добавление кастомных компонентов
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

        // --- Генерация меша ---
        Mesh mesh = new Mesh();
        mesh.name = $"TerrainMesh_{chunkX}_{chunkZ}";
        int vertsPerLine = terrainGenerator.resolutionPerChunk + 1;
        int totalVertices = vertsPerLine * vertsPerLine;

        // Выделение NativeArrays (Allocator.Persistent)
        NativeArray<float> heights = new NativeArray<float>(totalVertices, Allocator.Persistent);
        int curveSamples = 256;
        NativeArray<float> heightCurveValues = new NativeArray<float>(curveSamples, Allocator.Persistent);
        NativeArray<float> splineFactors = new NativeArray<float>(totalVertices, Allocator.Persistent);

        // Расчет heightCurveValues
        for (int i = 0; i < curveSamples; i++)
        {
            float t = (float)i / (curveSamples - 1);
            heightCurveValues[i] = terrainGenerator.heightCurve.Evaluate(t);
        }

        // Расчет splineFactors (с yield внутри)
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

        // Настройка и запуск TerrainHeightJob
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
            heights = heights // Выходной массив
        };
        JobHandle handle = job.Schedule(totalVertices, 64);

        // Ожидание Job
        while (!handle.IsCompleted) yield return null;
        handle.Complete();

        // Создание vertices, uvs, triangles из heights
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

        // Применение к mesh и коллайдеру
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
        splineFactors.Dispose();
        // --- Конец генерации меша ---

        // ---> Решаем судьбу сгенерированного чанка <---
        bool stillRequired = IsChunkStillRequired(coord); // Используем новую проверку

        if (!chunkObject) // Проверка на уничтожение извне
        {
            Debug.LogError($"Chunk object {coord} was destroyed during generation!");
            loadingChunks.Remove(coord);
            yield break;
        }

        if (stillRequired)
        {
            // Нужен -> Активируем СРАЗУ (т.к. запросили явно)
            // chunkObject.transform.SetParent(chunkParent, true);
            chunkObject.name = $"Chunk_{coord.x}_{coord.y} (Generated)";
            chunkObject.SetActive(true);
            activeChunkObjects.Add(coord, chunkObject);
        }
        else
        {
            // Уже не нужен -> В пул
            Debug.Log($"Chunk {coord} no longer required after generation, adding to pool.");
            chunkObject.SetActive(false);
            if (!chunkPool.ContainsKey(coord))
            {
                chunkObject.transform.SetParent(this.transform, true); // Переносим под родителя стримера
                chunkPool.Add(coord, chunkObject);
            }
            else
            {
                Destroy(chunkObject); // Уничтожаем дубликат
            }
        }

        // Убираем из списка загружаемых
        loadingChunks.Remove(coord);
    }

    // --- НОВОЕ: Вспомогательная функция для проверки актуальности чанка ---
    bool IsChunkStillRequired(Vector2Int coord) {
        Vector2Int currentCenterChunk = GetChunkCoordFromPos(playerTransform.position);
        int deltaX = Mathf.Abs(coord.x - currentCenterChunk.x);
        int deltaY = Mathf.Abs(coord.y - currentCenterChunk.y);
        int effectiveRadius = loadRadius; // Можно сделать loadRadius + 1 для буфера
        return deltaX <= effectiveRadius && deltaY <= effectiveRadius;
    }

    // Вспомогательная функция для получения координат чанка из мировой позиции
    Vector2Int GetChunkCoordFromPos(Vector3 pos)
    {
        int x = Mathf.FloorToInt(pos.x / chunkSize);
        int z = Mathf.FloorToInt(pos.z / chunkSize);
        return new Vector2Int(x, z);
    }

    // Корутина для отложенной генерации дорог
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