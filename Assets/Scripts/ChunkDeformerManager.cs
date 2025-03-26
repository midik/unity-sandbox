using UnityEngine;

public class ChunkDeformerManager : MonoBehaviour
{
    private MeshDeformer deformer;
    private ChunkedTerrainGenerator generator;
    private Bounds bounds;
    
    private float deformRadius;

    private void Start()
    {
        generator = GetComponentInParent<ChunkedTerrainGenerator>();
        deformer = GetComponent<MeshDeformer>();
        var meshFilter = GetComponent<MeshFilter>();

        bounds = meshFilter.mesh.bounds;
        bounds = TransformBounds(bounds, transform.localToWorldMatrix);
        
        deformRadius = generator.deformRadius;
    }

    public void DeformAtWorldPoint(Vector3 worldPos)
    {
        if (IntersectsCircle(bounds, worldPos, deformRadius))
        {
            deformer.DeformAtPoint(worldPos);
        }
    }

    /// Проверка: пересекается ли окружность с Bounds
    private bool IntersectsCircle(Bounds b, Vector3 center, float radius)
    {
        // Ограничиваемся проверкой по X и Z, игнорируем высоту
        Vector2 closestPoint = new Vector2(
            Mathf.Clamp(center.x, b.min.x, b.max.x),
            Mathf.Clamp(center.z, b.min.z, b.max.z)
        );

        float dist = Vector2.Distance(closestPoint, new Vector2(center.x, center.z));
        return dist < radius;
    }

    private Bounds TransformBounds(Bounds localBounds, Matrix4x4 localToWorld)
    {
        Vector3 center = localToWorld.MultiplyPoint3x4(localBounds.center);
        Vector3 extents = localBounds.extents;
        Vector3 axisX = localToWorld.MultiplyVector(Vector3.right) * extents.x;
        Vector3 axisY = localToWorld.MultiplyVector(Vector3.up) * extents.y;
        Vector3 axisZ = localToWorld.MultiplyVector(Vector3.forward) * extents.z;

        // Получаем мировые размеры
        Vector3 worldExtents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
        );

        return new Bounds(center, worldExtents * 2f); // Bounds от центра, size = extents*2
    }
}