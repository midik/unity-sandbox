using UnityEngine;

public class TerrainDeformer : MonoBehaviour
{
    public Terrain terrain;
    public float deformRadius = 0.05f;
    public float deformAmount = 0.1f;
    public float maxDeformDepth = 0.002f;

    private TerrainData terrainData;
    private int heightmapResolution;

    private float[,] initialHeights;     // Основной (базовый) террейн
    private float[,] deformableLayer;    // Проминаемый слой (изначально = maxDeformDepth)

    void Start()
    {
        terrainData = terrain.terrainData;
        heightmapResolution = terrainData.heightmapResolution;

        initialHeights = terrainData.GetHeights(0, 0, heightmapResolution, heightmapResolution);
        deformableLayer = new float[heightmapResolution, heightmapResolution];

        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int z = 0; z < heightmapResolution; z++)
            {
                deformableLayer[x, z] = maxDeformDepth;
            }
        }

        // Инициализируем высоты как сумма ОТ + ДС
        float[,] startHeights = new float[heightmapResolution, heightmapResolution];
        for (int x = 0; x < heightmapResolution; x++)
        {
            for (int z = 0; z < heightmapResolution; z++)
            {
                startHeights[x, z] = initialHeights[x, z] + deformableLayer[x, z];
            }
        }

        terrainData.SetHeights(0, 0, startHeights);
    }

    public void DeformAtPoint(Vector3 worldPos, float wheelRPM, float wheelMass)
    {
        Vector3 terrainPos = WorldToTerrainPosition(worldPos);

        int posX = Mathf.RoundToInt(terrainPos.x * heightmapResolution);
        int posZ = Mathf.RoundToInt(terrainPos.z * heightmapResolution);

        int deformRadiusInHeights = Mathf.RoundToInt(deformRadius / terrainData.size.x * heightmapResolution);

        int startX = Mathf.Clamp(posX - deformRadiusInHeights / 2, 0, heightmapResolution - 1);
        int startZ = Mathf.Clamp(posZ - deformRadiusInHeights / 2, 0, heightmapResolution - 1);
    
        int width = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startX);
        int height = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startZ);

        float baseDeform = deformAmount / terrainData.size.y;
        float speedFactor = Mathf.Clamp01(Mathf.Abs(wheelRPM) / 500f);
        float weightFactor = Mathf.Clamp01(wheelMass / 1500f);
        float normalizedDeform = baseDeform * (0.5f + speedFactor * 0.3f + weightFactor * 0.2f);

        // --- Проминаем слой
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int i = startX + x;
                int j = startZ + z;

                if (deformableLayer[i, j] > 0f)
                {
                    deformableLayer[i, j] = Mathf.Max(deformableLayer[i, j] - normalizedDeform, 0f);
                }
            }
        }

        // --- Применяем обновлённые значения
        float[,] newHeights = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int i = startX + x;
                int j = startZ + z;
                newHeights[x, z] = initialHeights[i, j] + deformableLayer[i, j];
            }
        }

        terrainData.SetHeights(startX, startZ, newHeights);
    }

    private Vector3 WorldToTerrainPosition(Vector3 worldPos)
    {
        Vector3 terrainPos = worldPos - terrain.transform.position;
        return new Vector3(
            terrainPos.x / terrainData.size.x,
            0,
            terrainPos.z / terrainData.size.z
        );
    }
}
