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
        if (aliveDetector && aliveDetector.isDead)
        {
            engine.StallEngine("dead");
            if (speedText) speedText.text = "O_o";
            if (rpmText) rpmText.text = "X";
            if (gearText) gearText.text = "X";
            if (respawnText) respawnText.gameObject.SetActive(true);
            return;
        }

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

    void UpdateHud()
    {
        if (engine == null || gearbox == null || aliveDetector.isDead) return;

        // Update Speed Text (using the value calculated in Driveable)
        if (speedText) speedText.text = currentSpeedKmh.ToString("F0");

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