using UnityEngine;

public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider; // WheelCollider
    public Transform wheelMesh; // Визуал колеса
    public CapsuleCollider capsuleCollider; // Физический коллайдер (Capsule)
    
    public TerrainDeformer terrainDeformer;
    public LayerMask groundLayer;
    

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
        if (Physics.Raycast(wheelMesh.position, Vector3.down, out hit, 5f, groundLayer))
        {
            if (wheelCollider.rpm != 0) {
                terrainDeformer.DeformAtPoint(hit.point);
            }
        }
    }
}