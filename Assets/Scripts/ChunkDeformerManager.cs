using UnityEngine;

public class ChunkDeformerManager : MonoBehaviour
{
    private MeshDeformer deformer;
    private Bounds bounds;

    
    private void Start()
    {
        deformer = GetComponent<MeshDeformer>();
        MeshFilter componentInChildren = deformer.GetComponentInChildren<MeshFilter>();
        bounds = componentInChildren.mesh.bounds;
        bounds.center = deformer.transform.position;
    }

    public void DeformAtWorldPoint(Vector3 worldPos)
    {
        if (bounds.Contains(worldPos))
        {
            deformer.DeformAtPoint(worldPos);
        }
    }
}