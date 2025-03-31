using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct TerrainHeightJob : IJobParallelFor
{
    // Параметры чанка
    [ReadOnly] public int vertsPerLine;
    [ReadOnly] public float step;
    [ReadOnly] public float chunkWorldX;
    [ReadOnly] public float chunkWorldZ;

    // Параметры базового шума
    [ReadOnly] public float maxHeight;
    [ReadOnly] public float terrainScale;
    [ReadOnly] public int octaves;
    [ReadOnly] public float persistence;
    [ReadOnly] public float lacunarity;
    [ReadOnly] public float noiseOffsetX;
    [ReadOnly] public float noiseOffsetZ;

    // Параметры Domain Warping
    [ReadOnly] public bool useDomainWarping;
    [ReadOnly] public float domainWarpScale;
    [ReadOnly] public float domainWarpStrength;
    [ReadOnly] public float domainWarpOffsetX;
    [ReadOnly] public float domainWarpOffsetZ;

    // Предвычисленная кривая высот
    [ReadOnly] public NativeArray<float> heightCurveValues;
    [ReadOnly] public int heightCurveSamples;

    // Параметры шумовых долин
    [ReadOnly] public bool useValleys;
    [ReadOnly] public float valleyNoiseScale;
    [ReadOnly] public float valleyDepth;
    [ReadOnly] public float valleyWidthFactor;
    [ReadOnly] public float valleyNoiseOffsetX;
    [ReadOnly] public float valleyNoiseOffsetZ;

    // Параметры сплайновых долин
    [ReadOnly] public bool useSplineValleys;
    [ReadOnly] public NativeArray<float> splineFactors; // Предвычисленные значения влияния сплайнов
    [ReadOnly] public float splineValleyDepth;

    // Выходной массив высот
    [WriteOnly] public NativeArray<float> heights;

    public void Execute(int index)
    {
        // Преобразование индекса в координаты вершины
        int x = index % vertsPerLine;
        int z = index / vertsPerLine;

        float localX = x * step;
        float localZ = z * step;
        float worldX = chunkWorldX + localX;
        float worldZ = chunkWorldZ + localZ;

        // Вычисление базовой высоты
        float baseHeight = CalculateTerrainHeight(worldX, worldZ);
        float finalHeight = baseHeight;

        // Применение сплайновых долин (используем предвычисленные значения)
        if (useSplineValleys)
        {
            float splineFactor = splineFactors[index];
            float heightReduction = splineFactor * splineValleyDepth;
            finalHeight = baseHeight - heightReduction;
        }

        // Ограничение высоты снизу
        finalHeight = math.max(0, finalHeight);
        heights[index] = finalHeight;
    }

    private float CalculateTerrainHeight(float worldX, float worldZ)
    {
        // Domain Warping (искажение координат)
        float warpedX = worldX;
        float warpedZ = worldZ;
        if (useDomainWarping)
        {
            float warpNoiseX = noise.cnoise(new float2((worldX / domainWarpScale) + domainWarpOffsetX, (worldZ / domainWarpScale))) * 0.5f + 0.5f;
            float warpNoiseZ = noise.cnoise(new float2((worldX / domainWarpScale) + domainWarpOffsetZ, (worldZ / domainWarpScale))) * 0.5f + 0.5f;
            warpedX += (warpNoiseX * 2f - 1f) * domainWarpStrength;
            warpedZ += (warpNoiseZ * 2f - 1f) * domainWarpStrength;
        }

        // Генерация фрактального шума Перлина
        float totalHeight = 0;
        float frequency = 1.0f;
        float amplitude = 1.0f;
        float maxValue = 0;

        for (int i = 0; i < octaves; i++)
        {
            float sampleX = (warpedX / terrainScale * frequency) + noiseOffsetX + i * 0.1f;
            float sampleZ = (warpedZ / terrainScale * frequency) + noiseOffsetZ + i * 0.1f;
            float perlinValue = noise.cnoise(new float2(sampleX, sampleZ)) * 0.5f + 0.5f; // Приведение к [0,1]
            totalHeight += perlinValue * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalizedHeight = (maxValue > 0) ? (totalHeight / maxValue) : 0;

        // Применение кривой высот
        float curvedHeight = SampleCurve(normalizedHeight);
        float baseTerrainHeight = curvedHeight * maxHeight;

        // Добавление шумовых долин
        float heightAfterNoiseValleys = baseTerrainHeight;
        if (useValleys)
        {
            float valleyNoiseX = (warpedX / valleyNoiseScale) + valleyNoiseOffsetX;
            float valleyNoiseZ = (warpedZ / valleyNoiseScale) + valleyNoiseOffsetZ;
            float rawValleyNoise = noise.cnoise(new float2(valleyNoiseX, valleyNoiseZ)) * 0.5f + 0.5f;
            float ridgeNoise = 1.0f - math.abs(rawValleyNoise * 2f - 1f);
            float valleyFactor = math.pow(ridgeNoise, valleyWidthFactor);
            float heightReduction = valleyFactor * valleyDepth;
            heightAfterNoiseValleys = baseTerrainHeight - heightReduction;
        }

        return heightAfterNoiseValleys;
    }

    private float SampleCurve(float t)
    {
        // Линейная интерполяция предвычисленной кривой высот
        t = math.clamp(t, 0f, 1f);
        float indexFloat = t * (heightCurveSamples - 1);
        int index0 = (int)math.floor(indexFloat);
        int index1 = math.min(index0 + 1, heightCurveSamples - 1);
        float frac = indexFloat - index0;
        float value0 = heightCurveValues[index0];
        float value1 = heightCurveValues[index1];
        return math.lerp(value0, value1, frac);
    }
}