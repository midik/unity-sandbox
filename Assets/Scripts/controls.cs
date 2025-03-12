using UnityEngine;

public class BallController : MonoBehaviour
{
    public float speed = 5.0f;
    private Rigidbody rb;
    public Transform cam; // Сюда привяжем Main Camera

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // Считываем ввод
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");

        // Получаем направления камеры
        Vector3 camForward = cam.forward;
        Vector3 camRight = cam.right;

        // Убираем наклон по Y, чтобы не двигался вверх/вниз
        camForward.y = 0;
        camRight.y = 0;

        // Нормализуем (на всякий случай)
        camForward.Normalize();
        camRight.Normalize();

        // Направление движения: вперёд/назад + влево/вправо относительно камеры
        Vector3 movement = camForward * moveVertical + camRight * moveHorizontal;

        // Добавляем силу к шарику
        rb.AddForce(movement * speed);
    }
}