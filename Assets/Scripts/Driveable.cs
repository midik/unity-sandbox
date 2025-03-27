using UnityEngine;

public abstract class Driveable : Respawnable
{
    public float motorTorque = 1500f;
    public float steerAngle = 30f;

    protected WheelCollider FL, FR, RL, RR;

    protected enum DrivetrainMode { AWD, FWD, RWD }
    protected DrivetrainMode drivetrainMode;

    protected override void Start()
    {
        InitializeWheels();
        InitializeSpawnData();
    }

    protected void InitializeWheels()
    {
        FL = transform.Find("Wheel FL").GetComponent<WheelCollider>();
        FR = transform.Find("Wheel FR").GetComponent<WheelCollider>();
        RL = transform.Find("Wheel RL").GetComponent<WheelCollider>();
        RR = transform.Find("Wheel RR").GetComponent<WheelCollider>();
    }

    private void InitializeSpawnData()
    {
        spawnPosition = transform.position;
        spawnRotation = transform.rotation.eulerAngles;
    }

    protected void Drive(float steering, float motor)
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

    protected void StopDriving()
    {
        Drive(0f, 0f);
    }

    protected void ToggleDrivetrain()
    {
        drivetrainMode++;
        if (drivetrainMode > DrivetrainMode.RWD) drivetrainMode = 0;
    }
}