using UnityEngine;

public class MeshDeformer : MonoBehaviour
{
    public float deformRadius = 0.3f;      // Радиус воздействия
    public float deformStrength = 0.1f;     // Глубина вмятины
    public float maxDeformDepth = 0.2f;     // Максимальная глубина относительно оригинала
    
    [Header("Track Texture")]
    [Tooltip("Determines how quickly the track texture appears (higher = faster appearance)")]
    public float trackTextureStrength = 1.0f;
    [Tooltip("Maximum track texture blend (0-1)")]
    [Range(0, 1)]
    public float maxTrackBlend = 0.8f;

    private Mesh mesh;
    private Vector3[] vertices;
    private Vector3[] originalVertices;
    private float[] deformableLayer;        // ДС — относительно тонкий слой, который "стирается"
    private Color[] vertexColors;           // Цвета вершин для блендинга текстур
    private MeshCollider meshCollider;

    void Start()
    {
        mesh = GetComponentInChildren<MeshFilter>().mesh;
        meshCollider = GetComponent<MeshCollider>();

        vertices = mesh.vertices;
        originalVertices = mesh.vertices.Clone() as Vector3[];
        deformableLayer = new float[vertices.Length];
        
        // Initialize vertex colors - red channel will store track blend factor
        vertexColors = new Color[vertices.Length];
        for (int i = 0; i < vertexColors.Length; i++)
        {
            vertexColors[i] = Color.black; // Start with no track texture visible
        }

        for (int i = 0; i < deformableLayer.Length; i++)
        {
            deformableLayer[i] = maxDeformDepth;
        }
        
        // Apply initial colors to mesh
        mesh.colors = vertexColors;
    }

    public void DeformAtPoint(Vector3 worldPos)
    {
        Vector3 localPos = transform.InverseTransformPoint(worldPos);

        for (int i = 0; i < vertices.Length; i++)
        {
            float distance = Vector2.Distance(new Vector2(vertices[i].x, vertices[i].z), new Vector2(localPos.x, localPos.z));

            if (distance < deformRadius)
            {
                // Calculate impact based on distance (stronger in center, weaker at edges)
                float impact = Mathf.Lerp(deformStrength, 0, distance / deformRadius);
                
                // Update deformable layer
                deformableLayer[i] = Mathf.Max(0, deformableLayer[i] - impact); // уменьшаем слой, но не уходим в минус

                // Update vertex position
                vertices[i].y = originalVertices[i].y - (maxDeformDepth - deformableLayer[i]);
                
                // Calculate texture blend value - stronger where deformation is deeper
                float deformRatio = 1.0f - (deformableLayer[i] / maxDeformDepth); // 0 = no deform, 1 = max deform
                float trackBlend = Mathf.Min(maxTrackBlend, vertexColors[i].r + (impact * trackTextureStrength));
                
                // Store blend value in the red channel
                vertexColors[i].r = trackBlend;
            }
        }

        // Apply changes to the mesh
        mesh.vertices = vertices;
        mesh.colors = vertexColors;
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
