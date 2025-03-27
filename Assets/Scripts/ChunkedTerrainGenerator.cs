using UnityEngine;
using System;

public class ChunkedTerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")] public int chunksX = 4;
    public int chunksZ = 4;
    public int resolutionPerChunk = 64;
    public float sizePerChunk = 10;

    [Header("Terrain Generation - Base Noise")]
    public float maxHeight = 15;

    public float terrainScale = 64.0f;
    public int octaves = 5;
    [Range(0f, 1f)] public float persistence = 0.387f;
    public float lacunarity = 3.0f;
    public float noiseOffsetX = 20f;
    public float noiseOffsetZ = 50f;

    [Header("Domain Warping")] public bool useDomainWarping = true;
    public float domainWarpScale = 100f;
    public float domainWarpStrength = 10f;
    public float domainWarpOffsetX = 1000f;
    public float domainWarpOffsetZ = 2000f;

    [Header("Terrain Shaping")] public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Valleys")] public bool useValleys = true; // Включить/выключить долины
    public float valleyNoiseScale = 150f; // Масштаб шума долин (обычно больше terrainScale)
    public float valleyDepth = 8f; // Максимальная глубина долины
    [Range(1f, 10f)] public float valleyWidthFactor = 4f; // Влияет на ширину/резкость краев долин (больше = уже)
    public float valleyNoiseOffsetX = 3000f; // Смещение шума долин X
    public float valleyNoiseOffsetZ = 4000f; // Смещение шума долин Z

    [Header("Deformation")] public float deformRadius = 0.35f;
    public float deformStrength = 0.1f;
    public float maxDeformDepth = 0.2f;

    [Header("Materials")] public Material terrainMaterial;
    public PhysicsMaterial physicsMaterial;

    public static event Action OnChunksRegenerated;


    void Start()
    {
        GenerateChunks();
    }

    [ContextMenu("Generate")]
    void GenerateChunks()
    {
        // ... (ClearChunks, цикл по чанкам - без изменений) ...
        ClearChunks();

        for (int z = 0; z < chunksZ; z++)
        {
            for (int x = 0; x < chunksX; x++)
            {
                GenerateChunk(x, z);
            }
        }

        OnChunksRegenerated?.Invoke();
    }

    void GenerateChunk(int chunkX, int chunkZ)
    {
        GameObject chunk = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunk.layer = LayerMask.NameToLayer("TerrainChunk");

        chunk.transform.parent = transform;
        chunk.transform.position = new Vector3(chunkX * sizePerChunk, 0, chunkZ * sizePerChunk);

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();
        mr.material = terrainMaterial;

        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        mc.material = physicsMaterial;

        MeshDeformer md = chunk.AddComponent<MeshDeformer>();
        md.deformRadius = deformRadius;
        md.deformStrength = deformStrength;
        md.maxDeformDepth = maxDeformDepth;

        chunk.AddComponent<ChunkDeformerManager>();

        Mesh mesh = new Mesh();
        int vertsPerLine = resolutionPerChunk + 1;
        Vector3[] vertices = new Vector3[vertsPerLine * vertsPerLine];
        Vector2[] uvs = new Vector2[vertsPerLine * vertsPerLine];
        int[] triangles = new int[resolutionPerChunk * resolutionPerChunk * 6];

        float step = sizePerChunk / resolutionPerChunk;

        for (int z = 0; z < vertsPerLine; z++)
        {
            for (int x = 0; x < vertsPerLine; x++)
            {
                float localX = x * step;
                float localZ = z * step;
                float worldX = chunk.transform.position.x + localX;
                float worldZ = chunk.transform.position.z + localZ;

                // Вычисляем высоту (уже включает фрактальный шум, domain warping и кривую)
                float y = GetTerrainHeight(worldX, worldZ); // Переименовал GetFractalNoise для ясности

                int index = x + z * vertsPerLine;
                vertices[index] = new Vector3(localX, y, localZ);
                uvs[index] = new Vector2((float)x / resolutionPerChunk, (float)z / resolutionPerChunk);
            }
        }

        // Заполнение triangles, назначение mesh, RecalculateNormals
        int tri = 0;
        for (int z = 0; z < resolutionPerChunk; z++)
        {
            for (int x = 0; x < resolutionPerChunk; x++)
            {
                int start = x + z * vertsPerLine;
                triangles[tri++] = start;
                triangles[tri++] = start + vertsPerLine;
                triangles[tri++] = start + 1;

                triangles[tri++] = start + 1;
                triangles[tri++] = start + vertsPerLine;
                triangles[tri++] = start + vertsPerLine + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
        mc.sharedMesh = mesh;
    }

    float GetTerrainHeight(float worldX, float worldZ)
    {
        // --- Шаг 1: Domain Warping (если включено) ---
        float warpedX = worldX;
        float warpedZ = worldZ;
        if (useDomainWarping)
        {
            float noiseX1 = Mathf.PerlinNoise((worldX / domainWarpScale) + domainWarpOffsetX,
                (worldZ / domainWarpScale) + domainWarpOffsetX);
            float noiseZ1 = Mathf.PerlinNoise((worldX / domainWarpScale) + domainWarpOffsetZ,
                (worldZ / domainWarpScale) + domainWarpOffsetZ);
            float warpOffsetX = (noiseX1 * 2f - 1f) * domainWarpStrength;
            float warpOffsetZ = (noiseZ1 * 2f - 1f) * domainWarpStrength;
            warpedX += warpOffsetX;
            warpedZ += warpOffsetZ;
        }

        // --- Шаг 2: Базовый фрактальный шум ---
        float totalHeight = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxValue = 0;
        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (warpedX / terrainScale * frequency) + noiseOffsetX;
            float sampleZ = (warpedZ / terrainScale * frequency) + noiseOffsetZ;
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ);
            totalHeight += perlinValue * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalizedHeight = (maxValue == 0) ? 0 : (totalHeight / maxValue);

        // --- Шаг 3: Применение кривой высот ---
        float curvedHeight = heightCurve.Evaluate(normalizedHeight);
        float baseTerrainHeight = curvedHeight * maxHeight; // Высота до добавления долин

        // --- Шаг 4: Формирование долин (если включено) ---
        float finalHeight = baseTerrainHeight;
        if (useValleys)
        {
            // Вычисляем шум для долин (используем НЕискаженные координаты или искаженные? Попробуем с warpedX/Z)
            float valleyNoiseX = (warpedX / valleyNoiseScale) + valleyNoiseOffsetX;
            float valleyNoiseZ = (warpedZ / valleyNoiseScale) + valleyNoiseOffsetZ;
            float rawValleyNoise = Mathf.PerlinNoise(valleyNoiseX, valleyNoiseZ);

            // Преобразуем шум в "фактор долины" (1 = центр долины, 0 = далеко от долины)
            // Используем формулу "инвертированного хребта"
            float ridgeNoise = 1.0f - Mathf.Abs(rawValleyNoise * 2f - 1f); // [0..1], пик при rawValleyNoise = 0.5

            // Применяем valleyWidthFactor, чтобы сделать долину уже/шире
            // Mathf.Pow(ridgeNoise, valleyWidthFactor) - чем больше фактор, тем быстрее спадает значение от центра (уже долина)
            float valleyFactor = Mathf.Pow(ridgeNoise, valleyWidthFactor);

            // Вычисляем, насколько нужно понизить высоту
            float heightReduction = valleyFactor * valleyDepth;

            // Вычитаем из базовой высоты
            finalHeight = baseTerrainHeight - heightReduction;

            // Ограничиваем минимальную высоту (например, нулем)
            finalHeight = Mathf.Max(0, finalHeight);
        }
        // ------------------------------------------------

        return finalHeight;
    }


    [ContextMenu("Clear")]
    void ClearChunks()
    {
        if (Application.isPlaying)
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
        }
        else
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }
}