using UnityEngine;

public class CarFollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 3, -8); // Высота + позади
    public float smoothSpeed = 5f;
    public float lookSmoothSpeed = 5f;
    public float minHeightAboveTarget = 1.5f;
    public LayerMask groundLayer;

    private Vector3 currentVelocity;
    private Vector3 currentLookTarget;

    void Start()
    {
        // Берем направление машины, а не forward по умолчанию
        Vector3 startDirection = -target.forward;
        Vector3 startPosition = target.position + startDirection * Mathf.Abs(offset.z) + Vector3.up * offset.y;
        transform.position = startPosition;
        currentLookTarget = target.position + Vector3.up * 1f;
    }

    void LateUpdate()
    {
        Vector3 directionBehind = -target.forward; // Всегда позади машины

        Vector3 desiredPosition = target.position + directionBehind * Mathf.Abs(offset.z) + Vector3.up * offset.y;

        // Raycast от машины назад
        Vector3 rayDirection = (desiredPosition - target.position).normalized;
        float rayDistance = Vector3.Distance(target.position, desiredPosition);

        if (Physics.Raycast(target.position, rayDirection, out RaycastHit hit, rayDistance, groundLayer))
        {
            desiredPosition = hit.point + Vector3.up * minHeightAboveTarget;
        }

        // Минимальная высота
        if (desiredPosition.y < target.position.y + minHeightAboveTarget)
            desiredPosition.y = target.position.y + minHeightAboveTarget;

        // Плавное движение
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, 1f / smoothSpeed);

        // Плавный взгляд
        Vector3 targetLookPosition = target.position + Vector3.up * 1f;
        currentLookTarget = Vector3.Lerp(currentLookTarget, targetLookPosition, Time.deltaTime * lookSmoothSpeed);
        transform.LookAt(currentLookTarget);
    }
}