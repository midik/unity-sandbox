using UnityEngine;

public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider; // WheelCollider
    public Transform wheelMesh; // Визуал колеса
    public CapsuleCollider collisionCollider; // Физический коллайдер (Capsule)

    void LateUpdate()
    {
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);

        // Обновляем визуал
        wheelMesh.position = pos;
        wheelMesh.rotation = rot;

        // Обновляем Capsule Collider для коллизий
        collisionCollider.transform.position = pos;
        collisionCollider.transform.rotation = rot;
    }
}