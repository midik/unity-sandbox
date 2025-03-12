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
        Vector3 targetDirection = currentDirection; // по умолчанию старое

        if (!ballController.isInputActive && velocity.magnitude > velocityThreshold)
        {
            targetDirection = velocity.normalized; // Обновляем направление, если нет активного ввода
        }


        // Плавная интерполяция (чтобы не дергалось)
        currentDirection = Vector3.Lerp(currentDirection, targetDirection, Time.deltaTime * 5f); // 5f - "скорость поворота"
        
        Vector3 desiredPositionXZ = target.position + currentDirection * offset.z;
        Vector3 desiredPosition = new Vector3(
            desiredPositionXZ.x,
            Mathf.Lerp(transform.position.y, target.position.y + offset.y, Time.deltaTime * 2f), // вертикаль плавно
            desiredPositionXZ.z
        );

        // Проверяем высоту, чтобы не опускалась ниже
        if (desiredPosition.y < target.position.y + minHeightAboveTarget)
        {
            desiredPosition.y = target.position.y + minHeightAboveTarget;
        }

        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;

        transform.LookAt(target);
    }

}