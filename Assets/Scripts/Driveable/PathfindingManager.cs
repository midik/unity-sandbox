using System.Collections.Generic;
using UnityEngine;

// Требуется реализация PriorityQueue, например, из стандартных коллекций или кастомная.
// Если у тебя ее нет, сообщи - могу предоставить простой вариант.
// using PriorityQueue; // Пример пространства имен

public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }
    public WorldStreamer worldStreamer; // Убедись, что ссылка назначена в инспекторе

    [Header("Grid Settings")] public float cellSize = 1f; // Размер ячейки сетки пути

    [Header("Path Costs")] public float roadCost = 1f; // Стоимость движения по дороге
    public float defaultCost = 5f; // Стандартная стоимость движения
    public float heightCostMultiplier = 0.5f; // Влияние высоты на стоимость

    [Header("Path Simplification")] [Tooltip("Минимальное желаемое расстояние между точками в итоговом пути")]
    public float minPathPointDistance = 5f; // Желаемое расстояние между точками пути

    [Tooltip("Слои, считающиеся проходимой поверхностью для определения высоты точек пути")]
    public LayerMask walkableLayerMask; // Настрой в инспекторе (например, Terrain, Road)

    private int worldSizeX;
    private int worldSizeZ;
    private float[,] costMap; // Карта стоимостей для A*


    void Awake()
    {
        // Синглтон
        if (!Instance)
        {
            Instance = this;
            // DontDestroyOnLoad(gameObject); // Раскомментируй, если менеджер должен жить между сценами
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (!worldStreamer)
        {
            Debug.LogError("WorldStreamer не назначен в PathfindingManager!", this);
            enabled = false; // Отключаем компонент, если нет зависимости
            return;
        }
    }

    void Start()
    {
        if (!worldStreamer.terrainGenerator)
        {
            Debug.LogError("TerrainGenerator не найден в WorldStreamer на момент Start!", this);
            enabled = false;
            return;
        }

        InitializePathfinding();
    }
    
    void InitializePathfinding()
    {
        worldSizeX = worldStreamer.terrainGenerator.chunksX * worldStreamer.terrainGenerator.sizePerChunk;
        worldSizeZ = worldStreamer.terrainGenerator.chunksZ * worldStreamer.terrainGenerator.sizePerChunk;

        GenerateCostMap(); // Генерируем карту стоимостей
        // Подписываемся на событие регенерации чанков, чтобы обновить карту
        ChunkedTerrainGenerator.OnChunksRegenerated += GenerateCostMap;
        Debug.Log("Pathfinding Initialized.");
    }

    void OnDestroy()
    {
        // Отписываемся от события при уничтожении объекта, чтобы избежать утечек памяти
        ChunkedTerrainGenerator.OnChunksRegenerated -= GenerateCostMap;

        if (Instance == this)
        {
            Instance = null; // Очищаем ссылку на синглтон
        }
    }

    /// <summary>
    /// Генерирует карту стоимостей для каждой ячейки мира.
    /// </summary>
     public void GenerateCostMap()
    {
        if (worldSizeX <= 0 || worldSizeZ <= 0) {
             Debug.LogError("Неверные размеры мира для генерации карты стоимостей.", this);
             return;
        }

        costMap = new float[worldSizeX, worldSizeZ];
        Debug.Log($"Generating cost map for size: {worldSizeX}x{worldSizeZ}");

        int impassableCount = 0; // Счетчик непроходимых ячеек

        for (int x = 0; x < worldSizeX; x++)
        {
            for (int z = 0; z < worldSizeZ; z++)
            {
                // Центр ячейки для Raycast
                float worldX = x * cellSize + cellSize / 2f;
                float worldZ = z * cellSize + cellSize / 2f;
                Vector3 rayStartPos = new Vector3(worldX, 500f, worldZ); // Начинаем луч высоко
                float rayDistance = 1000f; // Длина луча

                if (Physics.Raycast(rayStartPos, Vector3.down, out RaycastHit hit, rayDistance, walkableLayerMask)) // Используем walkableLayerMask
                {
                    // Рассчитываем стоимость на основе высоты и типа поверхности
                    float heightCost = hit.point.y * heightCostMultiplier;
                    float finalCost = defaultCost + heightCost;

                    // Проверяем слой объекта, в который попал луч
                    if (hit.collider.gameObject.layer == LayerMask.NameToLayer("Road")) // Убедись, что слой "Road" существует
                    {
                        finalCost = roadCost;
                    }
                    // Можно добавить другие условия для разных типов местности

                    costMap[x, z] = Mathf.Max(1f, finalCost); // Стоимость не должна быть <= 0
                }
                else
                {
                    // Если под ячейкой нет земли (пропасть), делаем ее непроходимой
                    costMap[x, z] = float.MaxValue;
                    impassableCount++; // Увеличиваем счетчик
                    // --- ДОБАВЛЕНО: Лог для ячеек, где Raycast не сработал ---
                    // Закомментируй, если логов будет слишком много
                    // Debug.LogWarning($"Raycast failed for cell ({x}, {z}). Marked as impassable.");
                    // --- КОНЕЦ ДОБАВЛЕННОГО ---
                }
            }
        }
        // --- ДОБАВЛЕНО: Лог о количестве непроходимых ячеек ---
        Debug.Log($"Cost map generated successfully. Impassable cells: {impassableCount} out of {worldSizeX * worldSizeZ}");
        // --- КОНЕЦ ДОБАВЛЕННОГО ---
    }

    /// <summary>
    /// Находит путь между двумя точками в мире с использованием A* и упрощает его.
    /// </summary>
    /// <param name="startPos">Начальная точка пути.</param>
    /// <param name="endPos">Конечная точка пути.</param>
    /// <returns>Список точек упрощенного пути или пустой список, если путь не найден.</returns>
    public List<Vector3> FindPath(Vector3 startPos, Vector3 endPos)
    {
        // Проверяем, инициализирована ли карта стоимостей
        if (costMap == null)
        {
            Debug.LogError("Карта стоимостей (costMap) не инициализирована. Поиск пути невозможен.", this);
            return new List<Vector3>();
        }

        Vector2Int startCell = WorldToCell(startPos);
        Vector2Int endCell = WorldToCell(endPos);
        
        // --- ДОБАВЛЕНО: Логирование стоимости стартовой и конечной ячеек ---
        float startCellCost = IsCellValid(startCell) ? costMap[startCell.x, startCell.y] : -1f; // -1f если ячейка вне карты
        float endCellCost = IsCellValid(endCell) ? costMap[endCell.x, endCell.y] : -1f;
        Debug.Log($"FindPath: StartCell={startCell}, Cost={startCellCost} | EndCell={endCell}, Cost={endCellCost}");
        // --- КОНЕЦ ДОБАВЛЕННОГО ---

        // Проверка валидности ячеек
        if (!IsCellValid(startCell) || !IsCellValid(endCell))
        {
            Debug.LogWarning($"Начальная ({startCell}) или конечная ({endCell}) ячейка вне допустимых границ мира.",
                this);
            return new List<Vector3>();
        }
        
        // --- ИЗМЕНЕНО: Используем проверенные значения стоимости ---
        if (startCellCost == float.MaxValue || endCellCost == float.MaxValue) {
            Debug.LogWarning($"Начальная ({startCell}, Cost={startCellCost}) или конечная ({endCell}, Cost={endCellCost}) ячейка непроходима.", this);
            return new List<Vector3>();
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---

        // Структуры данных для A*
        PriorityQueue<Vector2Int> openSet = new PriorityQueue<Vector2Int>(); // Очередь с приоритетом
        openSet.Enqueue(startCell, 0);

        Dictionary<Vector2Int, Vector2Int>
            cameFrom = new Dictionary<Vector2Int, Vector2Int>(); // Откуда пришли в ячейку
        Dictionary<Vector2Int, float> costSoFar = new Dictionary<Vector2Int, float>(); // Стоимость пути до ячейки

        // Инициализация для стартовой ячейки
        cameFrom[startCell] = startCell; // Сама на себя ссылается
        costSoFar[startCell] = 0;

        // Основной цикл A*
        while (openSet.Count > 0)
        {
            Vector2Int current = openSet.Dequeue(); // Берем ячейку с наименьшей оценкой

            // Цель достигнута
            if (current == endCell)
                break;

            // Обработка соседей
            foreach (Vector2Int next in GetNeighbors(current))
            {
                // Пропускаем непроходимые ячейки
                if (costMap[next.x, next.y] == float.MaxValue) continue;

                // Рассчитываем новую стоимость пути до соседа
                float newCost = costSoFar[current] + costMap[next.x, next.y]; // Используем стоимость из карты

                // Если нашли более короткий путь до соседа или еще не были там
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost; // Обновляем стоимость
                    float priority = newCost + Heuristic(next, endCell); // Приоритет = стоимость + эвристика
                    openSet.Enqueue(next, priority); // Добавляем/обновляем в очереди
                    cameFrom[next] = current; // Запоминаем, откуда пришли
                }
            }
        }

        // --- Восстановление и упрощение пути ---
        List<Vector3> detailedPath = ReconstructPath(cameFrom, startPos, startCell, endCell);

        if (detailedPath.Count == 0 && startCell != endCell)
        {
            Debug.LogWarning($"Путь от {startCell} до {endCell} не найден!", this);
            return detailedPath; // Возвращаем пустой список
        }

        // Упрощаем путь перед возвратом
        List<Vector3> simplifiedPath = SimplifyPathByDistance(detailedPath, minPathPointDistance);

        // Debug.Log($"Original path points: {detailedPath.Count}, Simplified path points: {simplifiedPath.Count}");

        return simplifiedPath; // Возвращаем упрощенный путь
    }

    /// <summary>
    /// Восстанавливает путь из данных A*.
    /// </summary>
    private List<Vector3> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector3 startPos,
        Vector2Int startCell, Vector2Int endCell)
    {
        // draw path for debugging
        Debug.DrawLine(startPos, CellToWorld(endCell), Color.red, 5f);
        
        List<Vector3> path = new List<Vector3>();
        Vector2Int currentCell = endCell;

        // Если конечная точка недостижима
        if (!cameFrom.ContainsKey(endCell))
        {
            return path; // Возвращаем пустой путь
        }

        // Идем от конечной точки к начальной по словарю cameFrom
        while (currentCell != startCell)
        {
            path.Add(CellToWorld(currentCell)); // Добавляем мировую позицию ячейки
            // Безопасный доступ к словарю
            if (!cameFrom.TryGetValue(currentCell, out currentCell))
            {
                Debug.LogError(
                    $"Ошибка восстановления пути: ключ {currentCell} отсутствует в cameFrom. Путь может быть неполным.",
                    this);
                path.Reverse(); // Разворачиваем то, что успели собрать
                return path; // Возвращаем неполный путь
            }
        }

        // Добавляем начальную позицию (она точнее, чем центр стартовой ячейки)
        path.Add(startPos);

        // Разворачиваем список, чтобы путь был от начала к концу
        path.Reverse();

        return path;
    }


    /// <summary>
    /// Упрощает путь, оставляя точки на расстоянии не менее minDistance друг от друга.
    /// </summary>
    /// <param name="path">Исходный детальный путь.</param>
    /// <param name="minDistance">Минимальное расстояние между точками.</param>
    /// <returns>Упрощенный путь.</returns>
    private List<Vector3> SimplifyPathByDistance(List<Vector3> path, float minDistance)
    {
        if (path == null || path.Count <= 2)
        {
            return path; // Нечего упрощать
        }

        List<Vector3> simplifiedPath = new List<Vector3>();
        simplifiedPath.Add(path[0]); // Всегда добавляем начальную точку

        Vector3 lastAddedPoint = path[0];
        // Используем квадрат расстояния для оптимизации (избегаем вызова Sqrt)
        float sqrMinDistance = minDistance * minDistance;

        for (int i = 1; i < path.Count - 1; i++) // Идем до предпоследней точки
        {
            // Если квадрат расстояния до последней добавленной точки больше или равен минимальному
            if ((path[i] - lastAddedPoint).sqrMagnitude >= sqrMinDistance)
            {
                simplifiedPath.Add(path[i]); // Добавляем текущую точку
                lastAddedPoint = path[i]; // Обновляем последнюю добавленную точку
            }
        }

        // Всегда добавляем самую последнюю точку исходного пути, чтобы точно дойти до цели
        simplifiedPath.Add(path[path.Count - 1]);

        return simplifiedPath;
    }


    /// <summary>
    /// Возвращает соседей ячейки (только по 4 направлениям).
    /// </summary>
    private IEnumerable<Vector2Int> GetNeighbors(Vector2Int cell)
    {
        // Смещения для соседей (вправо, влево, вверх, вниз)
        var directions = new Vector2Int[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1)
            // Можно добавить диагонали при необходимости:
            // new Vector2Int(1, 1), new Vector2Int(1, -1),
            // new Vector2Int(-1, 1), new Vector2Int(-1, -1)
            // Но тогда нужно скорректировать расчет стоимости и эвристики
        };

        foreach (var dir in directions)
        {
            Vector2Int next = cell + dir;
            // Проверяем, находится ли сосед в пределах карты
            if (IsCellValid(next))
            {
                yield return next; // Возвращаем валидного соседа
            }
        }
    }

    /// <summary>
    /// Проверяет, находится ли ячейка в пределах карты мира.
    /// </summary>
    private bool IsCellValid(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < worldSizeX && cell.y >= 0 && cell.y < worldSizeZ;
    }

    /// <summary>
    /// Эвристика Манхэттенского расстояния для A*.
    /// </summary>
    private float Heuristic(Vector2Int a, Vector2Int b)
    {
        // Подходит для движения по 4 направлениям
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        // Для движения по 8 направлениям лучше использовать диагональное расстояние:
        // float dx = Mathf.Abs(a.x - b.x);
        // float dy = Mathf.Abs(a.y - b.y);
        // return Mathf.Max(dx, dy) + (Mathf.Sqrt(2) - 1) * Mathf.Min(dx, dy); // Или просто Max(dx, dy)
    }

    /// <summary>
    /// Преобразует мировые координаты в координаты ячейки сетки.
    /// </summary>
    private Vector2Int WorldToCell(Vector3 pos)
    {
        // Убедимся, что деление на ноль не произойдет
        if (cellSize <= 0)
        {
            Debug.LogError("CellSize равен нулю или меньше, невозможно преобразовать координаты.", this);
            return Vector2Int.zero;
        }

        int x = Mathf.FloorToInt(pos.x / cellSize);
        int z = Mathf.FloorToInt(pos.z / cellSize);
        // Ограничиваем значения границами карты на всякий случай
        x = Mathf.Clamp(x, 0, worldSizeX - 1);
        z = Mathf.Clamp(z, 0, worldSizeZ - 1);
        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Преобразует координаты ячейки в мировые координаты (центр ячейки с реальной высотой).
    /// </summary>
    private Vector3 CellToWorld(Vector2Int cell)
    {
        // Центр ячейки по X и Z
        float worldX = cell.x * cellSize + cellSize / 2f;
        float worldZ = cell.y * cellSize + cellSize / 2f;
        float worldY = 0f; // Высота по умолчанию

        // Ищем высоту поверхности под центром ячейки
        Vector3 rayStart = new Vector3(worldX, 1000f, worldZ); // Луч сверху вниз
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2000f,
                walkableLayerMask)) // Используем маску проходимых слоев
        {
            worldY = hit.point.y; // Используем высоту точки попадания луча
        }
        else
        {
            // Если поверхность не найдена (например, над пропастью)
            // Можно попробовать найти высоту ближайшей валидной ячейки или использовать запасное значение
            // Debug.LogWarning($"Не удалось определить высоту для ячейки {cell}. Используется Y=0.", this);
            // Как вариант, можно взять высоту из costMap, если она там хранится осмысленно,
            // но costMap хранит стоимость, а не высоту. Лучше оставить 0 или найти соседа.
        }

        return new Vector3(worldX, worldY, worldZ);
    }
}

// Не забудь добавить или убедиться в наличии реализации PriorityQueue<T>
// Пример простой реализации (если нужна):
/*
public class PriorityQueue<T>
{
    private List<KeyValuePair<T, float>> elements = new List<KeyValuePair<T, float>>();

    public int Count => elements.Count;

    public void Enqueue(T item, float priority)
    {
        elements.Add(new KeyValuePair<T, float>(item, priority));
    }

    // Не самая эффективная реализация Dequeue, но простая
    public T Dequeue()
    {
        int bestIndex = 0;
        for (int i = 1; i < elements.Count; i++)
        {
            if (elements[i].Value < elements[bestIndex].Value)
            {
                bestIndex = i;
            }
        }

        T bestItem = elements[bestIndex].Key;
        elements.RemoveAt(bestIndex);
        return bestItem;
    }
}
*/