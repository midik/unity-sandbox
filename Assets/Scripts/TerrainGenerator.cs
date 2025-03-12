using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public Terrain terrain;
    public Texture2D texture; // сюда засунуть картинку текстуры

    public int width = 512;
    public int height = 512;
    public int maxHeight = 6;
    public float scale = 50f;
    public float tileSize = 10;

    void Start()
    {
        terrain.terrainData = GenerateTerrain(terrain.terrainData);
        AddTexture(terrain.terrainData);
    }

    TerrainData GenerateTerrain(TerrainData terrainData)
    {
        terrainData.heightmapResolution = width + 1;
        terrainData.size = new Vector3(width, maxHeight, height); // 50 - макс. высота
        terrainData.SetHeights(0, 0, GenerateHeights());
        return terrainData;
    }

    float[,] GenerateHeights()
    {
        float[,] heights = new float[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float xCoord = (float)x / width * scale;
                float yCoord = (float)y / height * scale;
                heights[x, y] = Mathf.PerlinNoise(xCoord, yCoord);
            }
        }
        return heights;
    }

    void AddTexture(TerrainData terrainData)
    {
        TerrainLayer layer = new TerrainLayer();
        layer.diffuseTexture = texture; // Текстура для земли
        layer.tileSize = new Vector2(tileSize, tileSize); // Размер тайла текстуры, можно регулировать

        terrainData.terrainLayers = new TerrainLayer[] { layer }; // Добавляем слой
}
}
