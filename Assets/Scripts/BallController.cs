using TMPro;
using UnityEngine;

public class BallController : MonoBehaviour
{
    public TextMeshProUGUI speedText; // UI элемент для скорости
    
    public float speed = 0.5f;
    public float jumpForce = 0.3f;
    // public float drag = 0.98f;
    public bool isInputActive = false;
    public Transform cam;
    public float handbrakeForce = 0.2f;

    private Rigidbody rb;
    private bool isGrounded; // Чтобы прыгать только по земле
    
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // Считываем ввод
        float moveHorizontal = Input.GetAxis("Horizontal");
        float moveVertical = Input.GetAxis("Vertical");
        bool handBrake = Input.GetButton("Handbrake");

        isInputActive = Mathf.Abs(moveHorizontal) > 0.1f || Mathf.Abs(moveVertical) > 0.1f;


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

        // Ручник
        if (handBrake && rb.linearVelocity.magnitude > 0f)
        {
            rb.AddForce(movement * handbrakeForce);
        }

        // Добавляем силу к шарику
        // rb.AddForce(movement * speed);
        // rb.linearVelocity *= drag;
        rb.AddForce(movement * speed, ForceMode.Impulse);
        
        // Прыжок (FixedUpdate для физики)
        if (Input.GetKey(KeyCode.Space) && isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false; // Чтобы не прыгал в воздухе
        }
        
        // Обновляем скорость на UI
        float speedValue = rb.linearVelocity.magnitude;
        speedText.text = "Speed: " + speedValue.ToString("F2");
    }

    // Проверяем, на земле ли шарик (OnCollisionStay, чтобы не пропускать моменты)
    private void OnCollisionStay(Collision collision)
    {
        isGrounded = true;
    }
}