using System.Collections.Generic;
using UnityEngine;

public class PathfindingManager : MonoBehaviour
{
    public static PathfindingManager Instance { get; private set; }
    public WorldStreamer worldStreamer; // Assign in inspector

    [Header("Grid Settings")]
    public float cellSize = 1f; // Path grid cell size

    [Header("Path Costs")]
    [Tooltip("Cost for traversing the main road surface.")]
    public float roadCost = 1.0f; // Cost for regular road
    [Tooltip("Cost for traversing the preferred center strip of the road.")]
    public float roadCenterCost = 0.5f; // Lower cost for the center strip!
    [Tooltip("Default cost for traversing terrain (non-road).")]
    public float defaultCost = 5.0f; // Default terrain cost
    [Tooltip("Multiplier for adding cost based on terrain height.")]
    public float heightCostMultiplier = 0.5f; // Height penalty

    [Header("Layer Names")]
    [Tooltip("Name of the layer used for the main road surface.")]
    public string roadLayerName = "Road"; // Assuming the edges/shoulders mesh is on "Road" layer now
    [Tooltip("Name of the layer used for the road's center strip (must exist!).")]
    public string roadCenterLayerName = "RoadCenter"; // Must match the layer used in ChunkedTerrainGenerator

    [Header("Path Simplification")]
    [Tooltip("Minimum desired distance between points in the final path.")]
    public float minPathPointDistance = 3.0f; // Increased default distance slightly

    [Header("Masks")]
    [Tooltip("Layers considered walkable for pathfinding raycasts (MUST include Terrain, Road, and RoadCenter layers).")]
    public LayerMask walkableLayerMask; // Assign in inspector (TerrainChunk, Road, RoadCenter)

    private int worldSizeX;
    private int worldSizeZ;
    private float[,] costMap; // Cost map for A*

    private int roadLayerId = -1;
    private int roadCenterLayerId = -1;


    void Awake()
    {
        // Singleton pattern
        if (!Instance)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        if (worldStreamer == null)
        {
            Debug.LogError("WorldStreamer is not assigned in PathfindingManager!", this);
            enabled = false; // Disable if dependency is missing
            return;
        }

        // Get layer IDs for faster comparison later
        roadLayerId = LayerMask.NameToLayer(roadLayerName);
        roadCenterLayerId = LayerMask.NameToLayer(roadCenterLayerName);

        if (roadLayerId == -1)
        {
            Debug.LogError($"PathfindingManager: Layer '{roadLayerName}' not found! Please ensure it exists.", this);
            // Optionally disable component or handle error
        }
        if (roadCenterLayerId == -1)
        {
             Debug.LogError($"PathfindingManager: Layer '{roadCenterLayerName}' not found! Please ensure it exists.", this);
             // Optionally disable component or handle error
        }
    }

    void Start()
    {
        // Ensure TerrainGenerator is available
        if (!worldStreamer.terrainGenerator)
        {
            Debug.LogError("TerrainGenerator not found in WorldStreamer at Start!", this);
            enabled = false;
            return;
        }
        InitializePathfinding();
    }

    void InitializePathfinding()
    {
        if (worldStreamer == null || worldStreamer.terrainGenerator == null) {
             Debug.LogError("Initialization failed: WorldStreamer or TerrainGenerator is missing.", this);
             return;
        }
        worldSizeX = worldStreamer.terrainGenerator.chunksX * worldStreamer.terrainGenerator.sizePerChunk;
        worldSizeZ = worldStreamer.terrainGenerator.chunksZ * worldStreamer.terrainGenerator.sizePerChunk;

        GenerateCostMap(); // Generate the cost map

        // Subscribe to chunk regeneration events to update the cost map
        ChunkedTerrainGenerator.OnChunksRegenerated -= GenerateCostMap; // Unsubscribe first to prevent duplicates
        ChunkedTerrainGenerator.OnChunksRegenerated += GenerateCostMap;
        Debug.Log("Pathfinding Initialized.");
    }

    void OnDestroy()
    {
        // Unsubscribe from events to prevent memory leaks
        ChunkedTerrainGenerator.OnChunksRegenerated -= GenerateCostMap;

        if (Instance == this)
        {
            Instance = null; // Clear singleton reference
        }
    }

    /// <summary>
    /// Generates the cost map used by the A* algorithm.
    /// </summary>
     public void GenerateCostMap()
    {
        if (worldSizeX <= 0 || worldSizeZ <= 0) {
             Debug.LogError("Invalid world size for cost map generation.", this);
             return;
        }
        // Check if layer IDs are valid before proceeding
        if (roadLayerId == -1 || roadCenterLayerId == -1) {
             Debug.LogError("Cannot generate cost map: Required road layers not found.", this);
             return;
        }


        costMap = new float[worldSizeX, worldSizeZ];
        Debug.Log($"Generating cost map for size: {worldSizeX}x{worldSizeZ}");

        int impassableCount = 0; // Counter for impassable cells

        for (int x = 0; x < worldSizeX; x++)
        {
            for (int z = 0; z < worldSizeZ; z++)
            {
                // Center of the cell for Raycast
                float worldX = x * cellSize + cellSize / 2f;
                float worldZ = z * cellSize + cellSize / 2f;
                // Start raycast high enough to be above potential terrain/roads
                Vector3 rayStartPos = new Vector3(worldX, 500f, worldZ); // Adjust Y start if needed
                float rayDistance = 1000f; // Adjust distance if needed

                // Raycast downwards using the walkableLayerMask
                if (Physics.Raycast(rayStartPos, Vector3.down, out RaycastHit hit, rayDistance, walkableLayerMask))
                {
                    float finalCost;
                    int hitLayer = hit.collider.gameObject.layer;

                    // 1. Проверяем слой центральной полосы (самый низкий приоритет/стоимость)
                    if (hitLayer == roadCenterLayerId)
                    {
                        finalCost = roadCenterCost;
                    }
                    // 2. Если не центр, проверяем слой обычной дороги
                    else if (hitLayer == roadLayerId)
                    {
                        finalCost = roadCost;
                    }
                    // 3. Иначе - это обычный проходимый террейн
                    else
                    {
                        // Calculate cost based on default cost and height penalty
                        float heightCost = hit.point.y * heightCostMultiplier;
                        finalCost = defaultCost + heightCost;
                    }

                    // Ensure cost is at least 1 (or a small positive value) to avoid issues with A*
                    costMap[x, z] = Mathf.Max(0.1f, finalCost);
                }
                else
                {
                    // If raycast doesn't hit anything in walkableLayerMask, mark as impassable
                    costMap[x, z] = float.MaxValue;
                    impassableCount++; // Increment counter
                }
            }
        }
        Debug.Log($"Cost map generated successfully. Impassable cells: {impassableCount} out of {worldSizeX * worldSizeZ}");
    }

    /// <summary>
    /// Finds a path between two world positions using A* and simplifies it.
    /// </summary>
    /// <param name="startPos">Starting world position.</param>
    /// <param name="endPos">Ending world position.</param>
    /// <returns>A list of Vector3 points representing the simplified path, or an empty list if no path is found.</returns>
    public List<Vector3> FindPath(Vector3 startPos, Vector3 endPos)
    {
        // Check if cost map is initialized
        if (costMap == null)
        {
            Debug.LogError("Cost map is not initialized. Cannot find path.", this);
            return new List<Vector3>();
        }

        Vector2Int startCell = WorldToCell(startPos);
        Vector2Int endCell = WorldToCell(endPos);

        // Log costs of start/end cells for debugging
        float startCellCost = IsCellValid(startCell) ? costMap[startCell.x, startCell.y] : -1f;
        float endCellCost = IsCellValid(endCell) ? costMap[endCell.x, endCell.y] : -1f;
        // Debug.Log($"FindPath Request: StartCell={startCell}, Cost={startCellCost} | EndCell={endCell}, Cost={endCellCost}"); // Optional detailed log

        // Check if start or end cells are valid and passable
        if (!IsCellValid(startCell) || !IsCellValid(endCell))
        {
            Debug.LogWarning($"Start ({startCell}) or End ({endCell}) cell is outside world bounds.", this);
            return new List<Vector3>();
        }
        if (startCellCost == float.MaxValue || endCellCost == float.MaxValue) {
            Debug.LogWarning($"Start ({startCell}, Cost={startCellCost}) or End ({endCell}, Cost={endCellCost}) cell is impassable.", this);
            return new List<Vector3>();
        }

        // A* data structures
        PriorityQueue<Vector2Int> openSet = new PriorityQueue<Vector2Int>();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        Dictionary<Vector2Int, float> costSoFar = new Dictionary<Vector2Int, float>();

        // Initialize start node
        openSet.Enqueue(startCell, 0);
        cameFrom[startCell] = startCell; // Point to itself
        costSoFar[startCell] = 0;

        // A* main loop
        while (openSet.Count > 0)
        {
            Vector2Int current = openSet.Dequeue();

            // Goal reached
            if (current == endCell)
                break;

            // Process neighbors
            foreach (Vector2Int next in GetNeighbors(current))
            {
                 // Skip invalid or impassable neighbors
                 if (!IsCellValid(next)) continue; // Check bounds first
                 float neighborCost = costMap[next.x, next.y];
                 if (neighborCost == float.MaxValue) continue; // Skip impassable

                // Calculate cost to reach neighbor through current node
                // Ensure 'current' exists in costSoFar before accessing (should always be true if logic is correct)
                 float currentPathCost = costSoFar.ContainsKey(current) ? costSoFar[current] : float.MaxValue;
                 if (currentPathCost == float.MaxValue) {
                      // This shouldn't happen if current was dequeued, indicates a potential issue
                      Debug.LogError($"Internal A* Error: Cost for current node {current} not found!");
                      continue;
                 }
                 float newCost = currentPathCost + neighborCost; // Cost from start to neighbor via current


                // If this path to neighbor is better than any previous one, or if neighbor hasn't been visited
                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost; // Update cost
                    float priority = newCost + Heuristic(next, endCell); // Calculate priority (f = g + h)
                    openSet.Enqueue(next, priority); // Add/update neighbor in the open set
                    cameFrom[next] = current; // Record path
                }
            }
        }

        // --- Path Reconstruction and Simplification ---
        List<Vector3> detailedPath = ReconstructPath(cameFrom, startPos, startCell, endCell);

        if (detailedPath.Count == 0 && startCell != endCell)
        {
            // Only log warning if start and end are different cells but path is empty
            Debug.LogWarning($"Path not found from {startCell} to {endCell}!", this);
            return detailedPath; // Return empty list
        }

        // Simplify the path using distance threshold
        List<Vector3> simplifiedPath = SimplifyPathByDistance(detailedPath, minPathPointDistance);

        // Debug.Log($"Path found: Original points={detailedPath.Count}, Simplified points={simplifiedPath.Count}");

        return simplifiedPath;
    }

    /// <summary>
    /// Reconstructs the path from A* data.
    /// </summary>
    private List<Vector3> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector3 startPos,
        Vector2Int startCell, Vector2Int endCell)
    {
        List<Vector3> path = new List<Vector3>();
        Vector2Int currentCell = endCell;

        // If end cell was never reached
        if (!cameFrom.ContainsKey(endCell))
        {
            return path; // Return empty path
        }

        // Trace back from end to start
        while (currentCell != startCell)
        {
            path.Add(CellToWorld(currentCell)); // Add world position of the cell
            // Safely get the previous cell
            if (!cameFrom.TryGetValue(currentCell, out Vector2Int previousCell))
            {
                 Debug.LogError($"Path reconstruction error: Key {currentCell} not found in cameFrom dictionary. Path might be incomplete.", this);
                 path.Reverse(); // Reverse what we have so far
                 return path; // Return potentially incomplete path
            }
             // Check for potential infinite loop (shouldn't happen with correct A*)
             if(previousCell == currentCell) {
                  Debug.LogError($"Path reconstruction error: Loop detected at cell {currentCell}.");
                  path.Reverse();
                  return path;
             }
            currentCell = previousCell;
        }

        // Add the actual starting world position (more accurate than cell center)
        path.Add(startPos);

        // Reverse the list to get the path from start to end
        path.Reverse();

        return path;
    }


    /// <summary>
    /// Simplifies the path by removing points that are too close together.
    /// </summary>
    private List<Vector3> SimplifyPathByDistance(List<Vector3> path, float minDistance)
    {
        if (path == null || path.Count <= 2)
        {
            return path; // Nothing to simplify
        }

        List<Vector3> simplifiedPath = new List<Vector3>();
        simplifiedPath.Add(path[0]); // Always add the first point

        Vector3 lastAddedPoint = path[0];
        // Use squared distance for efficiency (avoids square root calculation)
        float sqrMinDistance = minDistance * minDistance;

        // Iterate through the path, skipping the first and last points initially
        for (int i = 1; i < path.Count - 1; i++)
        {
            // If the squared distance from the last added point is sufficient
            if ((path[i] - lastAddedPoint).sqrMagnitude >= sqrMinDistance)
            {
                simplifiedPath.Add(path[i]); // Add this point
                lastAddedPoint = path[i]; // Update the last added point
            }
        }

        // Always add the very last point of the original path to ensure the destination is reached
        simplifiedPath.Add(path[path.Count - 1]);

        return simplifiedPath;
    }


    /// <summary>
    /// Returns valid neighbors of a cell (4-directional).
    /// </summary>
    private IEnumerable<Vector2Int> GetNeighbors(Vector2Int cell)
    {
        var directions = new Vector2Int[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), // Right, Left
            new Vector2Int(0, 1), new Vector2Int(0, -1)  // Up, Down (Map coordinates)
        };

        foreach (var dir in directions)
        {
            Vector2Int next = cell + dir;
            // Check if the neighbor is within the world bounds
            if (IsCellValid(next))
            {
                yield return next;
            }
        }
    }

    /// <summary>
    /// Checks if a cell coordinate is within the world bounds.
    /// </summary>
    private bool IsCellValid(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < worldSizeX && cell.y >= 0 && cell.y < worldSizeZ;
    }

    /// <summary>
    /// Calculates the Manhattan distance heuristic for A*.
    /// </summary>
    private float Heuristic(Vector2Int a, Vector2Int b)
    {
        // Manhattan distance is suitable for grid movement without diagonals
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    /// <summary>
    /// Converts world coordinates to grid cell coordinates.
    /// </summary>
    private Vector2Int WorldToCell(Vector3 pos)
    {
        if (cellSize <= 0)
        {
            Debug.LogError("CellSize is zero or negative, cannot convert world to cell.", this);
            return Vector2Int.zero; // Return default value
        }

        // Calculate cell indices
        int x = Mathf.FloorToInt(pos.x / cellSize);
        int z = Mathf.FloorToInt(pos.z / cellSize);

        // Clamp indices to be within the valid range of the cost map
        x = Mathf.Clamp(x, 0, worldSizeX - 1);
        z = Mathf.Clamp(z, 0, worldSizeZ - 1);

        return new Vector2Int(x, z);
    }

    /// <summary>
    /// Converts grid cell coordinates to world coordinates (center of cell with actual height).
    /// </summary>
    private Vector3 CellToWorld(Vector2Int cell)
    {
        // Calculate center of the cell in XZ plane
        float worldX = cell.x * cellSize + cellSize / 2f;
        float worldZ = cell.y * cellSize + cellSize / 2f;
        float worldY = 0f; // Default height if terrain lookup fails

        // Find the actual height of the surface at the cell center
        // Start raycast high above the potential surface
        Vector3 rayStart = new Vector3(worldX, 1000f, worldZ); // Adjust Y start height if necessary
        float rayLength = 2000f; // Adjust ray length if necessary

        // Raycast down using the walkableLayerMask to find the ground
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayLength, walkableLayerMask))
        {
            worldY = hit.point.y; // Use the exact hit point height
        }
        else
        {
            // Optional: Handle cases where no ground is found (e.g., over a chasm)
            // Debug.LogWarning($"Could not determine height for cell {cell}. Using Y=0.", this);
            // Consider alternative strategies: use neighbor height, use a default height, or mark cell as invalid?
        }

        return new Vector3(worldX, worldY, worldZ);
    }
}
