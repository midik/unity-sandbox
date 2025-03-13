using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target;
    public Vector3 offset = new Vector3(0, 3, 8); // Смещение камеры (над и позади шара)
    public BallController ballController;
    public float smoothSpeed = 8f; // Плавность движения камеры
    public float lookSmoothSpeed = 5f; // Плавность поворота камеры (LookAt)
    public float minHeightAboveTarget = 1.5f; // Минимальная высота над шаром
    public LayerMask groundLayer; // Слой земли для Raycast

    private Rigidbody targetRb;
    private Vector3 currentVelocity; // Для сглаживания позиции
    private Vector3 lastMoveDirection = Vector3.back; // Последнее направление движения
    private Vector3 currentLookTarget; // Для сглаживания взгляда

    void Start()
    {
        targetRb = target.GetComponent<Rigidbody>();

        // Начальная позиция камеры
        Vector3 startPosition = target.position + offset;
        transform.position = startPosition;
        currentLookTarget = target.position + Vector3.up * 1f; // Смотрим немного выше шара
    }

    void LateUpdate()
    {
        if (ballController.isFallen) return;

        // --- Определяем направление "позади шара" ---
        Vector3 velocity = targetRb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);

        if (horizontalVelocity.magnitude > 0.1f)
        {
            lastMoveDirection = horizontalVelocity.normalized;
        }

        Vector3 directionBehind = lastMoveDirection;

        // --- Рассчитываем желаемую позицию камеры ---
        Vector3 desiredPosition = target.position + Vector3.up * offset.y + directionBehind * offset.z;

        // --- Проверка Raycast от шара назад (к предполагаемой позиции камеры) ---
        Vector3 rayDirection = (desiredPosition - target.position).normalized;
        float rayDistance = Vector3.Distance(target.position, desiredPosition);

        if (Physics.Raycast(target.position, rayDirection, out RaycastHit hit, rayDistance, groundLayer))
        {
            // Если гора мешает — ставим перед горой + чуть выше
            desiredPosition = hit.point + Vector3.up * minHeightAboveTarget;
        }

        // --- Минимальная высота над шаром ---
        if (desiredPosition.y < target.position.y + minHeightAboveTarget)
            desiredPosition.y = target.position.y + minHeightAboveTarget;

        // --- Плавное перемещение камеры ---
        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, 1f / smoothSpeed);

        // --- Плавное сглаживание взгляда ---
        Vector3 targetLookPosition = target.position + Vector3.up * 1f; // Чуть выше центра шара
        currentLookTarget = Vector3.Lerp(currentLookTarget, targetLookPosition, Time.deltaTime * lookSmoothSpeed);
        transform.LookAt(currentLookTarget);
    }

    // Сброс камеры при респавне
    public void ResetToTarget()
    {
        lastMoveDirection = Vector3.back; // По умолчанию сзади
        Vector3 resetPosition = target.position + offset;
        transform.position = resetPosition;
        currentLookTarget = target.position + Vector3.up * 1f;
        transform.LookAt(currentLookTarget);
    }
}
