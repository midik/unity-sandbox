#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public class RuntimeMeshTerrain : MonoBehaviour
{
    public MeshFilter meshFilter;
    public MeshCollider meshCollider;
    public Mesh mesh;

    public int resolution = 128;  // Базовое разрешение (кол-во ячеек)
    public float size = 50;       // Размер террейна в мире
    public float maxHeight = 3;   // Максимальная высота
    public float perlinScale = 10;// Масштаб перлина

    private Vector3[] vertices;
    private int[] triangles;

    void Start()
    {
        GenerateTerrain();
    }

    [ContextMenu("Generate Terrain")]
    void GenerateTerrain()
    {
        int vertsPerRow = resolution + 1; // Количество вершин в ряду
        vertices = new Vector3[vertsPerRow * vertsPerRow];
        
        if (vertices.Length > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        
        triangles = new int[resolution * resolution * 6];

        float stepSize = size / resolution; // Шаг между вершинами

        for (int z = 0; z < vertsPerRow; z++)
        {
            for (int x = 0; x < vertsPerRow; x++)
            {
                int index = x + z * vertsPerRow;
                float xCoord = (float)x / resolution * perlinScale;
                float zCoord = (float)z / resolution * perlinScale;
                float height = Mathf.PerlinNoise(xCoord, zCoord) * maxHeight;

                vertices[index] = new Vector3(x * stepSize, height, z * stepSize);
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int topLeft = x + z * vertsPerRow;
                int topRight = (x + 1) + z * vertsPerRow;
                int bottomLeft = x + (z + 1) * vertsPerRow;
                int bottomRight = (x + 1) + (z + 1) * vertsPerRow;

                // Первый треугольник
                triangles[triIndex++] = topLeft;
                triangles[triIndex++] = bottomLeft;
                triangles[triIndex++] = topRight;

                // Второй треугольник
                triangles[triIndex++] = topRight;
                triangles[triIndex++] = bottomLeft;
                triangles[triIndex++] = bottomRight;
            }
        }

        UpdateMesh();
    }

    void UpdateMesh()
    {
        if (mesh == null)
        {
            mesh = new Mesh();
        }

        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = mesh;
    }
}
