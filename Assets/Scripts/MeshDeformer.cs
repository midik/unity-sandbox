using UnityEngine;

public class MeshDeformer : MonoBehaviour
{
    public float deformRadius = 0.3f; // Радиус воздействия
    public float deformStrength = 0.1f; // Глубина вмятины

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] originalVertices;

    void Start()
    {
        mesh = GetComponent<MeshFilter>().mesh;
        vertices = mesh.vertices;
        originalVertices = mesh.vertices.Clone() as Vector3[];
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
                vertices[i].y -= impact;
            }
        }

        mesh.vertices = vertices;
        mesh.RecalculateNormals();
    }
}