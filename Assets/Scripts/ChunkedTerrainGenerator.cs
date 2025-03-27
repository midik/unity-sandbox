using UnityEngine;
using System;

public class ChunkedTerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public int chunksX = 4;
    public int chunksZ = 4;
    public int resolutionPerChunk = 64;
    public float sizePerChunk = 10;

    [Header("Terrain Generation - Base Noise")]
    public float maxHeight = 15; // Общая максимальная высота
    public float terrainScale = 64.0f; // Масштаб базового шума
    public int octaves = 5; // Количество слоев шума
    [Range(0f, 1f)]
    public float persistence = 0.387f; // Уменьшение амплитуды октав
    public float lacunarity = 3.0f; // Увеличение частоты октав
    public float noiseOffsetX = 20f; // Смещение основного шума по X
    public float noiseOffsetZ = 50f; // Смещение основного шума по Z

    // ----- НОВОЕ: Параметры для Domain Warping -----
    [Header("Domain Warping")]
    public bool useDomainWarping = true; // Включить/выключить
    public float domainWarpScale = 100f; // Масштаб шума для искажения координат
    public float domainWarpStrength = 10f; // Сила искажения координат
    public float domainWarpOffsetX = 1000f; // Смещение для X-искажения (отличное от основного)
    public float domainWarpOffsetZ = 2000f; // Смещение для Z-искажения (отличное от основного)
    // --------------------------------------------

    // ----- НОВОЕ: Кривая для перераспределения высот -----
    [Header("Terrain Shaping")]
    public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1); // Кривая высот
    // --------------------------------------------------

    [Header("Deformation")]
    public float deformRadius = 0.35f;
    public float deformStrength = 0.1f;
    public float maxDeformDepth = 0.2f;

    [Header("Materials")]
    public Material terrainMaterial;
    public PhysicsMaterial physicsMaterial;

    public static event Action OnChunksRegenerated;


    void Start()
    {
        GenerateChunks();
    }

    [ContextMenu("Generate")]
    void GenerateChunks()
    {
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
        // ... (Код создания GameObject, MeshFilter, Renderer, Collider, Deformer - без изменений) ...
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

                // Вычисляем высоту с помощью фрактального шума (с возможным Domain Warping)
                float y = GetFractalNoise(worldX, worldZ);

                int index = x + z * vertsPerLine;
                vertices[index] = new Vector3(localX, y, localZ);
                uvs[index] = new Vector2((float)x / resolutionPerChunk, (float)z / resolutionPerChunk);
            }
        }

        // ... (Код заполнения triangles, назначения mesh, RecalculateNormals - без изменений) ...
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


    float GetFractalNoise(float worldX, float worldZ)
    {
        float warpedX = worldX;
        float warpedZ = worldZ;

        // ----- Применяем Domain Warping, если включено -----
        if (useDomainWarping)
        {
            // Вычисляем смещения координат с помощью другого шума Perlin
            // Используем разные смещения (domainWarpOffsetX/Z), чтобы шум для X и Z был разным
            float noiseX1 = Mathf.PerlinNoise(
                (worldX / domainWarpScale) + domainWarpOffsetX,
                (worldZ / domainWarpScale) + domainWarpOffsetX // Можно использовать одно смещение или разные
            );
            float noiseZ1 = Mathf.PerlinNoise(
                (worldX / domainWarpScale) + domainWarpOffsetZ, // Используем другое смещение для Z
                (worldZ / domainWarpScale) + domainWarpOffsetZ
            );

            // Преобразуем шум [0, 1] в диапазон [-1, 1]
            float warpOffsetX = (noiseX1 * 2f - 1f) * domainWarpStrength;
            float warpOffsetZ = (noiseZ1 * 2f - 1f) * domainWarpStrength;

            // Применяем смещение к исходным координатам
            warpedX += warpOffsetX;
            warpedZ += warpOffsetZ;

            // Опционально: можно добавить еще один слой Domain Warping для большей сложности
            // float noiseX2 = Mathf.PerlinNoise(warpedX / (domainWarpScale * 0.5f) + domainWarpOffsetX + 100f, ... );
            // warpedX += (noiseX2 * 2f - 1f) * (domainWarpStrength * 0.5f);
            // и т.д.
        }
        // -------------------------------------------------

        float totalHeight = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxValue = 0;

        for (int i = 0; i < octaves; i++)
        {
            // Используем ИСКАЖЕННЫЕ координаты warpedX, warpedZ
            float sampleX = (warpedX / terrainScale * frequency) + noiseOffsetX;
            float sampleZ = (warpedZ / terrainScale * frequency) + noiseOffsetZ;

            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ);
            totalHeight += perlinValue * amplitude;

            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        // Нормализуем высоту к диапазону [0, 1]
        float normalizedHeight = (maxValue == 0) ? 0 : (totalHeight / maxValue);

        // ----- Применяем Animation Curve -----
        float curvedHeight = heightCurve.Evaluate(normalizedHeight);
        // -----------------------------------

        // Масштабируем до maxHeight
        return curvedHeight * maxHeight;
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