using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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

    // Корутина для асинхронной загрузки/генерации ОДНОГО НОВОГО чанка
    IEnumerator LoadChunkCoroutine(Vector2Int coord)
    {
        GameObject chunkObject = null;

        // Вызов СИНХРОННОЙ генерации одного чанка
        try
        {
            chunkObject = terrainGenerator.GenerateSingleChunk(coord.x, coord.y);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Failed to generate chunk {coord}: {ex}", terrainGenerator);
            loadingChunks.Remove(coord);
            yield break;
        }

        // Пауза после генерации
        yield return null;

        // Проверка, нужен ли еще чанк после генерации
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
            chunkObject.name = $"Chunk_{coord.x}_{coord.y} (Generated)"; // Имя для нового
            chunkObject.transform.parent = chunkParent; // Устанавливаем родителя
            activeChunkObjects.Add(coord, chunkObject); // Добавляем в активные
        }
        else
        {
            Debug.Log($"Chunk {coord} no longer required after generation, destroying.");
            Destroy(chunkObject);
        }

        // Убираем из списка загружаемых
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