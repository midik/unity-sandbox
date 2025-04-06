using System.Collections.Generic;
using UnityEngine;

// Helper attribute for ReadOnly fields in Inspector
public class ReadOnlyAttribute : PropertyAttribute
{
}

#if UNITY_EDITOR
[UnityEditor.CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
public class ReadOnlyDrawer : UnityEditor.PropertyDrawer
{
    public override void OnGUI(Rect position, UnityEditor.SerializedProperty property, GUIContent label)
    {
        GUI.enabled = false;
        UnityEditor.EditorGUI.PropertyField(position, property, label, true);
        GUI.enabled = true;
    }

    public override float GetPropertyHeight(UnityEditor.SerializedProperty property, GUIContent label)
    {
        return UnityEditor.EditorGUI.GetPropertyHeight(property, label, true);
    }
}
#endif

public abstract class Driveable : Respawnable
{
    [Header("Handling")] public float steerAngle = 30f;

    [Header("Powertrain Components")]
    public Engine engine;
    public Gearbox gearbox;
    public Clutch clutch;

    [Header("Braking")] public float maxBrakeTorque = 5000f;

    [Header("Drivetrain")] [SerializeField] // Keep protected but allow viewing in inspector
    protected DrivetrainMode drivetrainMode = DrivetrainMode.AWD;

    public enum DrivetrainMode
    {
        AWD,
        FWD,
        RWD
    }

    [Header("Readouts (Read Only)")]
    [SerializeField, ReadOnly] protected float currentSpeedKmh;
    [SerializeField, ReadOnly] protected float engineRPM;
    [SerializeField, ReadOnly] protected float externalRPM;
    [SerializeField, ReadOnly] protected float drivenWheelRPM;
    [SerializeField, ReadOnly] protected float[] drivenWheelsRPM;
    [SerializeField, ReadOnly] protected int currentGear;
    [SerializeField, ReadOnly] protected float currentEngineTorque;
    [SerializeField, ReadOnly] protected float currentWheelTorque; // Torque *after* gearbox/diff

    // --- Internal State ---
    protected float throttleInput; // 0..1
    protected float brakeInput; // 0..1
    protected float steeringInput; // -1..1

    private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR order assumed
    private const int rpmSmoothingWindow = 30;
    private Queue<float>[] rpmHistory;


    protected override void Awake() // Use Awake for component references
    {
        FindWheelColliders(); // Find colliders if not assigned
    }

    protected override void Start()
    {
        base.Start();

        engine.Initialize();
        gearbox.Initialize();

        clutch = new Clutch();
        clutch.Initialize(engine.idleRPM);

        currentSpeedKmh = 0f;
        engineRPM = engine.idleRPM;
        externalRPM = 0f;
        drivenWheelRPM = 0f;
        drivenWheelsRPM = new float[4]; // Assuming 4 wheels
        currentGear = gearbox.CurrentGearIndex;
        currentEngineTorque = 0f;
        currentWheelTorque = 0f;

        rpmHistory = new Queue<float>[wheelColliders.Length];
        for (int i = 0; i < wheelColliders.Length; i++)
        {
            rpmHistory[i] = new Queue<float>();
        }
    }

    // Helper to find wheels if not manually assigned
    private void FindWheelColliders()
    {
        if (wheelColliders is not { Length: 4 } || !wheelColliders[0])
        {
            Debug.Log("Attempting to find WheelColliders automatically (FL, FR, RL, RR)...");
            wheelColliders = new WheelCollider[4];
            wheelColliders[0] = transform.Find("Wheel FL")?.GetComponent<WheelCollider>();
            wheelColliders[1] = transform.Find("Wheel FR")?.GetComponent<WheelCollider>();
            wheelColliders[2] = transform.Find("Wheel RL")?.GetComponent<WheelCollider>();
            wheelColliders[3] = transform.Find("Wheel RR")?.GetComponent<WheelCollider>();

            if (!wheelColliders[0] || !wheelColliders[1] || !wheelColliders[2] || !wheelColliders[3])
            {
                Debug.LogError("Could not find all WheelColliders automatically! Assign them in the Inspector.", this);
            }
        }

        if (!rb)
        {
            InitializeRigidBody(); // Вызываем метод из Respawnable
        }

        if (!rb)
        {
            Debug.LogError("Rigidbody not found on Driveable object!", this);
        }
    }


    protected void UpdatePowertrain(float throttle, float brake, float steering, float moveY)
    {
        // Store inputs
        throttleInput = Mathf.Clamp01(throttle);
        brakeInput = Mathf.Clamp01(brake);
        steeringInput = Mathf.Clamp(steering, -1f, 1f);

        currentSpeedKmh = rb.linearVelocity.magnitude * 3.6f; // Convert m/s to km/h

        if (engine.isRunning)
        {
        }

        // Calculate current speed & wheel RPM
        drivenWheelRPM = CalculateDrivenWheelRPM();

        bool wasShifted = false; // Флаг для отслеживания переключения

        clutch.UpdateClutchFactor(engine.CurrentRPM, gearbox.CurrentGearIndex);

        bool isTransmissionDisconnected = gearbox.IsNeutral() || clutch.GetClutchFactor() <= 0.01f;

        if (gearbox.IsAutomatic)
        {
            wasShifted = gearbox.UpdateGear(engine.CurrentRPM, throttleInput, currentSpeedKmh, moveY,
                Time.fixedDeltaTime);
        }

        if (!isTransmissionDisconnected)
        {
            float totalDriveRatio = gearbox.CurrentGearRatio * gearbox.finalDriveRatio;

            if (Mathf.Abs(totalDriveRatio) > 0.01f)
            {
                externalRPM = drivenWheelRPM * Mathf.Abs(totalDriveRatio);
            }
        }

        currentEngineTorque = engine.UpdateAndCalculateTorque(
            throttleInput,
            Time.fixedDeltaTime,
            clutch.GetClutchFactor(),
            clutch.GetClutchSlippingFactor(),
            isTransmissionDisconnected,
            externalRPM
        );

        engineRPM = engine.CurrentRPM;

        float driveTorque = 0;
        if (!isTransmissionDisconnected)
        {
            driveTorque = currentEngineTorque * clutch.GetClutchFactor() * gearbox.CurrentGearRatio * gearbox.finalDriveRatio;
        }

        currentWheelTorque = driveTorque;

        ApplyWheelTorque(driveTorque);

        currentGear = gearbox.CurrentGearIndex;
    }

    private void ApplyWheelTorque(float torque)
    {
        float torquePerWheel = 0;
        int drivenWheels = 0;

        if (drivetrainMode == DrivetrainMode.AWD) drivenWheels = 4;
        else if (drivetrainMode == DrivetrainMode.FWD || drivetrainMode == DrivetrainMode.RWD) drivenWheels = 2;

        if (drivenWheels > 0) torquePerWheel = torque / drivenWheels;

        float appliedBrakeTorque = brakeInput * maxBrakeTorque;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (!wheelColliders[i]) continue;

            if (i < 2)
            {
                wheelColliders[i].steerAngle = steeringInput * steerAngle;
            }

            bool isDriven = (drivetrainMode == DrivetrainMode.AWD) ||
                            (drivetrainMode == DrivetrainMode.FWD && i < 2) ||
                            (drivetrainMode == DrivetrainMode.RWD && i >= 2);

            wheelColliders[i].brakeTorque = appliedBrakeTorque;
            wheelColliders[i].motorTorque = isDriven ? torquePerWheel : 0f;

            UpdateWheelVisuals(wheelColliders[i]);
        }
    }


    private float CalculateSmoothedRPM(int wheelIndex, float currentRPM)
    {
        var history = rpmHistory[wheelIndex];
        if (history.Count >= rpmSmoothingWindow)
        {
            history.Dequeue();
        }

        history.Enqueue(currentRPM);

        float sum = 0f;
        foreach (var rpm in history)
        {
            sum += rpm;
        }

        return sum / history.Count;
    }

    private float CalculateDrivenWheelRPM()
    {
        float totalRPM = 0f;
        int drivenCount = 0;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (!wheelColliders[i]) continue;

            bool isDriven = (drivetrainMode == DrivetrainMode.AWD) ||
                            (drivetrainMode == DrivetrainMode.FWD && i < 2) ||
                            (drivetrainMode == DrivetrainMode.RWD && i >= 2);

            if (isDriven)
            {
                float smoothedRPM = CalculateSmoothedRPM(i, wheelColliders[i].rpm);
                totalRPM += smoothedRPM;
                drivenWheelsRPM[i] = smoothedRPM;
                drivenCount++;
            }
        }

        return (drivenCount > 0) ? totalRPM / drivenCount : 0f;
    }


    protected virtual void UpdateWheelVisuals(WheelCollider collider)
    {
        if (collider.transform.childCount > 0)
        {
            Transform visualWheel = collider.transform.GetChild(0);
            Vector3 position;
            Quaternion rotation;
            collider.GetWorldPose(out position, out rotation);
            visualWheel.transform.position = position;
            visualWheel.transform.rotation = rotation;
        }
    }


    protected void ToggleDrivetrain()
    {
        drivetrainMode++;
        if (drivetrainMode > DrivetrainMode.RWD) drivetrainMode = DrivetrainMode.AWD;
        Debug.Log("Drivetrain mode: " + drivetrainMode);
    }

    protected override void OnRespawned()
    {
        engine.Initialize();
        gearbox.Initialize();
        clutch.Reset();

        foreach (var wc in wheelColliders)
        {
            if (wc)
            {
                wc.motorTorque = 0;
                wc.brakeTorque = 0;
            }
        }

        currentSpeedKmh = 0f;
        engineRPM = engine.idleRPM;
        currentGear = gearbox.CurrentGearIndex;
        currentEngineTorque = 0f;
        currentWheelTorque = 0f;
    }

    public float GetEngineRPM() => engine.CurrentRPM;

    public float GetEngineMaxRPM() => engine.maxRPM;

    public int GetCurrentGear() => gearbox.CurrentGearIndex;

    internal void ToggleGearboxMode()
    {
        if (currentSpeedKmh > 0.1f)
        {
            Debug.LogWarning("Cannot change gearbox mode while moving!");
            return;
        }

        gearbox?.ToggleGearboxMode();
    }

    internal void StartStopEngine()
    {
        if (engine.isRunning)
        {
            engine.StopEngine();
        }
        else
        {
            clutch.Reset();
            engine.StartEngine();
        }
    }
}
