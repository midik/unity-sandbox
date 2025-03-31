using UnityEngine;
using UnityEngine.Serialization;

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

    [Header("Powertrain Components")] public Engine engine; // Assign or configure Engine settings here
    public Gearbox gearbox; // Assign or configure Gearbox settings here

    [Header("Clutch Settings")]
    [Tooltip("Кривая включения сцепления. X=Обороты выше холостых (норм. 0..1), Y=Фактор сцепления (0..1)")]
    public AnimationCurve clutchEngagementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f); // Плавный старт по умолчанию
    [FormerlySerializedAs("clutchBackpressureFactor")] [Tooltip("Фактор сцепления, ниже которого сцепление начинает проскальзывать (0..1)")]
    public float clutchSlippingFactor = 0.50f; 

    [Tooltip("Диапазон оборотов (выше холостых), на котором происходит включение сцепления от 0 до 1")]
    public float clutchEngageRPMRange = 600f; // Например, сцепление полностью включится при idleRPM + 500 RPM

    [Header("Braking")] public float maxBrakeTorque = 5000f;

    [Header("Drivetrain")] [SerializeField] // Keep protected but allow viewing in inspector
    protected DrivetrainMode drivetrainMode = DrivetrainMode.AWD;

    public enum DrivetrainMode
    {
        AWD,
        FWD,
        RWD
    }

    [Header("Readouts (Read Only)")] [SerializeField, ReadOnly]
    protected float currentSpeedKmh;

    [SerializeField, ReadOnly] protected float engineRPM;
    [SerializeField, ReadOnly] protected int currentGear;
    [SerializeField, ReadOnly] protected float currentEngineTorque;
    [SerializeField, ReadOnly] protected float currentWheelTorque; // Torque *after* gearbox/diff
    [SerializeField, ReadOnly] protected float clutchFactor;

    // --- Internal State ---
    protected float throttleInput; // 0..1
    protected float brakeInput; // 0..1
    protected float steeringInput; // -1..1

    private WheelCollider[] wheelColliders = new WheelCollider[4]; // FL, FR, RL, RR order assumed
    private float targetEngineRPMFromWheels; // Добавим для расчета внутри UpdatePowertrain


    protected override void Awake() // Use Awake for component references
    {
        // base.Awake(); // Call base Awake if it exists and does something important
        // В базовом Respawnable Awake вызывает InitializeRigidBody, base.Awake() не нужен, если мы вызываем InitializeRigidBody здесь или в Start базового класса
        // InitializeRigidBody(); // Вызывается в базовом Awake
        FindWheelColliders(); // Find colliders if not assigned
    }

    protected override void Start()
    {
        base.Start(); // Calls Respawnable's Start (which gets Rigidbody and sets spawn pos/rot)

        engine.Initialize();
        gearbox.Initialize();
        currentSpeedKmh = 0f;
        engineRPM = engine.idleRPM;
        currentGear = gearbox.CurrentGearIndex;
        currentEngineTorque = 0f;
        currentWheelTorque = 0f;
        clutchFactor = 0f; // Начинаем с выключенным сцеплением
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

        // Убедимся, что Rigidbody есть (на случай, если base.Start не вызвался или был переопределен без вызова)
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

        // Добавим проверку на rb, если он вдруг не найден
        currentSpeedKmh = rb ? rb.linearVelocity.magnitude * 3.6f : 0f;

        // Calculate current speed & wheel RPM
        float drivenWheelRPM = CalculateDrivenWheelRPM();

        bool wasShifted = false; // Флаг для отслеживания переключения

        // Определяем, пытается ли водитель ехать (газ нажат ИЛИ обороты выше холостых)
        bool tryingToEngage = throttleInput > 0.01f || engine.CurrentRPM > engine.idleRPM;

        // Рассчитываем фактор сцепления
        // Полностью выключаем только если обороты на холостых ИЛИ НИЖЕ, И газ НЕ нажат
        if (!tryingToEngage && engine.CurrentRPM <= engine.idleRPM)
        {
            clutchFactor = 0.0f;
        }
        else
        {
            // Рассчитываем обороты относительно начала включения (idleRPM)
            float rpmAboveIdle = engine.CurrentRPM - engine.idleRPM;
            // Нормализуем эти обороты в диапазоне [0, clutchEngageRPMRange] -> [0, 1]
            float normalizedEngageRPM = Mathf.Clamp01(rpmAboveIdle / clutchEngageRPMRange);
            // Получаем фактор сцепления из кривой
            clutchFactor = clutchEngagementCurve.Evaluate(normalizedEngageRPM);
        }

        // Гарантируем, что фактор всегда в пределах [0, 1]
        clutchFactor = Mathf.Clamp01(clutchFactor);


        // --- Обновляем коробку передач, если автомат ---
        if (gearbox.IsAutomatic)
        {
            wasShifted = gearbox.UpdateGear(engine.CurrentRPM, throttleInput, currentSpeedKmh, moveY,
                Time.fixedDeltaTime);
        }

        // --- Обновляем двигатель ---
        // Определяем, должны ли обороты двигателя СЛЕДОВАТЬ за колесами.
        // Условие: Водитель пытается ехать, мы на передаче, сцепление ХОТЯ БЫ ЧАСТИЧНО включено (factor > ~0),
        // и мы НЕ только что переключились (даем оборотам "устаканиться")
        bool isDrivetrainConnected = gearbox.IsInGear && clutchFactor > clutchSlippingFactor && !wasShifted;
        if (isDrivetrainConnected)
        {
            // Сцепление частично включено -> двигатель частично синхронизируется с колесами, учитывая проскальзывание
            float totalDriveRatio = gearbox.CurrentGearRatio * gearbox.finalDriveRatio;
            float targetEngineRPMFromWheels = 0f;
            if (Mathf.Abs(totalDriveRatio) > 0.01f)
            {
                targetEngineRPMFromWheels = drivenWheelRPM * Mathf.Abs(totalDriveRatio);
            }

            // Смешиваем текущие обороты двигателя с целевыми оборотами от колес по фактору сцепления
            float blendedRPM = Mathf.Lerp(engine.CurrentRPM, targetEngineRPMFromWheels, 1 - clutchFactor);
            engine.SetRPMFromLoad(blendedRPM);
        }

        // Вызываем основной метод обновления движка.
        // Он обновит CurrentRPM движка, ТОЛЬКО ЕСЛИ !shouldSyncRPM (т.е. сцепление выключено или только что переключились).
        // Он ВСЕГДА вернет потенциальный момент (положительный минус сопротивление) на ТЕКУЩИХ оборотах движка.
        // Передаем !isDrivetrainConnected как флаг "свободного вращения"
        currentEngineTorque = engine.UpdateAndCalculateTorque(throttleInput, isDrivetrainConnected, Time.fixedDeltaTime);
        engineRPM = engine.CurrentRPM; // Обновляем для отображения

        // --- Расчет момента на колесах ---
        float driveTorque = 0;
        // Передаем момент, если мы на передаче, умножая на ПЛАВНЫЙ clutchFactor
        if (gearbox.IsInGear)
        {
            // Используем ПОСЛЕДНИЙ рассчитанный чистый момент от движка и текущий clutchFactor
            driveTorque = currentEngineTorque * clutchFactor * gearbox.CurrentGearRatio * gearbox.finalDriveRatio;
        }

        currentWheelTorque = driveTorque; // Сохраняем для отладки

        // --- Применение к WheelColliders ---
        float torquePerWheel = 0;
        int drivenWheels = 0;
        if (drivetrainMode == DrivetrainMode.AWD) drivenWheels = 4;
        else if (drivetrainMode == DrivetrainMode.FWD || drivetrainMode == DrivetrainMode.RWD) drivenWheels = 2;
        if (drivenWheels > 0) torquePerWheel = driveTorque / drivenWheels;

        float appliedBrakeTorque = brakeInput * maxBrakeTorque;


        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (!wheelColliders[i]) continue; // Пропуск если колесо не найдено
            // Apply Steering (to front wheels: index 0, 1)
            if (i < 2)
            {
                wheelColliders[i].steerAngle = steeringInput * steerAngle;
            }

            // Apply Motor Torque or Brake Torque
            bool isDriven = (drivetrainMode == DrivetrainMode.AWD) ||
                            (drivetrainMode == DrivetrainMode.FWD && i < 2) ||
                            (drivetrainMode == DrivetrainMode.RWD && i >= 2);

            // --- Применяем тормоз ИЛИ момент ---
            if (appliedBrakeTorque > 0.01f) // Если нажали тормоз
            {
                // wheelColliders[i].motorTorque = 0f; // Момент двигателя не применяем
                wheelColliders[i].brakeTorque = appliedBrakeTorque; // Применяем тормоз
            }
            else // Если тормоз НЕ нажат
            {
                wheelColliders[i].brakeTorque = 0f; // Тормоз не применяем
                wheelColliders[i].motorTorque = isDriven ? torquePerWheel : 0f; // Применяем момент, если колесо ведущее
            }

            // Update visual wheel models (if you have them)
            UpdateWheelVisuals(wheelColliders[i]);
        }

        // Update readouts
        currentGear = gearbox.CurrentGearIndex;
    }


    // Calculate average RPM of the wheels that are currently driven
    private float CalculateDrivenWheelRPM()
    {
        float totalRPM = 0f;
        int drivenCount = 0;

        for (int i = 0; i < wheelColliders.Length; i++)
        {
            if (!wheelColliders[i]) continue; // Пропуск если колесо не найдено

            bool isDriven = (drivetrainMode == DrivetrainMode.AWD) ||
                            (drivetrainMode == DrivetrainMode.FWD && i < 2) ||
                            (drivetrainMode == DrivetrainMode.RWD && i >= 2);

            if (isDriven)
            {
                totalRPM += wheelColliders[i].rpm;
                drivenCount++;
            }
        }

        // Избегаем деления на ноль, если нет ведущих колес (или они не найдены)
        return (drivenCount > 0) ? totalRPM / drivenCount : 0f;
    }


    // Placeholder for updating visual wheel meshes/transforms
    protected virtual void UpdateWheelVisuals(WheelCollider collider)
    {
        // Example: Find the corresponding visual transform and update rotation/position
        // if (collider.transform.childCount > 0) { // Basic check
        //     Transform visualWheel = collider.transform.GetChild(0); // Assuming visual is child
        //     Vector3 position;
        //     Quaternion rotation;
        //     collider.GetWorldPose(out position, out rotation);
        //     visualWheel.transform.position = position;
        //     visualWheel.transform.rotation = rotation;
        // }
    }


    protected void ToggleDrivetrain()
    {
        drivetrainMode++;
        if (drivetrainMode > DrivetrainMode.RWD) drivetrainMode = DrivetrainMode.AWD; // Cycle through AWD, FWD, RWD
        Debug.Log("Drivetrain mode: " + drivetrainMode);
    }

    // Make sure Respawn resets powertrain state
    protected override void OnRespawned() // Этот метод вызывается из Respawnable.RespawnRoutine()
    {
        // base.OnRespawned();
        engine.Initialize();
        gearbox.Initialize();

        // Reset wheel torques immediately
        foreach (var wc in wheelColliders)
        {
            if (wc)
            {
                wc.motorTorque = 0;
                wc.brakeTorque = 0;
            }
        }

        // Reset readouts
        currentSpeedKmh = 0f;
        engineRPM = engine.idleRPM;
        currentGear = gearbox.CurrentGearIndex;
        currentEngineTorque = 0f;
        currentWheelTorque = 0f;
        clutchFactor = 0f; // Start disengaged after respawn
    }

    // Expose some data for HUD/Audio if needed
    public float GetEngineRPM() => engine?.CurrentRPM ?? 0f;

    public float GetEngineMaxRPM() => engine?.maxRPM ?? 4500f;

    public int GetCurrentGear() => gearbox?.CurrentGearIndex ?? 0;

    internal void ToggleGearboxMode()
    {
        if (currentSpeedKmh > 0.1f)
        {
            Debug.LogWarning("Cannot change gearbox mode while moving!");
            return;
        }

        gearbox?.ToggleGearboxMode();
    }
}