using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target; // Шарик
    public Vector3 offset = new Vector3(0, 2, -5); // Смещение камеры от шара
    public float smoothSpeed = 0.125f;
    public float minHeightAboveTarget = 2.0f; // Минимальная высота над шариком
    public float velocityThreshold = 0.2f;
    public BallController ballController;

    private Vector3 defaultDirection = new Vector3(0, 2, -5);
    private Vector3 currentDirection;
    private Rigidbody targetRb; // Для доступа к скорости шарика


    void Start()
    {
        currentDirection = defaultDirection;
        targetRb = target.GetComponent<Rigidbody>(); // Получаем Rigidbody шара
    }

    void LateUpdate()
    {
        Vector3 velocity = targetRb.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0, velocity.z);

        Vector3 targetDirection = currentDirection; // по умолчанию старое направление

        if (horizontalVelocity.magnitude > 0.5f && !ballController.isInputActive) // не реагировать сразу
        {
            targetDirection = horizontalVelocity.normalized;
        }

        // Плавная интерполяция направления
        currentDirection = Vector3.Lerp(currentDirection, targetDirection, Time.deltaTime * 3f);

        // Камера относительно шара
        Vector3 desiredPosition = target.position + currentDirection * offset.z + Vector3.up * offset.y;

        // Минимальная высота (если хочешь)
        if (desiredPosition.y < target.position.y + minHeightAboveTarget)
        {
            desiredPosition.y = target.position.y + minHeightAboveTarget;
        }

        // Плавное движение
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.LookAt(target);
    }
}