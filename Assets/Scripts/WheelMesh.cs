using UnityEngine;

public class WheelVisualizer : MonoBehaviour
{
    public WheelCollider wheelCollider;
    public Transform wheelMesh;

    void Update()
    {
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        wheelMesh.position = pos;
        wheelMesh.rotation = rot;
    }
}