using TMPro;
using UnityEngine;

public class CarController : MonoBehaviour
{
    public WheelCollider[] wheels; // Передать 4 коллайдера
    public float motorTorque = 1500f;
    public float steerAngle = 30f;
    
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI respawnText;
    
    public Rigidbody rb;
    public SmartFollowCamera followCamera;
    public LayerMask groundLayer;
    public AliveDetector aliveDetector;
    
    private WheelCollider FL;
    private WheelCollider FR;
    private WheelCollider RL;
    private WheelCollider RR;
    
    private Vector3 spawnPosition;
    private Vector3 spawnRotation;
    
    private InputSystem_Actions inputActions;
    private Vector2 moveInput;

    private void Start()
    {
        FL = wheels[0];
        FR = wheels[1];
        RL = wheels[2];
        RR = wheels[3];
        
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
        FL.motorTorque = motor;
        RL.motorTorque = motor;
        RL.motorTorque = motor;
        RR.motorTorque = motor;
    }

    void Respawn()
    {
        transform.position = spawnPosition + Vector3.up * 0.5f;
        transform.rotation = Quaternion.Euler(spawnRotation);
        
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Drive(0f, 0f);

        aliveDetector.recover();

        followCamera.ResetToTarget();

        respawnText.gameObject.SetActive(false);
        // ResetWheelColliders();
    }
    
    void UpdateHud()
    {
        if (aliveDetector.isDead) return;

        Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
        float horizontalSpeed = horizontalVelocity.magnitude;
        speedText.text = horizontalSpeed.ToString("F1") + " m/s";
    }
}