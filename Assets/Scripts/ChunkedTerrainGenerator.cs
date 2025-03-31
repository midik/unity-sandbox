using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Splines;

#if UNITY_EDITOR
using UnityEditor; // Добавляем для ProgressBar
#endif

public class ChunkedTerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")] public int chunksX = 4;
    public int chunksZ = 4;
    public int resolutionPerChunk = 64; // Вершин на сторону чанка - 1
    public float sizePerChunk = 10;

    [Header("Terrain Generation - Base Noise")]
    public float maxHeight = 15; // Общая максимальная высота

    public float terrainScale = 64.0f; // Масштаб базового шума
    public int octaves = 5; // Количество слоев шума
    [Range(0f, 1f)] public float persistence = 0.387f; // Уменьшение амплитуды октав
    public float lacunarity = 3.0f; // Увеличение частоты октав
    public float noiseOffsetX = 20f; // Смещение основного шума по X
    public float noiseOffsetZ = 50f; // Смещение основного шума по Z

    [Header("Domain Warping")] public bool useDomainWarping = true; // Включить/выключить
    public float domainWarpScale = 100f; // Масштаб шума для искажения координат
    public float domainWarpStrength = 10f; // Сила искажения координат
    public float domainWarpOffsetX = 1000f; // Смещение для X-искажения (отличное от основного)
    public float domainWarpOffsetZ = 2000f; // Смещение для Z-искажения (отличное от основного)

    [Header("Terrain Shaping")] public AnimationCurve heightCurve = AnimationCurve.Linear(0, 0, 1, 1); // Кривая высот

    [Header("Valleys (Noise Based)")] public bool useValleys = true; // Включить/выключить шумные долины
    public float valleyNoiseScale = 150f; // Масштаб шума долин (обычно больше terrainScale)
    public float valleyDepth = 8f; // Максимальная глубина долины
    [Range(1f, 10f)] public float valleyWidthFactor = 4f; // Влияет на ширину/резкость краев долин (больше = уже)
    public float valleyNoiseOffsetX = 3000f; // Смещение шума долин X
    public float valleyNoiseOffsetZ = 4000f; // Смещение шума долин Z

    [Header("Valleys (Spline Based)")] public bool useSplineValleys = true; // Включить/выключить сплайновые долины
    public SplineContainer splineContainer; // Сюда перетащить объект со сплайнами из сцены
    public float splineValleyWidth = 5f; // Ширина плоского дна долины
    public float splineValleyDepth = 6f; // Глубина долины в центре относительно окружающего рельефа
    public float splineValleyFalloff = 10f; // Расстояние, на котором долина переходит в обычный рельеф (ширина склона)

    // По умолчанию EaseInOut: начинается и заканчивается плавно, похоже на smoothstep
    // Ключи: (время=0, значение=1), (время=1, значение=0)
    public AnimationCurve valleySlopeCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Road Generation")] public bool generateRoad = true;

    [Tooltip("Объект, в который будут помещены все сегменты дороги")]
    public GameObject roads;

    [Tooltip("Использовать те же сплайны, что и для долин")]
    public bool useValleySplinesForRoads = true; // Если нужно отдельные сплайны - добавить еще SplineContainer

    public float roadWidth = 4f; // Ширина дороги
    public Material roadMaterial; // Материал дороги

    [Tooltip("Шаг в единицах длины сплайна для создания вершин дороги")]
    public float roadMeshStep = 1.0f; // Например, 1 метр

    [Tooltip("Насколько приподнять меш дороги над террейном")]
    public float roadRaise = 0.05f;

    [Header("Deformation")] public float deformRadius = 0.35f;
    public float deformStrength = 0.1f;
    public float maxDeformDepth = 0.2f;

    [Header("Materials")] public Material terrainMaterial;
    public PhysicsMaterial physicsMaterial;

    public static event Action OnChunksRegenerated;
    internal List<Spline> cachedSplines = null;

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
        if (!roads)
        {
            Debug.LogWarning("Cannot generate roads: Road container is not assigned.");
            return;
        }

        if (!roadMaterial)
        {
            Debug.LogWarning("Cannot generate roads: Road Material is not assigned.");
            return;
        }

        if (cachedSplines == null || cachedSplines.Count == 0)
        {
            CacheSplines();
        }

        if (!useValleySplinesForRoads || cachedSplines == null || cachedSplines.Count == 0)
        {
            Debug.LogWarning("Cannot generate roads: Road generation enabled, but no valid splines found or assigned.");
            return;
        }

        float startTime = Time.realtimeSinceStartup;
        Debug.Log("Starting road generation (with shoulders)...");

        ClearRoads();

        LayerMask terrainMask = LayerMask.GetMask("TerrainChunk");
        Vector3 raiseVector = Vector3.up * roadRaise; // Мировой вектор подъема дороги

        int roadSegmentIndex = 0;
        foreach (var spline in cachedSplines)
        {
            if (spline == null || spline.Knots == null || spline.Knots.Count() < 2) continue;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float currentDistance = 0f;
            float splineLength = spline.GetLength();
            if (splineLength < roadMeshStep) continue;

            // Коэффициенты для UV обочин (можно сделать параметрами)
            float shoulderU_Start = -0.1f; // UV координата U для нижнего края обочины
            float roadU_Start = 0f; // UV координата U для края дороги
            float roadU_End = 1f; // UV координата U для другого края дороги
            float shoulderU_End = 1.1f; // UV координата U для нижнего края другой обочины

            for (float dist = 0; dist <= splineLength; dist += roadMeshStep)
            {
                float t = Mathf.Clamp01(dist / splineLength); // t от 0 до 1
                spline.Evaluate(t, out float3 pos, out float3 dir, out float3 up);

                if (math.lengthsq(dir) < 0.001f) dir = math.forward(); // Запасной вариант, если тангенс нулевой
                else dir = math.normalize(dir);
                // Используем мировой Vector3.up, чтобы избежать проблем с наклоном сплайна
                up = (float3)Vector3.up;
                float3 right = math.normalize(math.cross(dir, up));

                // Идеальные точки краев дороги (горизонтальные)
                float3 p_left_ideal = pos - right * (roadWidth / 2f);
                float3 p_right_ideal = pos + right * (roadWidth / 2f);

                // Проецируем на террейн, чтобы получить точки НИЗА обочины
                Vector3 p_left_terrain = p_left_ideal;
                Vector3 p_right_terrain = p_right_ideal;
                float raycastDist = 50f; // Длина луча для поиска террейна

                RaycastHit hitLeft;
                if (Physics.Raycast(p_left_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down, out hitLeft,
                        raycastDist, terrainMask))
                {
                    p_left_terrain = hitLeft.point;
                } // TODO: Обработать случай, если Raycast не попал (например, у края карты)

                RaycastHit hitRight;
                if (Physics.Raycast(p_right_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down, out hitRight,
                        raycastDist, terrainMask))
                {
                    p_right_terrain = hitRight.point;
                } // TODO: Обработать случай, если Raycast не попал

                // Рассчитываем точки ВЕРХА дороги (приподнятые над точками террейна)
                Vector3 p_left_road = p_left_terrain + raiseVector;
                Vector3 p_right_road = p_right_terrain + raiseVector;

                // Добавляем 4 вершины в список
                int vertIndex = vertices.Count;
                vertices.Add(p_left_terrain); // Индекс vertIndex + 0
                vertices.Add(p_left_road); // Индекс vertIndex + 1
                vertices.Add(p_right_road); // Индекс vertIndex + 2
                vertices.Add(p_right_terrain); // Индекс vertIndex + 3

                // Добавляем UV координаты
                // V координата зависит от пройденного расстояния (можно добавить множитель тайлинга)
                float vCoord = currentDistance / (roadWidth * 1.5f); // Примерный тайлинг по ширине
                uvs.Add(new Vector2(shoulderU_Start, vCoord)); // UV для p_left_terrain
                uvs.Add(new Vector2(roadU_Start, vCoord)); // UV для p_left_road
                uvs.Add(new Vector2(roadU_End, vCoord)); // UV для p_right_road
                uvs.Add(new Vector2(shoulderU_End, vCoord)); // UV для p_right_terrain

                // Добавляем треугольники (если это не первая четверка вершин)
                if (vertIndex >= 4)
                {
                    int prev_lt = vertIndex - 4; // Индексы предыдущих вершин
                    int prev_lr = vertIndex - 3;
                    int prev_rr = vertIndex - 2;
                    int prev_rt = vertIndex - 1;

                    int curr_lt = vertIndex + 0; // Индексы текущих вершин
                    int curr_lr = vertIndex + 1;
                    int curr_rr = vertIndex + 2;
                    int curr_rt = vertIndex + 3;

                    // Левая обочина (два треугольника)
                    triangles.Add(prev_lt);
                    triangles.Add(prev_lr);
                    triangles.Add(curr_lt);
                    triangles.Add(curr_lt);
                    triangles.Add(prev_lr);
                    triangles.Add(curr_lr);

                    // Поверхность дороги (два треугольника)
                    triangles.Add(prev_lr);
                    triangles.Add(prev_rr);
                    triangles.Add(curr_lr);
                    triangles.Add(curr_lr);
                    triangles.Add(prev_rr);
                    triangles.Add(curr_rr);

                    // Правая обочина (два треугольника)
                    triangles.Add(prev_rr);
                    triangles.Add(prev_rt);
                    triangles.Add(curr_rr);
                    triangles.Add(curr_rr);
                    triangles.Add(prev_rt);
                    triangles.Add(curr_rt);
                }

                currentDistance += roadMeshStep;
            }

            // Создаем объект и меш (как раньше, но теперь он включает и обочины)
            if (vertices.Count >= 8) // Нужно минимум 2 сегмента (8 вершин)
            {
                GameObject roadSegment = new GameObject($"Road_Spline_{roadSegmentIndex}");
                roadSegment.transform.parent = roads.transform;
                Mesh roadMesh = new Mesh { name = $"RoadMesh_{roadSegmentIndex}" };
                roadMesh.SetVertices(vertices); // Используем SetVertices для List<Vector3>
                roadMesh.SetUVs(0, uvs); // Используем SetUVs для List<Vector2>
                roadMesh.SetTriangles(triangles, 0); // Используем SetTriangles для List<int>
                roadMesh.RecalculateNormals();
                roadMesh.RecalculateBounds();
                MeshFilter roadMF = roadSegment.AddComponent<MeshFilter>();
                roadMF.mesh = roadMesh;
                MeshRenderer roadMR = roadSegment.AddComponent<MeshRenderer>();
                roadMR.material = roadMaterial;
                // MeshCollider roadMC = roadSegment.AddComponent<MeshCollider>(); // Опционально
                // roadMC.sharedMesh = roadMesh;
            }

            roadSegmentIndex++;
        }

        Debug.Log($"Road generation finished in {(Time.realtimeSinceStartup - startTime):F2} seconds.");
    }

    [ContextMenu("Roads - Clear")]
    void ClearRoads()
    {
        if (!roads)
        {
            Debug.LogWarning("Cannot clear roads: Road container is not assigned.");
            return;
        }

        for (int i = roads.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = roads.transform.GetChild(i);
            if (child) DestroyImmediate(child.gameObject);
        }

        Debug.Log("All roads removed.");
    }
}