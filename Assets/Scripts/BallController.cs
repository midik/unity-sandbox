using TMPro;
using UnityEngine;

public class BallController : MonoBehaviour
{
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI altitudeText;
    public TextMeshProUGUI respawnText;

    public FollowCamera followCamera;

    public float jumpForce = 5f;
    public Transform cam;
    public float powerForce = 5f;
    public float handbrakeForce = 5f;

    public LayerMask groundLayer;

    private Rigidbody rb;
    private Vector2 moveInput;
    private bool handbrakeActive;

    private float groundCheckDistance = 3f;
    private bool isGrounded;

    private Vector3 spawnPosition;

    public bool isFallen { get; private set; } = false;
    private float airTime = 0f;
    private float maxAirTime = 3f;
    private float absoluteFallThreshold = -200f;

    private InputSystem_Actions inputActions;

    void Awake()
    {
        inputActions = new InputSystem_Actions();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        inputActions.Player.Handbrake.performed += ctx => handbrakeActive = true;
        inputActions.Player.Handbrake.canceled += ctx => handbrakeActive = false;

        inputActions.Player.Jump.performed += ctx => Jump();
        inputActions.Player.Respawn.performed += ctx => Respawn();
    }

    void OnEnable() => inputActions.Enable();
    void OnDisable() => inputActions.Disable();

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        spawnPosition = transform.position;
        respawnText.gameObject.SetActive(false);
    }

    void FixedUpdate()
    {
        // Проверка на землю
        isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);

        // Подсчет времени в воздухе
        if (!isGrounded) airTime += Time.fixedDeltaTime;
        else airTime = 0f;

        // Проверка на падение
        if (airTime > maxAirTime || transform.position.y < absoluteFallThreshold)
        {
            if (!isFallen)
            {
                isFallen = true;
                speedText.text = "O_o";
                respawnText.gameObject.SetActive(true);
            }
        }

        // Управление движением только если не упал
        if (!isFallen)
        {
            Vector3 camForward = cam.forward;
            camForward.y = 0;
            camForward.Normalize();

            Vector3 camRight = cam.right;
            camRight.y = 0;
            camRight.Normalize();

            Vector3 inputDirection = camForward * moveInput.y + camRight * moveInput.x;

            Vector3 currentVelocity = rb.linearVelocity;
            float verticalVelocity = currentVelocity.y; // Сохраняем влияние гравитации
            Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0, currentVelocity.z);
            float speedMagnitude = horizontalVelocity.magnitude;

            // 1. Ускорение
            if (isGrounded && inputDirection.magnitude > 0.1f)
            {
                rb.AddForce(inputDirection.normalized * powerForce, ForceMode.Force);
            }

            // 2. Коррекция направления без потери Y
            if (isGrounded && speedMagnitude > 0.1f && inputDirection.magnitude > 0.1f)
            {
                Vector3 targetHorizontalVelocity = inputDirection.normalized * speedMagnitude;
                Vector3 smoothedVelocity = Vector3.Lerp(horizontalVelocity, targetHorizontalVelocity, Time.fixedDeltaTime * 3f);
                rb.linearVelocity = new Vector3(smoothedVelocity.x, verticalVelocity, smoothedVelocity.z);
            }

            // 3. Ручник (торможение только по XZ)
            if (isGrounded && handbrakeActive && horizontalVelocity.magnitude > 0.1f)
            {
                Vector3 slowedHorizontal = Vector3.Lerp(horizontalVelocity, Vector3.zero, Time.fixedDeltaTime * handbrakeForce);
                rb.linearVelocity = new Vector3(slowedHorizontal.x, verticalVelocity, slowedHorizontal.z);
            }
        }

        // 4. Обновление HUD
        UpdateHud();
    }

    void Jump()
    {
        if (!isGrounded) return;
        rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
    }

    void Respawn()
    {
        transform.position = spawnPosition;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        respawnText.gameObject.SetActive(false);
        isFallen = false;
        airTime = 0f;
        followCamera.ResetToTarget();
    }

    void UpdateHud()
    {
        if (isFallen) return;

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float horizontalSpeed = horizontalVelocity.magnitude;
        speedText.text = horizontalSpeed.ToString("F1") + " m/s";

        float altitude = transform.position.y;
        altitudeText.text = altitude.ToString("F1") + " m";
    }
}
