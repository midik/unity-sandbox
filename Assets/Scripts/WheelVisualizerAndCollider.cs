using UnityEngine;

public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider; // WheelCollider
    public Transform wheelMesh; // Визуал колеса
    public CapsuleCollider capsuleCollider; // Физический коллайдер (Capsule)
    
    private ChunkDeformerManager chunkDeformerManager;
    
    void Start()
    {
        chunkDeformerManager = GetComponent<ChunkDeformerManager>();
    }
   
    void LateUpdate()
    {
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);

        // Обновляем визуал
        wheelMesh.position = pos;
        wheelMesh.rotation = rot;

        // Обновляем collider для коллизий
        capsuleCollider.transform.position = pos;
        capsuleCollider.transform.rotation = rot;
    }

    void FixedUpdate()
    {
        RaycastHit hit;
        if (Physics.Raycast(wheelMesh.position, Vector3.down, out hit, 5f))
        {
            if (chunkDeformerManager)
            {
                chunkDeformerManager.DeformAtWorldPoint(hit.point);
            }
        }
    }
}