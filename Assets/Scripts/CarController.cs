using TMPro;
using UnityEngine;

public class CarController : MonoBehaviour
{
    public WheelCollider[] wheels; // Передать 4 коллайдера
    public float motorTorque = 1500f;
    public float steerAngle = 30f;
    
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI altitudeText;
    public TextMeshProUGUI respawnText;
    
    public Rigidbody rb;
    
    public bool isFallen { get; private set; } = false;
    
    private WheelCollider FL;
    private WheelCollider FR;
    private WheelCollider RL;
    private WheelCollider RR;
    
    private void Start()
    {
        FL = wheels[0];
        FR = wheels[1];
        RL = wheels[2];
        RR = wheels[3];
    }

    void FixedUpdate()
    {
        float motor = motorTorque * Input.GetAxis("Vertical");
        float steering = steerAngle * Input.GetAxis("Horizontal");
        
        FL.steerAngle = steering;
        FR.steerAngle = steering;
        
        FL.motorTorque = motor;
        RL.motorTorque = motor;
        RL.motorTorque = motor;
        RR.motorTorque = motor;
        
        UpdateHud();
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