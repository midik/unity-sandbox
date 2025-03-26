using UnityEngine;

public class MeshDeformer : MonoBehaviour
{
    public float deformRadius = 0.3f;      // Радиус воздействия
    public float deformStrength = 0.1f;     // Глубина вмятины
    public float maxDeformDepth = 0.2f;     // Максимальная глубина относительно оригинала

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] originalVertices;
    private float[] deformableLayer;        // ДС — относительно тонкий слой, который "стирается"
    private MeshCollider meshCollider;

    void Start()
    {
        mesh = GetComponentInChildren<MeshFilter>().mesh;
        meshCollider = GetComponent<MeshCollider>();

        vertices = mesh.vertices;
        originalVertices = mesh.vertices.Clone() as Vector3[];
        deformableLayer = new float[vertices.Length];

        for (int i = 0; i < deformableLayer.Length; i++)
        {
            deformableLayer[i] = maxDeformDepth;
        }
    }

    public void DeformAtPoint(Vector3 worldPos)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);

        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = Vector2.Distance(new Vector2(vertices[i].x, vertices[i].z), new Vector2(localPos.x, localPos.z));

            if (distance < deformRadius)
            {
                float impact = Mathf.Lerp(deformStrength, 0, distance / deformRadius);
                deformableLayer[i] = Mathf.Max(0, deformableLayer[i] - impact); // уменьшаем слой, но не уходим в минус

                vertices[i].y = originalVertices[i].y - (maxDeformDepth - deformableLayer[i]);
            }
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // Обновляем MeshCollider, иначе физика не увидит
        if (meshCollider)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
        }
    }
}