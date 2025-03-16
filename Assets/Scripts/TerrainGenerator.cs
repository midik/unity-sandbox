using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public Terrain terrain;
    public Texture2D texture; // сюда засунуть картинку текстуры

    public int width = 512;
    public int height = 512;
    public int maxHeight = 20;
    public float scale = 50f;
    public float tileSize = 10;
    public TerrainData terrainData;

    public float flatRadius = 2f;
    public float difficultyCoef = 1.2f;
    public float difficultyCoefLinear = 2;
    
    public int numValleys = 3;
    public float valleyDepth = 0.3f; // глубина долины
    public float valleyWidth = 0.8f; // "размазанность" долины


    void Start()
    {
        // TerrainData data = new TerrainData();
        terrain.terrainData = terrainData; // назначить сразу
        GenerateTerrain(terrainData);
        AddTexture(terrainData);
        terrain.Flush();
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
        Vector2 center = new Vector2(width / 2f, height / 2f);
        float maxDistance = Vector2.Distance(Vector2.zero, center);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Vector2 point = new Vector2(x, y);
                float distance = Vector2.Distance(point, center);
                float normalizedDistance = (distance - flatRadius) / maxDistance;
                normalizedDistance = Mathf.Max(0, normalizedDistance); // не уходить в минус

                float distanceFactor = normalizedDistance * difficultyCoef;
                float xCoord = (float)x / width * scale;
                float yCoord = (float)y / height * scale;
                float noise = Mathf.PerlinNoise(xCoord, yCoord);

                // Базовая высота
                float finalHeight = noise * distanceFactor + normalizedDistance * difficultyCoefLinear;

                // --- Генерация радиальных долин ---
                float angle = Mathf.Atan2(y - center.y, x - center.x);
                float radialValley = (Mathf.Sin(angle * numValleys) + 1f) / 2f; // в [0,1]
                radialValley = Mathf.Pow(radialValley, valleyWidth); // сделать более острым/размытым
                float valleyEffect = radialValley * valleyDepth;

                // --- Итоговая высота с учетом долин ---
                heights[x, y] = Mathf.Max(0, finalHeight - valleyEffect);
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
    
    [ContextMenu("Generate Terrain")]
    void GenerateFromEditor()
    {
        Start();
    }
    
    // void OnValidate()
    // {
    //     Start(); // автоматически генерировать при изменениях параметров
    // }
}