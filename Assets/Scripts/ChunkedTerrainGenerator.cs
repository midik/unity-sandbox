using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
    private List<Spline> cachedSplines;


    void Start()
    {
        // При старте игры используем корутину для асинхронной генерации
        // StartCoroutine(GenerateChunksCoroutine());
    }

    // Метод для вызова из Context Menu в редакторе
    [ContextMenu("Terrain - Generate")]
    void GenerateContextMenu()
    {
#if UNITY_EDITOR
        // В редакторе (не в Play Mode) запускаем синхронно с ProgressBar
        if (!Application.isPlaying)
        {
            GenerateChunks();
        }
        else
        {
            // В Play Mode из меню лучше тоже запустить корутину
            StopAllCoroutines(); // Остановим предыдущую, если была
            StartCoroutine(GenerateChunksCoroutine());
            Debug.LogWarning("Started generation coroutine from context menu in Play Mode.");
        }
#else
        // В билде ContextMenu недоступно, но если бы было, вызвали бы корутину
         StopAllCoroutines();
         StartCoroutine(GenerateChunksCoroutine());
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
                        // Очищаем уже созданные чанки в этой сессии генерации, если отменили
                        ClearChunks();
                        return; // Выходим из метода
                    }

                    GenerateChunk(x, z); // Генерируем чанк синхронно
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
                ClearChunks(); // Попробуем очистить в случае ошибки
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


    // Асинхронная генерация через корутину (для Start и Play Mode)
    IEnumerator GenerateChunksCoroutine()
    {
        float startTime = Time.realtimeSinceStartup;
        // Debug.Log("Starting terrain generation coroutine...");
        int totalChunks = chunksX * chunksZ;
        int chunksProcessed = 0;

        if (totalChunks == 0)
        {
            // Debug.LogWarning("chunksX or chunksZ is zero. No chunks to generate.");
            yield break; // Выходим из корутины
        }

        // Очистка чанков перед началом
        ClearChunks();
        yield return null; // Даем кадру обновиться

        // Кеширование сплайнов
        CacheSplines();
        yield return null; // Даем кадру обновиться

        for (int z = 0; z < chunksZ; z++)
        {
            for (int x = 0; x < chunksX; x++)
            {
                try
                {
                    GenerateChunk(x, z); // Генерируем один чанк
                }
                catch (Exception e)
                {
                    // Debug.LogError($"Error generating chunk ({x},{z}): {e}");
                    // Можно решить, прерывать ли всю генерацию или пропустить чанк
                    // yield break; // Прервать всю генерацию
                }

                chunksProcessed++;

                // Выводим лог прогресса
                if (chunksProcessed % 10 == 0 || chunksProcessed == totalChunks)
                {
                    // Debug.Log($"Terrain generation progress: {chunksProcessed}/{totalChunks} chunks generated.");
                }

                // !!! Отдаем управление Unity, чтобы редактор/игра не зависали !!!
                yield return null;
            }
            // yield return null; // Можно добавить ожидание после каждой строки (z)
        }

        // Убедимся, что все чанки сгенерировались (на случай пропуска из-за ошибки)
        int finalChunkCount = transform.childCount;
        if (finalChunkCount != totalChunks)
        {
            // Debug.LogWarning(
            //     $"Expected {totalChunks} chunks, but found {finalChunkCount}. Some chunks might have failed to generate.");
        }

        OnChunksRegenerated?.Invoke();
        // Debug.Log($"Terrain generation COROUTINE finished in {(Time.realtimeSinceStartup - startTime):F2} seconds.");
    }

    void CacheSplines()
    {
        cachedSplines = new List<Spline>();
        // Проверяем, назначено ли поле в инспекторе
        if (!splineContainer)
        {
            // Выводим предупреждение только если сплайновые долины включены
            if (useSplineValleys)
            {
                Debug.LogWarning("Spline Valleys are enabled, but SplineContainer is not assigned in the inspector!");
            }

            return; // Выходим, если контейнер не назначен
        }

        // Проверяем, существует ли сам GameObject контейнера
        if (!splineContainer.gameObject)
        {
            Debug.LogWarning("SplineContainer is assigned but its GameObject might be missing or destroyed.");
            return; // Выходим, если объект недействителен
        }

        cachedSplines.AddRange(splineContainer.Splines);

        // Логгируем количество найденных сплайнов
        if (useSplineValleys && cachedSplines.Count == 0)
        {
            Debug.LogWarning(
                $"Spline Valleys enabled, SplineContainer '{splineContainer.name}' assigned, but it contains 0 splines.");
        }
        else if (useSplineValleys)
        {
            Debug.Log($"Cached {cachedSplines.Count} splines from '{splineContainer.name}' for valley generation.");
        }
    }

    void GenerateChunk(int chunkX, int chunkZ)
    {
        // ... (Начало метода, создание объектов, сетки) ...
        GameObject chunk = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunk.layer = LayerMask.NameToLayer("TerrainChunk");
        chunk.transform.parent = transform;
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
        Vector3[] vertices = new Vector3[vertsPerLine * vertsPerLine];
        Vector2[] uvs = new Vector2[vertsPerLine * vertsPerLine];
        int[] triangles = new int[resolutionPerChunk * resolutionPerChunk * 6];
        float step = sizePerChunk / resolutionPerChunk;
        float actualSplineValleyFalloff = Mathf.Max(0.01f, splineValleyFalloff);
        float halfSplineWidth = splineValleyWidth / 2f;
        float fullInfluenceRadius = halfSplineWidth + actualSplineValleyFalloff;
        float fullInfluenceRadiusSq = fullInfluenceRadius * fullInfluenceRadius;
        bool debugFirstVertex = (chunkX == 0 && chunkZ == 0);


        for (int z = 0; z < vertsPerLine; z++)
        {
            for (int x = 0; x < vertsPerLine; x++)
            {
                bool isFirstVertex = (debugFirstVertex && x == 0 && z == 0);
                float localX = x * step;
                float localZ = z * step;
                float worldX = chunk.transform.position.x + localX;
                float worldZ = chunk.transform.position.z + localZ;
                float3 worldPos = new float3(worldX, 0, worldZ);

                float baseHeight = GetTerrainHeight(worldX, worldZ);
                float finalHeight = baseHeight;

                //if (isFirstVertex) Debug.Log($"[Chunk 0,0 Vert 0,0] Base height (before spline): {baseHeight:F2}"); // Отладочный лог можно оставить или убрать

                // --- Применяем сплайновые долины ---
                if (useSplineValleys && cachedSplines != null && cachedSplines.Count > 0)
                {
                    float minDistanceToSplineSq = float.MaxValue;
                    foreach (var spline in cachedSplines)
                    {
                        if (spline == null || spline.Knots == null || spline.Knots.Count() < 2) continue;
                        SplineUtility.GetNearestPoint(spline, worldPos, out float3 nearestPoint, out _, 3, 8);
                        float distSq = math.distancesq(new float2(worldPos.x, worldPos.z),
                            new float2(nearestPoint.x, nearestPoint.z));
                        minDistanceToSplineSq = math.min(minDistanceToSplineSq, distSq);
                    }

                    if (minDistanceToSplineSq < fullInfluenceRadiusSq)
                    {
                        float minDistanceToSpline = math.sqrt(minDistanceToSplineSq);
                        float splineFactor = 0f;

                        if (minDistanceToSpline <= halfSplineWidth) // Дно
                        {
                            splineFactor = 1.0f;
                        }
                        else // Склон
                        {
                            // Нормализуем позицию на склоне от 0 до 1
                            float t = (minDistanceToSpline - halfSplineWidth) / actualSplineValleyFalloff;
                            // ----- ИСПОЛЬЗУЕМ КРИВУЮ -----
                            // Передаем t в кривую. Кривая должна быть настроена так,
                            // чтобы при t=0 значение было 1 (полная глубина), при t=1 значение было 0 (нет глубины).
                            splineFactor = valleySlopeCurve.Evaluate(t);
                            // ---------------------------
                        }

                        splineFactor = math.clamp(splineFactor, 0f, 1f); // Убедимся, что фактор в [0,1]

                        float heightReduction = splineFactor * splineValleyDepth;
                        finalHeight = baseHeight - heightReduction;
                    }
                }
                // --- Конец сплайновых долин ---

                finalHeight = Mathf.Max(0, finalHeight);

                int index = x + z * vertsPerLine;
                vertices[index] = new Vector3(localX, finalHeight, localZ);
                uvs[index] = new Vector2((float)x / resolutionPerChunk, (float)z / resolutionPerChunk);
            }
        }

        // ... (Заполнение triangles, назначение mesh, RecalculateNormals) ...
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

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.mesh = mesh;
        mc.sharedMesh = mesh;
    }

    // Функция расчета высоты БЕЗ сплайновых долин
    float GetTerrainHeight(float worldX, float worldZ)
    {
        // Шаг 1: Domain Warping
        float warpedX = worldX;
        float warpedZ = worldZ;
        if (useDomainWarping)
        {
            // Используем 4 вызова для более сложного варпинга (можно упростить до 2)
            float warpNoiseX_1 =
                Mathf.PerlinNoise((worldX / domainWarpScale) + domainWarpOffsetX, (worldZ / domainWarpScale));
            float warpNoiseX_2 = Mathf.PerlinNoise((worldX / domainWarpScale),
                (worldZ / domainWarpScale) + domainWarpOffsetX + 100f); // Смещение для второго
            float warpNoiseZ_1 =
                Mathf.PerlinNoise((worldX / domainWarpScale) + domainWarpOffsetZ, (worldZ / domainWarpScale));
            float warpNoiseZ_2 = Mathf.PerlinNoise((worldX / domainWarpScale),
                (worldZ / domainWarpScale) + domainWarpOffsetZ + 100f); // Смещение для второго

            float warpOffsetX =
                ((warpNoiseX_1 + warpNoiseX_2) * 0.5f * 2f - 1f) * domainWarpStrength; // Среднее от двух шумов
            float warpOffsetZ =
                ((warpNoiseZ_1 + warpNoiseZ_2) * 0.5f * 2f - 1f) * domainWarpStrength; // Среднее от двух шумов

            warpedX += warpOffsetX;
            warpedZ += warpOffsetZ;
        }

        // Шаг 2: Базовый фрактальный шум
        float totalHeight = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxValue = 0; // Для нормализации
        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (warpedX / terrainScale * frequency) + noiseOffsetX;
            float sampleZ = (warpedZ / terrainScale * frequency) + noiseOffsetZ;
            // Добавим небольшое смещение для каждой октавы, чтобы избежать артефактов
            sampleX += i * 0.1f;
            sampleZ += i * 0.1f;
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ);
            totalHeight += perlinValue * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalizedHeight = (maxValue > 0) ? (totalHeight / maxValue) : 0; // Защита от деления на ноль

        // Шаг 3: Применение кривой высот
        float curvedHeight = heightCurve.Evaluate(normalizedHeight);
        float baseTerrainHeight = curvedHeight * maxHeight;

        // Шаг 4: Формирование долин (Noise Based)
        float heightAfterNoiseValleys = baseTerrainHeight;
        if (useValleys)
        {
            float valleyNoiseX = (warpedX / valleyNoiseScale) + valleyNoiseOffsetX; // Используем warped координаты
            float valleyNoiseZ = (warpedZ / valleyNoiseScale) + valleyNoiseOffsetZ;
            float rawValleyNoise = Mathf.PerlinNoise(valleyNoiseX, valleyNoiseZ);
            float ridgeNoise = 1.0f - Mathf.Abs(rawValleyNoise * 2f - 1f);
            float valleyFactor = Mathf.Pow(ridgeNoise, valleyWidthFactor);
            float heightReduction = valleyFactor * valleyDepth;
            heightAfterNoiseValleys = baseTerrainHeight - heightReduction;
            // Не ограничиваем здесь, ограничение будет после сплайновых долин
        }

        return heightAfterNoiseValleys;
    }

    // Метод очистки чанков
    void ClearChunks()
    {
        bool isEditorNotInPlayMode = false;
#if UNITY_EDITOR
        isEditorNotInPlayMode = !Application.isPlaying;
#endif

        // Используем DestroyImmediate ТОЛЬКО в редакторе вне Play Mode
        if (isEditorNotInPlayMode)
        {
            // Debug.Log("Clearing chunks using DestroyImmediate.");
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child) DestroyImmediate(child.gameObject);
            }
        }
        else // В Play Mode (в редакторе или билде) используем Destroy
        {
            // Debug.Log("Clearing chunks using Destroy.");
            foreach (Transform child in transform)
            {
                if (child) Destroy(child.gameObject);
            }
            // Destroy сам по себе отложенный, но для синхронности можно подождать конца кадра,
            // но это усложнит логику, пока оставим так.
        }
    }

    [ContextMenu("Roads - Generate")]
    void GenerateRoads()
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
        Debug.Log("Starting road generation...");

        ClearRoads();

        LayerMask terrainMask = LayerMask.GetMask("TerrainChunk");
        Vector3 raiseVector = Vector3.up * roadRaise; // Используем мировой Vector3.up

        int roadSegmentIndex = 0;
        foreach (var spline in cachedSplines)
        {
            if (spline == null || spline.Knots == null || spline.Knots.Count() < 2) continue;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();
            float currentDistance = 0f; // Пройденное расстояние для V-координаты

            float splineLength = spline.GetLength(); // Получаем длину сплайна
            if (splineLength < roadMeshStep) continue; // Сплайн слишком короткий

            // Итерация по длине сплайна с заданным шагом
            for (float dist = 0; dist <= splineLength; dist += roadMeshStep)
            {
                // Получаем параметр t (0..1) по расстоянию
                float t = dist / splineLength;
                // Получаем точку, тангенс и нормаль на сплайне
                spline.Evaluate(t, out float3 pos, out float3 dir, out float3 up);

                if (math.lengthsq(dir) < 0.001f) continue; // Пропускаем, если тангенс нулевой
                dir = math.normalize(dir);
                up = math.normalize(up);

                float3 right = math.normalize(math.cross(dir, up));

                // Идеальные точки краев дороги
                float3 p_left_ideal = pos - right * (roadWidth / 2f);
                float3 p_right_ideal = pos + right * (roadWidth / 2f);

                // Проецируем на террейн
                Vector3 p_left_real = p_left_ideal; // Значение по умолчанию, если Raycast не сработает
                Vector3 p_right_real = p_right_ideal;
                float raycastDist = 50f; // Достаточно длинный луч вниз

                RaycastHit hitLeft;
                if (Physics.Raycast(p_left_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down, out hitLeft,
                        raycastDist, terrainMask))
                {
                    p_left_real = hitLeft.point;
                }
                else
                {
                    Debug.DrawRay(p_left_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down * raycastDist,
                        Color.red, 5f);
                } // Луч, если не попал

                RaycastHit hitRight;
                if (Physics.Raycast(p_right_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down, out hitRight,
                        raycastDist, terrainMask))
                {
                    p_right_real = hitRight.point;
                }
                else
                {
                    Debug.DrawRay(p_right_ideal + (float3)Vector3.up * (raycastDist / 2f), Vector3.down * raycastDist,
                        Color.red, 5f);
                } // Луч, если не попал


                // Добавляем вершины (приподнятые)
                int vertIndex = vertices.Count;
                vertices.Add(p_left_real + raiseVector);
                vertices.Add(p_right_real + raiseVector);

                // Добавляем UV (V зависит от пройденного расстояния)
                // Можно добавить множитель для тайлинга текстуры
                float vCoord = currentDistance / (roadWidth); // Пример: тайлинг зависит от ширины
                uvs.Add(new Vector2(0, vCoord));
                uvs.Add(new Vector2(1, vCoord));

                // Добавляем треугольники (если это не первая пара вершин)
                if (vertIndex > 0)
                {
                    // Треугольник 1: (N-2) -> (N-1) -> N
                    triangles.Add(vertIndex - 2);
                    triangles.Add(vertIndex - 1);
                    triangles.Add(vertIndex + 0);

                    // Треугольник 2: N -> (N-1) -> (N+1)
                    triangles.Add(vertIndex + 0);
                    triangles.Add(vertIndex - 1);
                    triangles.Add(vertIndex + 1);
                }

                currentDistance += roadMeshStep; // Увеличиваем пройденное расстояние
            }

            // Создаем объект и меш для этого сегмента дороги
            if (vertices.Count >= 4)
            {
                GameObject roadSegment = new GameObject($"Road_Spline_{roadSegmentIndex}");
                roadSegment.transform.parent = roads.transform;

                Mesh roadMesh = new Mesh { name = $"RoadMesh_{roadSegmentIndex}" };
                roadMesh.vertices = vertices.ToArray();
                roadMesh.uv = uvs.ToArray();
                roadMesh.triangles = triangles.ToArray();
                roadMesh.RecalculateNormals();
                roadMesh.RecalculateBounds();

                MeshFilter roadMF = roadSegment.AddComponent<MeshFilter>();
                roadMF.mesh = roadMesh;

                MeshRenderer roadMR = roadSegment.AddComponent<MeshRenderer>();
                roadMR.material = roadMaterial;

                // Опционально: добавить коллайдер
                MeshCollider roadMC = roadSegment.AddComponent<MeshCollider>();
                roadMC.sharedMesh = roadMesh;
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
            if (child) Destroy(child.gameObject);
        }
       
        Debug.Log("All roads removed.");
    }
}