using UnityEngine;

public class FollowCamera : MonoBehaviour
{
    public Transform target; // Шарик
    public Vector3 offset = new Vector3(0, 2, -5); // Смещение камеры от шара
    public float smoothSpeed = 0.125f;

    private Rigidbody targetRb; // Для доступа к скорости шарика

    void Start()
    {
        targetRb = target.GetComponent<Rigidbody>(); // Получаем Rigidbody шара
    }

    void LateUpdate()
    {
        // Получаем направление движения шарика
        Vector3 velocity = targetRb.linearVelocity;

        // Если шарик почти не двигается — не крутим камеру
        if (velocity.magnitude > 0.1f)
        {
            // Нормализуем направление
            Vector3 direction = velocity.normalized;

            // Позиция камеры: позади шарика по его направлению
            // Vector3 desiredPosition = target.position - direction * offset.z + Vector3.up * offset.y;
            Vector3 desiredPosition = target.position + direction * offset.z + Vector3.up * offset.y;


            // Плавное перемещение
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
            transform.position = smoothedPosition;

            // Смотрим на шарик
            transform.LookAt(target);
        }
    }
}