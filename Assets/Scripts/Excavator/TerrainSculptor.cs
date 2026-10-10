using UnityEngine;

// Runtime digging / piling of Terrain with volume bookkeeping, so sand removed by a bucket
// can be put back somewhere else.
public static class TerrainSculptor
{
    private static bool _copied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        _copied = false;
    }

    // В режиме Play работаем с копией TerrainData, чтобы не портить ассет на диске
    public static void EnsureRuntimeCopies(Vector3 referencePoint)
    {
        if (_copied)
        {
            return;
        }

        _copied = true;

        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain.terrainData == null)
            {
                continue;
            }

            TerrainData copy = Object.Instantiate(terrain.terrainData);
            PrepareForDigging(terrain, copy, referencePoint);
            terrain.terrainData = copy;

            TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
            if (terrainCollider != null)
            {
                terrainCollider.terrainData = copy;
            }
        }
    }

    // Рельеф лежит на дне диапазона высот — копать некуда; к тому же грубая сетка высот даёт
    // «ямы» шириной с десяток метров. Поэтому на копии: уточняем сетку и даём запас глубины.
    private const float MinHeadroom = 12f;
    private const float MaxCellSize = 1.2f;
    private const int FineResolution = 2049;

    private static void PrepareForDigging(Terrain terrain, TerrainData data, Vector3 referencePoint)
    {
        Vector3 size = data.size;
        Vector3 origin = terrain.transform.position;

        // Контрольная точка: высоту земли в ней после подготовки возвращаем в точности
        float u = Mathf.Clamp01((referencePoint.x - origin.x) / size.x);
        float v = Mathf.Clamp01((referencePoint.z - origin.z) / size.z);
        float surfaceBefore = origin.y + data.GetInterpolatedHeight(u, v);

        int oldRes = data.heightmapResolution;
        float[,] heights = data.GetHeights(0, 0, oldRes, oldRes);

        // Уточняем сетку сами (билинейно), не полагаясь на пересчёт внутри Unity
        if (size.x / (oldRes - 1) > MaxCellSize && oldRes < FineResolution)
        {
            int newRes = FineResolution;
            float[,] fine = new float[newRes, newRes];
            float ratio = (oldRes - 1) / (float)(newRes - 1);

            for (int z = 0; z < newRes; z++)
            {
                float fz = z * ratio;
                int z0 = Mathf.Min((int)fz, oldRes - 1);
                int z1 = Mathf.Min(z0 + 1, oldRes - 1);
                float tz = fz - z0;

                for (int x = 0; x < newRes; x++)
                {
                    float fx = x * ratio;
                    int x0 = Mathf.Min((int)fx, oldRes - 1);
                    int x1 = Mathf.Min(x0 + 1, oldRes - 1);
                    float tx = fx - x0;

                    float low = Mathf.Lerp(heights[z0, x0], heights[z0, x1], tx);
                    float high = Mathf.Lerp(heights[z1, x0], heights[z1, x1], tx);
                    fine[z, x] = Mathf.Lerp(low, high, tz);
                }
            }

            data.heightmapResolution = newRes;
            data.size = size;
            heights = fine;
        }

        int res = data.heightmapResolution;
        float min = 1f;

        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                if (heights[z, x] < min)
                {
                    min = heights[z, x];
                }
            }
        }

        float lift = Mathf.Max(0f, MinHeadroom - min * size.y);
        float newSizeY = size.y + lift;

        if (lift > 0f)
        {
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    heights[z, x] = (heights[z, x] * size.y + lift) / newSizeY;
                }
            }

            size.y = newSizeY;
            data.size = size;
        }

        data.SetHeights(0, 0, heights);

        // Двигаем объект так, чтобы земля в контрольной точке осталась на прежней высоте
        float surfaceAfter = origin.y + data.GetInterpolatedHeight(u, v);
        terrain.transform.position += Vector3.up * (surfaceBefore - surfaceAfter);
    }

    public static Terrain FindTerrain(Vector3 worldPos)
    {
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            Vector3 origin = terrain.GetPosition();
            Vector3 size = terrain.terrainData.size;

            if (worldPos.x >= origin.x && worldPos.x <= origin.x + size.x &&
                worldPos.z >= origin.z && worldPos.z <= origin.z + size.z)
            {
                return terrain;
            }
        }

        return null;
    }

    // Меняет высоту рельефа вокруг точки (колоколом).
    // deltaHeight < 0 — копаем, > 0 — насыпаем.
    // maxVolume ограничивает объём изменения (м3).
    // Возвращает объём (м3) со знаком: отрицательный при копании, положительный при насыпании.
    public static float Modify(Vector3 worldPos, float radius, float deltaHeight, float maxVolume = float.PositiveInfinity)
    {
        Terrain terrain = FindTerrain(worldPos);
        if (terrain == null || Mathf.Approximately(deltaHeight, 0f))
        {
            return 0f;
        }

        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.GetPosition();
        Vector3 size = data.size;
        int res = data.heightmapResolution;

        float cellX = size.x / (res - 1);
        float cellZ = size.z / (res - 1);

        // Кисть меньше пары ячеек карты высот просто не даст эффекта
        radius = Mathf.Max(radius, 1.5f * Mathf.Max(cellX, cellZ));

        int x0 = Mathf.Max(0, Mathf.FloorToInt((worldPos.x - radius - origin.x) / cellX));
        int x1 = Mathf.Min(res - 1, Mathf.CeilToInt((worldPos.x + radius - origin.x) / cellX));
        int z0 = Mathf.Max(0, Mathf.FloorToInt((worldPos.z - radius - origin.z) / cellZ));
        int z1 = Mathf.Min(res - 1, Mathf.CeilToInt((worldPos.z + radius - origin.z) / cellZ));

        int width = x1 - x0 + 1;
        int height = z1 - z0 + 1;

        if (width <= 0 || height <= 0)
        {
            return 0f;
        }

        float[,] heights = data.GetHeights(x0, z0, width, height);
        float cellArea = cellX * cellZ;

        // Проход 1: сколько объёма хотим изменить
        float weightSum = 0f;

        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                weightSum += Weight(origin, cellX, cellZ, x0 + i, z0 + j, worldPos, radius);
            }
        }

        float potential = weightSum * Mathf.Abs(deltaHeight) * cellArea;

        if (potential <= 0f)
        {
            return 0f;
        }

        float scale = potential > maxVolume ? Mathf.Max(maxVolume, 0f) / potential : 1f;

        // Проход 2: применяем
        float volume = 0f;

        for (int j = 0; j < height; j++)
        {
            for (int i = 0; i < width; i++)
            {
                float weight = Weight(origin, cellX, cellZ, x0 + i, z0 + j, worldPos, radius);

                if (weight <= 0f)
                {
                    continue;
                }

                float oldValue = heights[j, i];
                float newValue = Mathf.Clamp01(oldValue + deltaHeight * weight * scale / size.y);

                heights[j, i] = newValue;
                volume += (newValue - oldValue) * size.y * cellArea;
            }
        }

        data.SetHeights(x0, z0, heights);

        return volume;
    }

    private static float Weight(Vector3 origin, float cellX, float cellZ, int ix, int iz, Vector3 center, float radius)
    {
        float dx = origin.x + ix * cellX - center.x;
        float dz = origin.z + iz * cellZ - center.z;
        float dist = Mathf.Sqrt(dx * dx + dz * dz);

        if (dist >= radius)
        {
            return 0f;
        }

        float t = 1f - dist / radius;
        return t * t * (3f - 2f * t);
    }
}
