using TMPro;
using UnityEngine;

public class CarController : Driveable
{
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI respawnText;
    public TextMeshProUGUI drivetrainText;

    public SmartFollowCamera followCamera;
    public AliveDetector aliveDetector;

    private InputSystem_Actions inputActions;
    private Vector2 moveInput;

    protected override void Start()
    {
        base.Start();
        
        InitializeWheels();
        rb = GetComponent<Rigidbody>();

        spawnPosition = transform.position;
        spawnRotation = transform.rotation.eulerAngles;
        respawnText.gameObject.SetActive(false);

        aliveDetector = GetComponent<AliveDetector>();
    }

    protected override void Awake()
    {
        base.Awake();
        
        inputActions = new InputSystem_Actions();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;
        inputActions.Player.Respawn.performed += ctx => Respawn();
        inputActions.Player.ToggleDrivetrainMode.performed += ctx => ToggleDrivetrain();
    }

    void OnEnable() => inputActions.Enable();
    void OnDisable() => inputActions.Disable();

    void FixedUpdate()
    {
        if (aliveDetector.isDead)
        {
            speedText.text = "O_o";
            respawnText.gameObject.SetActive(true);
            Drive(0f, 0f);
            return;
        }

        float motor = motorTorque * moveInput.y;
        float steering = steerAngle * moveInput.x;
        Drive(steering, motor);

        UpdateHud();
    }

    protected override void OnRespawned()
    {
        aliveDetector.Recover();
        followCamera.ResetToTarget();
        respawnText.gameObject.SetActive(false);
    }

    void UpdateHud()
    {
        if (aliveDetector.isDead) return;

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float horizontalSpeed = horizontalVelocity.magnitude;
        speedText.text = horizontalSpeed.ToString("F1");
        drivetrainText.text = drivetrainMode.ToString();
    }
}
