#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RuntimeMeshTerrain : MonoBehaviour
{
    public MeshFilter meshFilter; // Используем существующий MeshFilter
    public MeshCollider meshCollider;
    public Mesh mesh;
    
    public int resolution = 128;
    public float size = 10;
    public float maxHeight = 2;
    public float perlinScale = 5;

    private Vector3[] vertices;
    private int[] triangles;

    void Start()
    {
        GenerateTerrain();
    }

    [ContextMenu("Generate Terrain")]
    void GenerateTerrain()
    {
        int vertsPerRow = resolution + 1;
        vertices = new Vector3[vertsPerRow * vertsPerRow];
        triangles = new int[resolution * resolution * 6];

        for (int z = 0; z <= resolution; z++)
        {
            for (int x = 0; x <= resolution; x++)
            {
                int index = x + z * vertsPerRow;
                float height = Mathf.PerlinNoise(x * perlinScale / resolution, z * perlinScale / resolution) * maxHeight;
                vertices[index] = new Vector3(x * size / resolution, height, z * size / resolution);
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

                triangles[triIndex++] = topLeft;
                triangles[triIndex++] = bottomLeft;
                triangles[triIndex++] = topRight;

                triangles[triIndex++] = topRight;
                triangles[triIndex++] = bottomLeft;
                triangles[triIndex++] = bottomRight;
            }
        }

        UpdateMesh();
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        meshFilter.mesh = mesh; // Применяем новый меш

        // 💡 Обновляем MeshCollider после изменения меша
        meshCollider.sharedMesh = null;  // Сбрасываем меш
        meshCollider.sharedMesh = mesh;  // Применяем новый
    }

#if UNITY_EDITOR
    [ContextMenu("Save Mesh")]
    void SaveMesh()
    {
        string path = "Assets/GeneratedTerrain.asset";
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        Debug.Log("Mesh saved to: " + path);
    }
#endif
}
