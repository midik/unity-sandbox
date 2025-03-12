using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target; // Шарик
    public Vector3 offset = new Vector3(0, 2, 5); // Смещение камеры (z положительный!)

    public BallController ballController;
    public float smoothSpeed = 2f; // Плавность движения камеры
    public float minHeightAboveTarget = 1f; // Минимальная высота над шариком
    public float horizontalVelocityThreshold = 0.5f; // Минимальная скорость для обновления направления
    public float rotationSpeed = 5f; // Скорость поворота камеры

    private Vector3 currentDirection;
    private Rigidbody targetRb; // Для доступа к скорости шарика

    void Start()
    {
        targetRb = target.GetComponent<Rigidbody>();

        // Камера изначально за спиной шара
        currentDirection = Vector3.back;
        Vector3 startPosition = target.position - currentDirection * offset.z + Vector3.up * offset.y;
        transform.position = startPosition;
        transform.LookAt(target);
    }

    void LateUpdate()
    {
        if (ballController.isFallen) return;
        
        Vector3 velocity = targetRb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);

        // Если шар достаточно быстро катится — пересчитываем направление
        if (horizontalVelocity.magnitude > horizontalVelocityThreshold)
        {
            Vector3 movingDirection = horizontalVelocity.normalized;
            Vector3 targetDirection = -movingDirection; // Камера позади шара

            // Плавно выравниваем currentDirection на targetDirection
            currentDirection = Vector3.Slerp(currentDirection, targetDirection, Time.deltaTime * rotationSpeed).normalized;
        }

        // Вычисляем позицию камеры
        Vector3 desiredPosition = target.position - currentDirection * offset.z + Vector3.up * offset.y;

        // Минимальная высота над шаром
        if (desiredPosition.y < target.position.y + minHeightAboveTarget)
            desiredPosition.y = target.position.y + minHeightAboveTarget;

        // Плавное движение камеры
        transform.position = Vector3.Lerp(transform.position, desiredPosition, Time.deltaTime * smoothSpeed);
        transform.LookAt(target);
    }
    
    public void ResetToTarget()
    {
        Vector3 resetPosition = target.position - currentDirection * offset.z + Vector3.up * offset.y;
        transform.position = resetPosition;
        transform.LookAt(target);
    }

}
