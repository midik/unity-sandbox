using TMPro;
using UnityEngine;

public class BallController : MonoBehaviour
{
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI respawnText;
    
    public FollowCamera followCamera;
    
    public float jumpForce = 5f;
    public Transform cam;
    public float handbrakeForce = 5f;
    
    public LayerMask groundLayer;
    
    private Rigidbody rb;
    private Vector2 moveInput;
    private bool handbrakeActive;
    
    private float groundCheckDistance = 0.6f;
    private bool isGrounded;
    
    private Vector3 spawnPosition;
    
    public bool isFallen { get; private set; } = false; // Флаг, что шар упал
    private float airTime = 0f; // Время в воздухе
    private float maxAirTime = 3f; // Сколько можно быть в воздухе перед тем, как считать падением
    private float absoluteFallThreshold = -200f; // Резервная "абсолютная граница" карты (если вдруг шарик улетел в бездну)
    
    private InputSystem_Actions inputActions;
    
    void Awake()
    {
        inputActions = new InputSystem_Actions();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        inputActions.Player.Handbrake.performed += ctx => handbrakeActive = true;
        inputActions.Player.Handbrake.canceled += ctx => handbrakeActive = false;

        inputActions.Player.Jump.performed += ctx => Jump();
        inputActions.Player.Respawn.performed += ctx => Respawn(); // Новая кнопка Respawn
    }

    void OnEnable() => inputActions.Enable();
    void OnDisable() => inputActions.Disable();

    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        spawnPosition = transform.position; // Запоминаем начальную позицию
        respawnText.gameObject.SetActive(false); // Скрываем надпись
    }

    void FixedUpdate()
    {
        // Чистая проверка через луч вниз
        isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
        
        // Подсчет времени в воздухе
        if (!isGrounded) {
            airTime += Time.fixedDeltaTime;
        } else {
            airTime = 0f; // Сброс, если коснулись земли
        }

        // Если в воздухе слишком долго или улетел вниз
        if (airTime > maxAirTime || transform.position.y < absoluteFallThreshold)
        {
            if (!isFallen) // Чтобы не повторялось каждый кадр
            {
                isFallen = true;
                speedText.text = "O_o";
                respawnText.gameObject.SetActive(true);
            }
        }

        if (!isFallen)
        {
            Vector3 camForward = cam.forward;
            camForward.y = 0;
            camForward.Normalize();

            Vector3 camRight = cam.right;
            camRight.y = 0;
            camRight.Normalize();

            // Направление от игрока (вектор управления)
            Vector3 inputDirection = camForward * moveInput.y + camRight * moveInput.x;

            // 1. Если есть ввод — добавляем силу (ускорение)
            if (isGrounded && inputDirection.magnitude > 0.1f)
            {
                rb.AddForce(inputDirection.normalized * 3f, ForceMode.Force); // 3f - сила ускорения
            }

            // 2. Коррекция направления скорости, если шарик едет
            Vector3 currentVelocity = rb.linearVelocity;
            float speedMagnitude = currentVelocity.magnitude;

            if (isGrounded && speedMagnitude > 0.1f && inputDirection.magnitude > 0.1f)
            {
                Vector3 newVelocity = inputDirection.normalized * speedMagnitude;
                rb.linearVelocity = Vector3.Lerp(currentVelocity, newVelocity, Time.fixedDeltaTime * 3f);
            }

            // 3. Ручник
            if (isGrounded && handbrakeActive && rb.linearVelocity.magnitude > 0f)
            {
                rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * handbrakeForce); // быстрое торможение
            }
        }

        // 4. Показания скорости
        UpdateSpeedometer();    
    }
    
    void Jump()
    {
        if (isGrounded)
        {
            // isGrounded = false;
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
        }
    }
    
    void Respawn()
    {
        transform.position = spawnPosition;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        respawnText.gameObject.SetActive(false); // Убираем надпись
        isFallen = false; // Снимаем флаг падения
        airTime = 0f; // Сброс таймера
        
        // Сброс камеры
        followCamera.ResetToTarget();
    }

    void UpdateSpeedometer()
    {
        if (!isFallen)
        {
            Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
            float horizontalSpeed = horizontalVelocity.magnitude;
            speedText.text = horizontalSpeed.ToString("F2") + " m/s";
        }
    }
}