using UnityEngine;

public class TerrainDeformer : MonoBehaviour
{
    public Terrain terrain;
    public float deformRadius = 0.05f; // Радиус вмятины
    public float deformStrength = 0.1f; // Глубина вмятины
    public float deformAmount = 0.2f; // Максимальная глубина вмятины

    private TerrainData terrainData;
    private int heightmapResolution;

    private float[,] initialHeights;
    

    void Start()
    {
        terrainData = terrain.terrainData;
        heightmapResolution = terrainData.heightmapResolution;
        
        initialHeights = terrainData.GetHeights(0, 0, terrainData.heightmapResolution, terrainData.heightmapResolution);
    }

    public void DeformAtPoint(Vector3 worldPos)
    {
        Vector3 terrainPos = WorldToTerrainPosition(worldPos);

        int posXInTerrain = (int)(terrainPos.x * heightmapResolution);
        int posZInTerrain = (int)(terrainPos.z * heightmapResolution);

        int deformRadiusInHeights = Mathf.RoundToInt(deformRadius / terrainData.size.x * heightmapResolution);

        // Получаем участок высот
        int startX = Mathf.Clamp(posXInTerrain - deformRadiusInHeights / 2, 0, heightmapResolution);
        int startZ = Mathf.Clamp(posZInTerrain - deformRadiusInHeights / 2, 0, heightmapResolution);

        int width = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startX);
        int height = Mathf.Clamp(deformRadiusInHeights, 1, heightmapResolution - startZ);

        float[,] heights = terrainData.GetHeights(startX, startZ, width, height);

        // Меняем высоты (делаем вмятину)
        
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                float currentHeight = heights[j, i];
                float minHeight = initialHeights[j, i] - deformAmount;
                if (currentHeight > minHeight) {
                    heights[j, i] -= deformAmount;
                }
                float dist = Vector2.Distance(new Vector2(i, j), new Vector2(width / 2, height / 2)) / (width / 2);
                float strength = Mathf.Clamp01(1 - dist); // Сила уменьшается к краю
                heights[j, i] -= deformStrength * strength;
                heights[j, i] = Mathf.Clamp01(heights[j, i]);
            }
        }

        // Записываем обратно
        terrainData.SetHeights(startX, startZ, heights);
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