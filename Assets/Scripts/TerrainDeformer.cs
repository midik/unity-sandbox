using UnityEngine;

public class TerrainDeformer : MonoBehaviour
{
    public Terrain terrain;
    public float deformRadius = 0.05f; // Радиус вмятины
    // public float deformStrength = 0.1f; // Глубина вмятины
    public float deformAmount = 0.1f; // Максимальная глубина вмятины
    public float maxDeformDepth = 0.005f; // Максимальная глубина проминания (в норм. координатах)

    private TerrainData terrainData;
    private int heightmapResolution;

    private float[,] initialHeights; // Базовый (неизменяемый) рельеф
    private float[,] deformableLayer; // Проминаемый слой


    void Start()
    {
        terrainData = terrain.terrainData;
        heightmapResolution = terrainData.heightmapResolution;

        initialHeights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
        deformableLayer = new float[heightmapResolution, heightmapResolution];

        // Начальный "проминаемый слой" – пустой (без деформации)
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int z = 0; z < heightmapResolution; z++)
            {
                deformableLayer[x, z] = 0f; // Нет проминания в начале
            }
        }
    }


    public void DeformAtPoint(Vector3 worldPos, float wheelRPM, float wheelMass)
    {
        Vector3 terrainPos = WorldToTerrainPosition(worldPos);

        int posXInTerrain = Mathf.RoundToInt(terrainPos.x * heightmapResolution);
        int posZInTerrain = Mathf.RoundToInt(terrainPos.z * heightmapResolution);

        int deformRadiusInHeights = Mathf.RoundToInt(deformRadius / terrainData.size.x * heightmapResolution);

        int startX = Mathf.Clamp(posXInTerrain - deformRadiusInHeights / 2, 0, heightmapResolution - 1);
        int startZ = Mathf.Clamp(posZInTerrain - deformRadiusInHeights / 2, 0, heightmapResolution - 1);
    
        int width = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startX);
        int height = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startZ);

        // Зависимость от нагрузки на колесо (массы и скорости)
        float baseDeform = deformAmount / terrainData.size.y;
        float speedFactor = Mathf.Clamp01(wheelRPM / 500f);  // Нормируем: 0 (0 об/мин) - 1 (500 об/мин)
        float weightFactor = Mathf.Clamp01(wheelMass / 1500f); // Нормируем: 0 - легкая машина, 1 - тяжелая
        float normalizedDeform = baseDeform * (0.5f + speedFactor * 0.3f + weightFactor * 0.2f);

        // Debug.Log($"Deform -> RPM: {wheelRPM}, Mass: {wheelMass}, Deform: {normalizedDeform}");

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                deformableLayer[startX + i, startZ + j] = Mathf.Min(
                    deformableLayer[startX + i, startZ + j] + normalizedDeform, 
                    maxDeformDepth
                );
            }
        }

        // Обновляем карту высот
        float[,] newHeights = new float[width, height];

        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                newHeights[i, j] = initialHeights[startX + i, startZ + j] - deformableLayer[startX + i, startZ + j];
            }
        }

        terrainData.SetHeights(startX, startZ, newHeights);
    }


    public void Update()
    {
        // Debug.Log(transform.position.y);
    }

    private Vector3 WorldToTerrainPosition(Vector3 worldPos)
    {
        Vector3 terrainPos = worldPos - terrain.transform.position;
        Vector3 normalizedPos = new Vector3(
            terrainPos.x / terrainData.size.x,
            0,
            terrainPos.z / terrainData.size.z
        );

        // Debug.Log($"WorldPos: {worldPos}, TerrainPos: {terrainPos}, Normalized: {normalizedPos}");

        return normalizedPos;
    }
}