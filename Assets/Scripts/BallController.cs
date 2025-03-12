using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class BallController : MonoBehaviour
{
    public TextMeshProUGUI speedText;
    public float jumpForce = 0.3f;
    public Transform cam;
    public float handbrakeForce = 0.2f;
    public bool isInputActive => moveInput.magnitude > 0.1f;

    private Rigidbody rb;
    private bool isGrounded;
    private Vector2 moveInput;
    private bool handbrakeActive;
    
    private InputSystem_Actions inputActions;
    
    void Awake()
    {
        inputActions = new InputSystem_Actions();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        inputActions.Player.Handbrake.performed += ctx => handbrakeActive = true;
        inputActions.Player.Handbrake.canceled += ctx => handbrakeActive = false;

        inputActions.Player.Jump.performed += ctx => Jump();
    }

    void OnEnable() => inputActions.Enable();
    void OnDisable() => inputActions.Disable();

    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
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
        if (inputDirection.magnitude > 0.1f)
        {
            rb.AddForce(inputDirection.normalized * 3f, ForceMode.Force); // 3f - сила ускорения
        }

        // 2. Коррекция направления скорости, если шарик едет
        Vector3 currentVelocity = rb.linearVelocity;
        float speedMagnitude = currentVelocity.magnitude;

        if (speedMagnitude > 0.1f && inputDirection.magnitude > 0.1f && isGrounded)
        {
            Vector3 newVelocity = inputDirection.normalized * speedMagnitude;
            rb.linearVelocity = Vector3.Lerp(currentVelocity, newVelocity, Time.fixedDeltaTime * 3f);
        }

        // 3. Ручник
        if (handbrakeActive && rb.linearVelocity.magnitude > 0f)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 5f); // быстрое торможение
        }

        // 4. Показания скорости
        UpdateSpeedometer();    
    }
    
    void Jump()
    {
        if (isGrounded)
        {
            rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            isGrounded = false;
        }
    }

    // Проверяем, на земле ли шарик (OnCollisionStay, чтобы не пропускать моменты)
    private void OnCollisionStay(Collision collision)
    {
        isGrounded = true;
    }

    void UpdateSpeedometer()
    {
        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float horizontalSpeed = horizontalVelocity.magnitude;
        speedText.text = horizontalSpeed.ToString("F2") + " m/s";
    }
}