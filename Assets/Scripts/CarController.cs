using TMPro;
using UnityEngine;

public class CarController : Driveable // Убедись, что наследуется от твоего Driveable
{
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI respawnText;
    public TextMeshProUGUI drivetrainText;
    public TextMeshProUGUI rpmText;
    public TextMeshProUGUI gearText;
    public TextMeshProUGUI gearboxModeText;

    public SmartFollowCamera followCamera;
    public AliveDetector aliveDetector;

    private InputSystem_Actions inputActions;
    private Vector2 moveInput;


    // Awake is called before Start
    protected override void Awake()
    {
        // Call Driveable's Awake first if it exists and does something important
        base.Awake();

        inputActions = new InputSystem_Actions();

        // Read combined Move input
        inputActions.Player.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        inputActions.Player.Move.canceled += ctx => moveInput = Vector2.zero;

        // Setup other actions
        inputActions.Player.Respawn.performed += ctx => Respawn();
        inputActions.Player.ToggleDrivetrainMode.performed += ctx => ToggleDrivetrain();
        inputActions.Player.ToggleGearMode.performed += ctx => ToggleGearboxMode();
        inputActions.Player.GearUp.performed += ctx => gearbox.GearUp();
        inputActions.Player.GearDown.performed += ctx => gearbox.GearDown();
        inputActions.Player.StartEngine.performed += ctx => StartStopEngine();

        // Find components
        aliveDetector = GetComponent<AliveDetector>();
    }

    // Start is called after Awake
    protected override void Start()
    {
        base.Start();
        if (respawnText) respawnText.gameObject.SetActive(false); // Добавим проверку на null
    }


    void OnEnable() => inputActions.Enable();
    void OnDisable() => inputActions.Disable();

    // Process inputs before FixedUpdate
    void Update()
    {
        // Separate throttle and brake from moveInput.y
        // Газ - только положительная часть Y
        throttleInput = Mathf.Clamp01(moveInput.y);
        // Тормоз - только отрицательная часть Y (берем по модулю)
        brakeInput = Mathf.Clamp01(-moveInput.y);

        // Get steering input
        steeringInput = moveInput.x;

        // Update HUD elements that don't need physics timing
        UpdateHud();
    }


    void FixedUpdate()
    {
        // Handle Dead State
        if (aliveDetector && aliveDetector.isDead) // Убедимся, что aliveDetector не null
        {
            // Stop the car completely when dead
            // Call UpdatePowertrain with zero inputs and max brakes?
            // Передаем moveY = -1, чтобы показать намерение тормозить/ехать назад
            UpdatePowertrain(0f, 1f, 0f, -1f); // Zero throttle, full brake, zero steer, moveY=-1
            // Update text for dead state
            if (speedText) speedText.text = "O_o";
            if (rpmText) rpmText.text = "---";
            if (gearText) gearText.text = "X";
            if (respawnText) respawnText.gameObject.SetActive(true);
            return;
        }

        // --- Call Driveable's UpdatePowertrain with processed inputs ---
        // Передаем исходный moveInput.y для логики включения задней передачи в Gearbox
        UpdatePowertrain(throttleInput, brakeInput, steeringInput, moveInput.y);
    }

    // Override Respawned to also update HUD immediately
    protected override void OnRespawned()
    {
        base.OnRespawned(); // This now resets Engine/Gearbox state in Driveable

        if (aliveDetector) aliveDetector.Recover();
        if (followCamera) followCamera.ResetToTarget();
        if (respawnText) respawnText.gameObject.SetActive(false);

        // Clear inputs immediately after respawn
        moveInput = Vector2.zero;
        throttleInput = 0f;
        brakeInput = 0f;
        steeringInput = 0f;

        UpdateHud(); // Update HUD to show initial state
    }

    // Update HUD elements
    void UpdateHud()
    {
        // Only update if Driveable is initialized and we are not dead
        // Добавим проверку на null для aliveDetector
        if (engine == null || gearbox == null || (aliveDetector && aliveDetector.isDead)) return;

        // Update Speed Text (using the value calculated in Driveable)
        if (speedText) speedText.text = currentSpeedKmh.ToString("F1"); // Display Km/h

        // Update Drivetrain Text
        if (drivetrainText) drivetrainText.text = drivetrainMode.ToString();

        // --- Optional: Update RPM and Gear Text ---
        if (rpmText)
        {
            rpmText.text = GetEngineRPM().ToString("F0"); // Use getter from Driveable
        }

        if (gearText)
        {
            int gear = GetCurrentGear(); // Use getter from Driveable
            string gearStr;
            if (gear == 0) gearStr = "R"; // Индекс 0 = R
            else if (gear == 1) gearStr = "N"; // Индекс 1 = N
            else gearStr = (gear - 1).ToString(); // Индекс 2 = 1-я, 3 = 2-я и т.д.
            gearText.text = gearStr;
        }

        if (gearboxModeText)
        {
            gearboxModeText.text = gearbox.IsAutomatic ? $"Auto ({gearbox.CurrentAutomaticMode})" : "Manual";
        }
    }
}