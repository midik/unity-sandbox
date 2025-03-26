using TMPro;
using UnityEngine;

public class CarController : MonoBehaviour
{
    // public WheelCollider[] wheels; // Передать 4 коллайдера
    public float motorTorque = 1500f;
    public float steerAngle = 30f;
    
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI respawnText;
    public TextMeshProUGUI drivetrainText;
    
    public SmartFollowCamera followCamera;
    public AliveDetector aliveDetector;
    
    private WheelCollider FL;
    private WheelCollider FR;
    private WheelCollider RL;
    private WheelCollider RR;
    
    private Rigidbody rb;
    
    private Vector3 spawnPosition;
    private Vector3 spawnRotation;
    
    private InputSystem_Actions inputActions;
    private Vector2 moveInput;
    
    private enum DrivetrainMode { AWD, FWD, RWD }
    private DrivetrainMode drivetrainMode;

    private void Start()
    {
        FL = transform.Find("Wheel FL").GetComponent<WheelCollider>();
        FR = transform.Find("Wheel FR").GetComponent<WheelCollider>();
        RL = transform.Find("Wheel RL").GetComponent<WheelCollider>();
        RR = transform.Find("Wheel RR").GetComponent<WheelCollider>();
        
        rb = GetComponent<Rigidbody>();
        
        spawnPosition = transform.position;
        spawnRotation = transform.rotation.eulerAngles;
        respawnText.gameObject.SetActive(false);
        
        aliveDetector = GetComponent<AliveDetector>();
    }
    
    void Awake()
    {
        inputActions = new InputSystem_Actions();
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;
        inputActions.Player.Respawn.performed += ctx => Respawn();
        inputActions.Player.ToggleDrivetrainMode.performed += ctx =>
        {
            drivetrainMode++;
            if (drivetrainMode > DrivetrainMode.RWD) drivetrainMode = 0;
        };
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

    private void Drive(float steering, float motor)
    {
        FL.steerAngle = steering;
        FR.steerAngle = steering;
        
        if (drivetrainMode == DrivetrainMode.FWD || drivetrainMode == DrivetrainMode.AWD)
        {
            FL.motorTorque = motor;
            FR.motorTorque = motor;
        }
        if (drivetrainMode == DrivetrainMode.RWD || drivetrainMode == DrivetrainMode.AWD)
        {
            RL.motorTorque = motor;
            RR.motorTorque = motor;
        }
    }

    void Respawn()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        
        transform.position = spawnPosition + Vector3.up * 0.5f;
        transform.rotation = Quaternion.Euler(spawnRotation);

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