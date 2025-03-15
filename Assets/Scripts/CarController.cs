using UnityEngine;

public class CarController : MonoBehaviour
{
    public WheelCollider[] wheels; // Передать 4 коллайдера
    public float motorTorque = 1500f;
    public float steerAngle = 30f;
    
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
        
        // 2WD
        RL.motorTorque = motor;
        RR.motorTorque = motor;
    }
}