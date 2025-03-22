using UnityEngine;

public class ChunkedTerrainGenerator : MonoBehaviour
{
    [Header("Chunk Settings")]
    public int chunksX = 4;
    public int chunksZ = 4;
    public int resolutionPerChunk = 64;
    public float sizePerChunk = 10;
    public float maxHeight = 2;
    public float perlinScale = 300f;
    public Material terrainMaterial;

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
    }

    void GenerateChunk(int chunkX, int chunkZ)
    {
        GameObject chunk = new GameObject($"Chunk_{chunkX}_{chunkZ}");
        chunk.transform.parent = transform;
        chunk.transform.position = new Vector3(chunkX * sizePerChunk, 0, chunkZ * sizePerChunk);

        MeshFilter mf = chunk.AddComponent<MeshFilter>();
        MeshRenderer mr = chunk.AddComponent<MeshRenderer>();
        MeshCollider mc = chunk.AddComponent<MeshCollider>();
        
        chunk.AddComponent<MeshDeformer>();
        // todo set deformRadius/deformStrength here

        mr.material = terrainMaterial;

        Mesh mesh = new Mesh();
        int vertsPerLine = resolutionPerChunk + 1;
        Vector3[] vertices = new Vector3[vertsPerLine * vertsPerLine];
        int[] triangles = new int[resolutionPerChunk * resolutionPerChunk * 6];

        for (int z = 0; z < vertsPerLine; z++)
        {
            for (int x = 0; x < vertsPerLine; x++)
            {
                float worldX = (chunkX * resolutionPerChunk + x) / (float)(chunksX * resolutionPerChunk);
                float worldZ = (chunkZ * resolutionPerChunk + z) / (float)(chunksZ * resolutionPerChunk);
                float y = Mathf.PerlinNoise(worldX * perlinScale, worldZ * perlinScale) * maxHeight;
                vertices[x + z * vertsPerLine] = new Vector3(x * sizePerChunk / resolutionPerChunk, y, z * sizePerChunk / resolutionPerChunk);
            }
        }

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
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
        mc.sharedMesh = mesh;
    }
    [ContextMenu("Clear")]
    void ClearChunks()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

}
