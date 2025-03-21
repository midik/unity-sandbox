using UnityEngine;

public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider; // WheelCollider
    public Transform wheelMesh; // Визуал колеса
    public CapsuleCollider capsuleCollider; // Физический коллайдер (Capsule)
    
    // public TerrainDeformer terrainDeformer;
    // public LayerMask groundLayer;
    
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

    // void FixedUpdate()
    // {
    //     RaycastHit hit;
    //     if (Physics.Raycast(wheelMesh.position, Vector3.down, out hit, 5f, groundLayer))
    //     {
    //         Vector3 wheelForward = wheelMesh.forward;  // Направление оси колеса
    //         float direction = Mathf.Sign(wheelCollider.rpm); // 1 = вперед, -1 = назад
    //         float wheelRadius = wheelCollider.radius;
    //
    //         // Смещаем точку в сторону движения колеса
    //         Vector3 adjustedPoint = hit.point + (wheelForward * (direction * wheelRadius * 0.3f));
    //
    //         // Передаем в деформер уже скорректированную точку
    //         terrainDeformer.DeformAtPoint(adjustedPoint, wheelCollider.rpm, wheelCollider.attachedRigidbody.mass);
    //     }
    // }
    
    // void FixedUpdate()
    // {
    //     RaycastHit hit;
    //     if (Physics.Raycast(wheelMesh.position, Vector3.down, out hit, 5f))
    //     {
    //         terrain.GetComponent<MeshDeformer>().DeformAtPoint(hit.point);
    //     }
    // }


}