using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Serialization;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor; // Добавляем для ProgressBar
#endif

public class ChunkedTerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public int chunksX = 32;
    public int chunksZ = 32;
    public int resolutionPerChunk = 24;
    public int sizePerChunk = 10;

    [Header("Terrain Generation - Base Noise")]
    public float maxHeight = 15; // Общая максимальная высота

    public float terrainScale = 30.0f; // Масштаб базового шума
    public int octaves = 8; // Количество слоев шума
    [Range(0f, 1f)] public float persistence = 0.382f; // Уменьшение амплитуды октав
    public float lacunarity = 2.0f; // Увеличение частоты октав
    public float noiseOffsetX = 20f; // Смещение основного шума по X
    public float noiseOffsetZ = 50f; // Смещение основного шума по Z

    [Header("Domain Warping")]
    public bool useDomainWarping = true; // Включить/выключить
    public float domainWarpScale = 100f; // Масштаб шума для искажения координат
    public float domainWarpStrength = 10f; // Сила искажения координат
    public float domainWarpOffsetX = 1000f; // Смещение для X-искажения (отличное от основного)
    public float domainWarpOffsetZ = 2000f; // Смещение для Z-искажения (отличное от основного)

    [Header("Terrain Shaping")]
    public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1); // Кривая высот

    [Header("Valleys (Noise Based)")]
    public bool useValleys = true; // Включить/выключить шумные долины
    public float valleyNoiseScale = 60f; // Масштаб шума долин (обычно больше terrainScale)
    public float valleyDepth = 6f; // Максимальная глубина долины
    [Range(1f, 10f)] public float valleyWidthFactor = 1.6f; // Влияет на ширину/резкость краев долин (больше = уже)
    public float valleyNoiseOffsetX = 3000f; // Смещение шума долин X
    public float valleyNoiseOffsetZ = 4000f; // Смещение шума долин Z

    [Header("Valleys (Spline Based)")]
    public bool useSplineValleys = true; // Включить/выключить сплайновые долины
    public bool cutoffToBottom = true; // Срезать долину до дна (упрощенный вариант)
    public SplineContainer splineContainer; // Сюда перетащить объект со сплайнами из сцены
    public float splineValleyWidth = 5f; // Ширина плоского дна долины
    public float splineValleyDepth = 12f; // Глубина долины в центре относительно окружающего рельефа
    public float splineValleyFalloff = 20f; // Расстояние, на котором долина переходит в обычный рельеф (ширина склона)

    // По умолчанию EaseInOut: начинается и заканчивается плавно, похоже на smoothstep
    // Ключи: (время=0, значение=1), (время=1, значение=0)
    public AnimationCurve valleySlopeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Road Generation")]
    public bool generateRoad = true;

    [FormerlySerializedAs("roads")] [Tooltip("Объект, в который будут помещены все сегменты дороги")]
    public GameObject roadsContainer;

    [Tooltip("Использовать те же сплайны, что и для долин")]
    public bool useValleySplinesForRoads = true; // Если нужно отдельные сплайны - добавить еще SplineContainer

    public float roadWidth = 4f; // Ширина дороги
    public Material roadMaterial; // Материал дороги
    
    [Tooltip("Ширина центральной полосы с пониженной стоимостью")]
    public float centerStripWidth = 1.0f;
    [Tooltip("Имя слоя для центральной полосы (убедитесь, что слой существует!)")]
    public string centerStripLayerName = "RoadCenter"; // Убедись, что этот слой есть в Tags and Layers
    [Tooltip("Опциональный материал для визуального выделения центральной полосы")]
    public Material centerStripMaterial; // Если null, будет использован roadMaterial

    [Tooltip("Шаг в единицах длины сплайна для создания вершин дороги")]
    public float roadMeshStep = 1.0f; // Например, 1 метр

    [Tooltip("Насколько приподнять меш дороги над террейном")]
    public float roadRaise = 0.2f;

    [Header("Deformation")]
    public float deformRadius = 0.35f;
    public float deformStrength = 0.1f;
    public float maxDeformDepth = 0.24f;

    [Header("Materials")]
    public Material terrainMaterial;
    public PhysicsMaterial physicsMaterial;

    public static event Action OnChunksRegenerated;
    internal List<Spline> cachedSplines = null;

    private static readonly int SceneView1 = Shader.PropertyToID("_SceneView");
    
    
    private void Awake()
    {
        // Disable scene view mode (normal blending)
        terrainMaterial.SetFloat(SceneView1, 0);
    }

    private void OnDestroy()
    {
        // Enable scene view mode (no blending)
        terrainMaterial.SetFloat(SceneView1, 1);
    }

    // Метод для вызова из Context Menu в редакторе
    [ContextMenu("Terrain - Generate")]
    void GenerateContextMenu()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            GenerateChunks();
        }
#endif
    }

    // Метод для очистки чанков (вызывается из Context Menu)
    [ContextMenu("Terrain - Clear")]
    void ClearChunksContextMenu()
    {
        ClearChunks();
        Debug.Log("Cleared all terrain chunks.");
    }

    // Синхронная генерация с ProgressBar (для вызова из редактора)
    void GenerateChunks()
    {
#if UNITY_EDITOR // Этот метод целиком зависит от EditorUtility
        float startTime = Time.realtimeSinceStartup;
        string progressBarTitle = "Generating Terrain";
        bool cancelled = false; // Флаг отмены

        try
        {
            EditorUtility.DisplayProgressBar(progressBarTitle, "Preparing...", 0f);

            ClearChunks(); // Очищаем синхронно
            CacheSplines(); // Кешируем сплайны синхронно

            int totalChunks = chunksX * chunksZ;
            int chunksProcessed = 0;

            if (totalChunks == 0)
            {
                Debug.LogWarning("chunksX or chunksZ is zero. No chunks to generate.");
                return;
            }

            for (int z = 0; z < chunksZ; z++)
            {
                for (int x = 0; x < chunksX; x++)
                {
                    // Обновляем прогресс бар для каждого чанка
                    float progress = (float)chunksProcessed / totalChunks;
                    cancelled = EditorUtility.DisplayCancelableProgressBar(
                        progressBarTitle,
                        $"Processing chunk ({x}, {z}) - {chunksProcessed + 1}/{totalChunks}",
                        progress);

                    if (cancelled) // Позволяем отменить генерацию
                    {
                        Debug.LogWarning("Terrain generation cancelled by user.");
                        ClearChunks();
                        return;
                    }

                    GameObject chunkObject = GenerateSingleChunk(x, z);
                    if (chunkObject)
                    {
                        chunkObject.transform.parent = transform;
                    }

                    chunksProcessed++;
                }
            }

            OnChunksRegenerated?.Invoke();
            Debug.Log($"Terrain generation finished in {(Time.realtimeSinceStartup - startTime):F2} seconds.");
        }
        catch (Exception e) // Ловим ошибки во время генерации
        {
            Debug.LogError($"Error during terrain generation: {e}");
            if (!cancelled) // Если не была отмена, то это ошибка
            {
                ClearChunks();
            }
        }
        finally // Гарантированно убираем прогресс бар
        {
            EditorUtility.ClearProgressBar();
            // Если отменили, OnChunksRegenerated не вызовется, возможно, нужно оповестить системы об отмене
            if (cancelled)
            {
                // Может потребоваться доп. логика оповещения об отмене
            }
        }
#else
        // Если этот метод случайно вызван в билде, ничего не делаем или перенаправляем на корутину
        Debug.LogError("Synchronous GenerateChunks() called outside Unity Editor. Use GenerateChunksCoroutine instead.");
        // StartCoroutine(GenerateChunksCoroutine());
#endif
    }

    public void CacheSplines()
    {
        cachedSplines = new List<Spline>(); // Всегда создаем новый список
        if (!splineContainer)
        {
            if (useSplineValleys || useValleySplinesForRoads) // Проверяем оба флага
                Debug.LogWarning(
                    $"CacheSplines: SplineContainer not assigned but required for valleys or roads. InstanceID={GetInstanceID()}");
            return;
        }

        if (!splineContainer.gameObject)
        {
            if (useSplineValleys || useValleySplinesForRoads)
                Debug.LogError(
                    $"CacheSplines Error: SplineContainer GameObject is invalid! InstanceID={GetInstanceID()}");
            return;
        }

        cachedSplines.AddRange(splineContainer.Splines);
    }

    // Метод для запуска генерации чанка, теперь использует предварительные вычисления и burst-оптимизированные Jobs
    public GameObject GenerateSingleChunk(int chunkX, int chunkZ)
    {
        GameObject chunk = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunk.layer = LayerMask.NameToLayer("TerrainChunk");
        chunk.transform.position = new Vector3(chunkX * sizePerChunk, 0, chunkZ * sizePerChunk);

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();
        mr.material = terrainMaterial;
        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        mc.material = physicsMaterial;

        if (!chunk.GetComponent<MeshDeformer>())
        {
            MeshDeformer md = chunk.AddComponent<MeshDeformer>();
            md.deformRadius = deformRadius;
            md.deformStrength = deformStrength;
            md.maxDeformDepth = maxDeformDepth;
        }

        if (!chunk.GetComponent<ChunkDeformerManager>())
        {
            chunk.AddComponent<ChunkDeformerManager>();
        }

        Mesh mesh = new Mesh();
        mesh.name = $"TerrainMesh_{chunkX}_{chunkZ}";
        int vertsPerLine = resolutionPerChunk + 1;
        int totalVertices = vertsPerLine * vertsPerLine;

        // Подготовка данных для Job
        NativeArray<float> heights = new NativeArray<float>(totalVertices, Allocator.TempJob);

        // Предвычисление кривой высот
        int curveSamples = 256;
        NativeArray<float> heightCurveValues = new NativeArray<float>(curveSamples, Allocator.TempJob);
        for (int i = 0; i < curveSamples; i++)
        {
            float t = (float)i / (curveSamples - 1);
            heightCurveValues[i] = heightCurve.Evaluate(t);
        }

        // Предвычисление влияния сплайнов
        NativeArray<float> splineFactors = new NativeArray<float>(totalVertices, Allocator.TempJob);
        float step = sizePerChunk / resolutionPerChunk;
        if (useSplineValleys && cachedSplines != null && cachedSplines.Count > 0)
        {
            for (int z = 0; z < vertsPerLine; z++)
            {
                for (int x = 0; x < vertsPerLine; x++)
                {
                    float localX = x * step;
                    float localZ = z * step;
                    float worldX = chunk.transform.position.x + localX;
                    float worldZ = chunk.transform.position.z + localZ;
                    float3 worldPos = new float3(worldX, 0, worldZ);

                    float minDistSq = float.MaxValue;
                    foreach (var spline in cachedSplines)
                    {
                        if (spline == null || spline.Knots == null || spline.Knots.Count() < 2) continue;
                        SplineUtility.GetNearestPoint(spline, worldPos, out float3 nearestPoint, out _, 3, 8);
                        float distSq = math.distancesq(new float2(worldPos.x, worldPos.z), new float2(nearestPoint.x, nearestPoint.z));
                        minDistSq = math.min(minDistSq, distSq);
                    }

                    float minDistanceToSpline = math.sqrt(minDistSq);
                    float splineFactor = 0f;
                    float halfWidth = splineValleyWidth / 2f;
                    if (minDistanceToSpline <= halfWidth)
                    {
                        splineFactor = 1.0f;
                    }
                    else if (minDistanceToSpline < halfWidth + splineValleyFalloff)
                    {
                        float t = (minDistanceToSpline - halfWidth) / splineValleyFalloff;
                        float smoothT = t * t * (3f - 2f * t); // Smoothstep
                        splineFactor = 1f - smoothT;
                    }

                    int index = x + z * vertsPerLine;
                    splineFactors[index] = splineFactor;
                }
            }
        }
        else
        {
            for (int i = 0; i < totalVertices; i++)
            {
                splineFactors[i] = 0f;
            }
        }

        // Настройка и запуск Burst-оптимизированного Job
        TerrainHeightJob job = new TerrainHeightJob
        {
            vertsPerLine = vertsPerLine,
            step = step,
            chunkWorldX = chunk.transform.position.x,
            chunkWorldZ = chunk.transform.position.z,
            maxHeight = maxHeight,
            terrainScale = terrainScale,
            octaves = octaves,
            persistence = persistence,
            lacunarity = lacunarity,
            noiseOffsetX = noiseOffsetX,
            noiseOffsetZ = noiseOffsetZ,
            useDomainWarping = useDomainWarping,
            domainWarpScale = domainWarpScale,
            domainWarpStrength = domainWarpStrength,
            domainWarpOffsetX = domainWarpOffsetX,
            domainWarpOffsetZ = domainWarpOffsetZ,
            heightCurveValues = heightCurveValues,
            heightCurveSamples = curveSamples,
            useValleys = useValleys,
            valleyNoiseScale = valleyNoiseScale,
            valleyDepth = valleyDepth,
            valleyWidthFactor = valleyWidthFactor,
            valleyNoiseOffsetX = valleyNoiseOffsetX,
            valleyNoiseOffsetZ = valleyNoiseOffsetZ,
            useSplineValleys = useSplineValleys,
            splineFactors = splineFactors,
            splineValleyDepth = splineValleyDepth,
            heights = heights
        };

        JobHandle handle = job.Schedule(totalVertices, 64);
        handle.Complete(); // В синхронном методе ожидаем завершения

        // Создание вершин
        Vector3[] vertices = new Vector3[totalVertices];
        Vector2[] uvs = new Vector2[totalVertices];
        for (int i = 0; i < totalVertices; i++)
        {
            int x = i % vertsPerLine;
            int z = i / vertsPerLine;
            float localX = x * step;
            float localZ = z * step;
            vertices[i] = new Vector3(localX, heights[i], localZ);
            uvs[i] = new Vector2((float)x / resolutionPerChunk, (float)z / resolutionPerChunk);
        }

        // Создание треугольников
        int[] triangles = new int[resolutionPerChunk * resolutionPerChunk * 6];
        int tri = 0;
        for (int z = 0; z < resolutionPerChunk; z++)
        {
            for (int x = 0; x < resolutionPerChunk; x++)
            {
                int row = z * vertsPerLine;
                int nextRow = (z + 1) * vertsPerLine;
                int current = row + x;
                triangles[tri++] = current;
                triangles[tri++] = nextRow + x;
                triangles[tri++] = current + 1;
                triangles[tri++] = current + 1;
                triangles[tri++] = nextRow + x;
                triangles[tri++] = nextRow + x + 1;
            }
        }

        // Настройка меша
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.mesh = mesh;
        mc.sharedMesh = mesh;

        // Освобождение памяти
        heights.Dispose();
        heightCurveValues.Dispose();
        splineFactors.Dispose();

        return chunk;
    }

    // Метод очистки чанков
    public void ClearChunks()
    {
        bool isEditorNotInPlayMode = false;
#if UNITY_EDITOR
        isEditorNotInPlayMode = !Application.isPlaying;
#endif

        // Используем DestroyImmediate ТОЛЬКО в редакторе вне Play Mode
        if (isEditorNotInPlayMode)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child) DestroyImmediate(child.gameObject);
            }
        }
        else // В Play Mode (в редакторе или билде) используем Destroy
        {
            foreach (Transform child in transform)
            {
                if (child) Destroy(child.gameObject);
            }
            // Destroy сам по себе отложенный, но для синхронности можно подождать конца кадра,
            // но это усложнит логику, пока оставим так.
        }
    }

    [ContextMenu("Roads - Generate")]
    internal void GenerateRoads()
    {
        if (!generateRoad) return;
        if (!roadsContainer)
        {
            Debug.LogError("Cannot generate roads: Roads Container GameObject is not assigned.", this);
            return;
        }

        if (!roadMaterial)
        {
            Debug.LogError("Cannot generate roads: Road Material is not assigned.", this);
            return;
        }

        // Ensure the center strip layer exists
        int centerLayer = LayerMask.NameToLayer(centerStripLayerName);
        if (centerLayer == -1) // Layer does not exist
        {
            Debug.LogError($"Cannot generate roads: Layer '{centerStripLayerName}' does not exist. Please create it in Project Settings -> Tags and Layers.", this);
            return;
        }

        if (cachedSplines == null || cachedSplines.Count == 0)
        {
            CacheSplines(); // Attempt to cache splines if not already done
        }

        if (!useValleySplinesForRoads || cachedSplines == null || cachedSplines.Count == 0)
        {
            Debug.LogWarning("Cannot generate roads: Road generation enabled, but no valid splines found or assigned.", this);
            return;
        }

        // Validate widths
        if (centerStripWidth >= roadWidth || centerStripWidth <= 0)
        {
             Debug.LogError($"Cannot generate roads: Center Strip Width ({centerStripWidth}) must be positive and less than Road Width ({roadWidth}).", this);
             return;
        }


        float startTime = Time.realtimeSinceStartup;
        Debug.Log("Starting road generation (Center Strip + Edges/Shoulders)...");

        ClearRoads(); // Clear existing roads first

        LayerMask terrainMask = LayerMask.GetMask("TerrainChunk"); // Ensure this layer matches your terrain chunks
        Vector3 raiseVector = Vector3.up * roadRaise; // World up vector for raising the road

        int roadSegmentIndex = 0;
        foreach (var spline in cachedSplines)
        {
            if (spline == null || spline.Count < 2) continue; // Basic spline validity check

            // Lists for the CENTER STRIP mesh
            List<Vector3> centerVertices = new List<Vector3>();
            List<Vector2> centerUvs = new List<Vector2>();
            List<int> centerTriangles = new List<int>();

            // Lists for the EDGES and SHOULDERS mesh
            List<Vector3> edgeVertices = new List<Vector3>();
            List<Vector2> edgeUvs = new List<Vector2>();
            List<int> edgeTriangles = new List<int>();

            float currentDistance = 0f;
            float splineLength = spline.GetLength();
            if (splineLength < roadMeshStep) continue; // Skip very short splines

            // UV parameters (can be made public variables if needed)
            float shoulderU_Start = -0.1f; // U for outer edge of left shoulder
            float roadEdgeU_Start = 0f;    // U for left edge of road surface
            float centerEdgeU_Start = 0.45f; // U for left edge of center strip (on edge mesh)
            float centerStripU_Start = 0f; // U for left edge of center strip (on center mesh)
            float centerStripU_End = 1f;   // U for right edge of center strip (on center mesh)
            float centerEdgeU_End = 0.55f; // U for right edge of center strip (on edge mesh)
            float roadEdgeU_End = 1f;      // U for right edge of road surface
            float shoulderU_End = 1.1f;   // U for outer edge of right shoulder

            float halfRoadWidth = roadWidth / 2f;
            float halfCenterWidth = centerStripWidth / 2f;

            for (float dist = 0; dist <= splineLength + roadMeshStep; dist += roadMeshStep) // Iterate slightly beyond length to ensure last segment is generated
            {
                float t = Mathf.Clamp01(dist / splineLength); // Normalized distance along spline
                // Evaluate spline properties
                if (!spline.Evaluate(t, out float3 pos, out float3 dir, out float3 up))
                {
                     // Handle evaluation failure if necessary (e.g., log warning, skip point)
                     Debug.LogWarning($"Failed to evaluate spline at t={t}");
                     continue;
                }

                // Ensure direction is valid and normalized
                if (math.lengthsq(dir) < 0.0001f) dir = math.forward(); // Fallback direction
                else dir = math.normalize(dir);

                // Use world up for consistent right vector calculation, avoids issues with spline tilt
                float3 worldUp = new float3(0, 1, 0);
                float3 right = math.normalize(math.cross(dir, worldUp));

                // --- Calculate Key Horizontal Positions ---
                float3 p_left_edge_ideal = pos - right * halfRoadWidth;         // Outer left edge
                float3 p_center_left_ideal = pos - right * halfCenterWidth;     // Left edge of center strip
                float3 p_center_right_ideal = pos + right * halfCenterWidth;    // Right edge of center strip
                float3 p_right_edge_ideal = pos + right * halfRoadWidth;        // Outer right edge

                // --- Project onto Terrain to find base heights ---
                float3 p_left_terrain = ProjectToTerrain(p_left_edge_ideal, terrainMask);
                float3 p_center_left_terrain = ProjectToTerrain(p_center_left_ideal, terrainMask);
                float3 p_center_right_terrain = ProjectToTerrain(p_center_right_ideal, terrainMask);
                float3 p_right_terrain = ProjectToTerrain(p_right_edge_ideal, terrainMask);

                // --- Calculate Final Road Vertex Positions (raised) ---
                Vector3 v_sh_l = (Vector3)p_left_terrain;                                  // Shoulder Left (on terrain)
                Vector3 v_rd_l = (Vector3)p_left_terrain + raiseVector;                    // Road Left Edge (raised)
                Vector3 v_cn_l = (Vector3)p_center_left_terrain + raiseVector;             // Center Left Edge (raised)
                Vector3 v_cn_r = (Vector3)p_center_right_terrain + raiseVector;            // Center Right Edge (raised)
                Vector3 v_rd_r = (Vector3)p_right_terrain + raiseVector;                   // Road Right Edge (raised)
                Vector3 v_sh_r = (Vector3)p_right_terrain;                                 // Shoulder Right (on terrain)                           // Shoulder Right (on terrain)

                // --- Add Vertices and UVs ---
                int currentCenterVertIndex = centerVertices.Count;
                int currentEdgeVertIndex = edgeVertices.Count;
                float vCoord = currentDistance / (roadWidth); // V coordinate based on distance

                // Center Strip Mesh Data
                centerVertices.Add(v_cn_l); // Index +0
                centerVertices.Add(v_cn_r); // Index +1
                centerUvs.Add(new Vector2(centerStripU_Start, vCoord));
                centerUvs.Add(new Vector2(centerStripU_End, vCoord));

                // Edge/Shoulder Mesh Data
                edgeVertices.Add(v_sh_l); // Index +0
                edgeVertices.Add(v_rd_l); // Index +1
                edgeVertices.Add(v_cn_l); // Index +2 (Shares vertex with center mesh start)
                edgeVertices.Add(v_cn_r); // Index +3 (Shares vertex with center mesh end)
                edgeVertices.Add(v_rd_r); // Index +4
                edgeVertices.Add(v_sh_r); // Index +5
                edgeUvs.Add(new Vector2(shoulderU_Start, vCoord));
                edgeUvs.Add(new Vector2(roadEdgeU_Start, vCoord));
                edgeUvs.Add(new Vector2(centerEdgeU_Start, vCoord)); // UV for inner edge of left road part
                edgeUvs.Add(new Vector2(centerEdgeU_End, vCoord));   // UV for inner edge of right road part
                edgeUvs.Add(new Vector2(roadEdgeU_End, vCoord));
                edgeUvs.Add(new Vector2(shoulderU_End, vCoord));


                // --- Add Triangles (after the first set of vertices) ---
                if (currentDistance > 0) // Or check if index >= 1 set of vertices
                {
                    // Center Strip Triangles (2 triangles)
                    int prev_cn_l = currentCenterVertIndex - 2;
                    int prev_cn_r = currentCenterVertIndex - 1;
                    int curr_cn_l = currentCenterVertIndex + 0;
                    int curr_cn_r = currentCenterVertIndex + 1;
                    centerTriangles.Add(prev_cn_l); centerTriangles.Add(prev_cn_r); centerTriangles.Add(curr_cn_l);
                    centerTriangles.Add(curr_cn_l); centerTriangles.Add(prev_cn_r); centerTriangles.Add(curr_cn_r);

                    // Edge/Shoulder Triangles (4 strips = 8 triangles)
                    int prev_sh_l = currentEdgeVertIndex - 6; // Indices from previous step
                    int prev_rd_l = currentEdgeVertIndex - 5;
                    int prev_cn_l_edge = currentEdgeVertIndex - 4;
                    int prev_cn_r_edge = currentEdgeVertIndex - 3;
                    int prev_rd_r = currentEdgeVertIndex - 2;
                    int prev_sh_r = currentEdgeVertIndex - 1;

                    int curr_sh_l = currentEdgeVertIndex + 0; // Indices for current step
                    int curr_rd_l = currentEdgeVertIndex + 1;
                    int curr_cn_l_edge = currentEdgeVertIndex + 2;
                    int curr_cn_r_edge = currentEdgeVertIndex + 3;
                    int curr_rd_r = currentEdgeVertIndex + 4;
                    int curr_sh_r = currentEdgeVertIndex + 5;

                    // Left Shoulder Strip
                    edgeTriangles.Add(prev_sh_l); edgeTriangles.Add(prev_rd_l); edgeTriangles.Add(curr_sh_l);
                    edgeTriangles.Add(curr_sh_l); edgeTriangles.Add(prev_rd_l); edgeTriangles.Add(curr_rd_l);
                    // Left Road Strip (Edge to Center)
                    edgeTriangles.Add(prev_rd_l); edgeTriangles.Add(prev_cn_l_edge); edgeTriangles.Add(curr_rd_l);
                    edgeTriangles.Add(curr_rd_l); edgeTriangles.Add(prev_cn_l_edge); edgeTriangles.Add(curr_cn_l_edge);
                    // Right Road Strip (Center to Edge)
                    edgeTriangles.Add(prev_cn_r_edge); edgeTriangles.Add(prev_rd_r); edgeTriangles.Add(curr_cn_r_edge);
                    edgeTriangles.Add(curr_cn_r_edge); edgeTriangles.Add(prev_rd_r); edgeTriangles.Add(curr_rd_r);
                    // Right Shoulder Strip
                    edgeTriangles.Add(prev_rd_r); edgeTriangles.Add(prev_sh_r); edgeTriangles.Add(curr_rd_r);
                    edgeTriangles.Add(curr_rd_r); edgeTriangles.Add(prev_sh_r); edgeTriangles.Add(curr_sh_r);
                }

                currentDistance += roadMeshStep;
            }

            // --- Create GameObjects and Meshes ---
            // Center Strip Object
            if (centerVertices.Count >= 4) // Need at least 2 steps for triangles
            {
                CreateRoadSubMesh(
                    $"Road_Spline_{roadSegmentIndex}_Center",
                    roadsContainer.transform, // Parent to main container
                    centerLayer,              // Assign specific layer
                    centerVertices,
                    centerUvs,
                    centerTriangles,
                    centerStripMaterial != null ? centerStripMaterial : roadMaterial // Use specific material or fallback
                );
            }

            // Edges and Shoulders Object
            if (edgeVertices.Count >= 12) // Need at least 2 steps for triangles
            {
                 CreateRoadSubMesh(
                    $"Road_Spline_{roadSegmentIndex}_Edges",
                    roadsContainer.transform,    // Parent to main container
                    roadsContainer.layer,        // Inherit layer from container (usually "Road")
                    edgeVertices,
                    edgeUvs,
                    edgeTriangles,
                    roadMaterial                 // Use standard road material
                );
            }

            roadSegmentIndex++;
        } // End foreach spline

        Debug.Log($"Road generation finished in {(Time.realtimeSinceStartup - startTime):F2} seconds.");
    }


    // --- ДОБАВЛЕНО: Вспомогательная функция для проекции точки на террейн ---
    private float3 ProjectToTerrain(float3 idealPosition, LayerMask terrainMask)
    {
        float raycastDist = 50f; // Search distance up/down
        if (Physics.Raycast(idealPosition + new float3(0, raycastDist / 2f, 0), Vector3.down, out RaycastHit hit, raycastDist, terrainMask))
        {
            return hit.point;
        }
        // Fallback: return original position if terrain not found below/above
        // This might happen at map edges or over holes. Consider logging a warning.
        // Debug.LogWarning($"ProjectToTerrain failed for position {idealPosition}");
        return idealPosition; // Or return idealPosition with a default Y=0? Depends on desired behavior.
    }

     // --- ДОБАВЛЕНО: Вспомогательная функция для создания под-мешей дороги ---
     private void CreateRoadSubMesh(string gameObjectName, Transform parent, int layer, List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, Material material)
     {
         GameObject subMeshObject = new GameObject(gameObjectName);
         subMeshObject.transform.parent = parent;
         subMeshObject.layer = layer; // Assign the specified layer

         Mesh mesh = new Mesh { name = gameObjectName + "_Mesh" };
         mesh.SetVertices(vertices);
         mesh.SetUVs(0, uvs);
         mesh.SetTriangles(triangles, 0);
         mesh.RecalculateNormals();
         mesh.RecalculateBounds();

         MeshFilter mf = subMeshObject.AddComponent<MeshFilter>();
         mf.mesh = mesh;

         MeshRenderer mr = subMeshObject.AddComponent<MeshRenderer>();
         mr.material = material; // Assign the specified material

         // Add collider - crucial for pathfinding raycasts later
         MeshCollider mc = subMeshObject.AddComponent<MeshCollider>();
         mc.sharedMesh = mesh;
         // Optional: Assign physics material if needed
         // if (physicsMaterial != null) mc.material = physicsMaterial;
     }

    [ContextMenu("Roads - Clear")]
    void ClearRoads()
    {
        if (!roadsContainer)
        {
            Debug.LogWarning("Cannot clear roads: Road container is not assigned.");
            return;
        }

        for (int i = roadsContainer.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = roadsContainer.transform.GetChild(i);
            if (child) DestroyImmediate(child.gameObject);
        }

        Debug.Log("All roads removed.");
    }
}