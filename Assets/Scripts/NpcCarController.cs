using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AliveDetector))]
public class NpcCarController : Driveable
{
    [Header("Targeting")]
    public Transform target;
    public float stopDistance = 15f;

    [Header("AI Settings")]
    public float edgeDetectionForwardDistance = 2.0f;
    public float edgeDetectionMaxDrop = 10.0f; // Настрой в инспекторе
    public float edgeRaySideOffset = 0.6f;
    public float avoidanceSteerForce = 1.0f;
    public float reverseTime = 1.0f;
    public float turnTime = 1.5f;

    [Header("Obstacle & Slope Avoidance")]
    public bool useObstacleAvoidance = true;
    public float obstacleDetectionDistance = 10f;
    public float maxClimbAngle = 35f;
    public LayerMask obstacleLayerMask; // Слои препятствий (НЕ TerrainChunk)
    public int obstacleFeelerCount = 5; // Нечетное число >= 1
    public float obstacleFeelerAngle = 30f; // Угол для крайних лучей
    public float obstacleAvoidanceWeight = 2.0f; // Приоритет избегания

    private AliveDetector aliveDetector;
    private  Logger logger;

    private bool isRespawnPending;
    private enum AvoidanceState { None, Reversing, Turning }
    private AvoidanceState currentAvoidanceState = AvoidanceState.None;
    private float avoidanceTimer = 0f;
    private float turnDirection = 0f;
    private LayerMask combinedObstacleMask; // Маска для Raycast (Террейн + Препятствия)
    private LayerMask terrainMaskOnly; // Маска только для террейна (для CheckEdgeAheadNew)


    protected override void Start()
    {
        base.Start();
        isRespawnPending = false;
        currentAvoidanceState = AvoidanceState.None;

        if (!aliveDetector) aliveDetector = GetComponent<AliveDetector>();
        if (!aliveDetector) Debug.LogError("AliveDetector not found on NPC car!", this);

        if (!logger) logger = GetComponent<Logger>();

        terrainMaskOnly = LayerMask.GetMask("TerrainChunk");
        combinedObstacleMask = obstacleLayerMask | terrainMaskOnly;

        drivetrainMode = DrivetrainMode.AWD;
        if (!gearbox.IsAutomatic) gearbox.ToggleGearboxMode();
        gearbox.SetAutomaticMode(Gearbox.AutomaticMode.D);
    }

    void FixedUpdate()
    {
        if (isRespawnPending) return;

        if (aliveDetector.isDead)
        {
            logger?.Log($"NPC State: Detected isDead=true. Starting respawn.");
            UpdatePowertrain(0f, 0f, 0f, 0f);
            StartCoroutine(RespawnAfterDelay(3f));
            isRespawnPending = true;
            currentAvoidanceState = AvoidanceState.None;
            return;
        }

        if (currentAvoidanceState != AvoidanceState.None)
        {
            HandleEdgeAvoidance();
            return;
        }

        bool edgeAhead = CheckEdgeAheadNew();
        if (edgeAhead)
        {
            logger?.Log("NPC State: Edge ahead! Starting avoidance.");
            currentAvoidanceState = AvoidanceState.Reversing;
            avoidanceTimer = reverseTime;
            turnDirection = 1f;
            UpdatePowertrain(0f, 1f, -1f, -1f);
            return;
        }

        if (!target) { UpdatePowertrain(0f, 0f, 0f, 0f); return; }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        float distance = toTarget.magnitude;

        if (distance <= stopDistance)
        {
            logger?.Log("NPC State: Target reached. Stopping.");
            UpdatePowertrain(0f, 0f, 0f, 0f);
            return;
        }

        float angle = Vector3.SignedAngle(transform.forward, toTarget.normalized, Vector3.up);
        float seekSteerInput = 0f;
        if (Mathf.Abs(angle) > 1.0f) { seekSteerInput = Mathf.Clamp(angle / 45f, -1f, 1f); }

        float avoidanceSteerInput = 0f;
        if (useObstacleAvoidance)
        {
            avoidanceSteerInput = CalculateObstacleAvoidanceSteer();
        }

        float combinedSteerInput = seekSteerInput + avoidanceSteerInput * obstacleAvoidanceWeight;
        combinedSteerInput = Mathf.Clamp(combinedSteerInput, -1f, 1f);

        float finalSteerAngle = combinedSteerInput * steerAngle;
        float finalMotorTorque = 1f;

        UpdatePowertrain(finalMotorTorque, 0f, finalSteerAngle, 1f);
    }

    // Проверка края (лучи вниз)
    private bool CheckEdgeAheadNew()
    {
        Vector3 carPos = transform.position;
        Quaternion carRot = transform.rotation;
        Vector3 carForward = carRot * Vector3.forward;
        Vector3 carRight = carRot * Vector3.right;
        Vector3 carUp = carRot * Vector3.up;

        Vector3 forwardOffset = carForward * edgeDetectionForwardDistance;
        Vector3 leftOffset = forwardOffset - carRight * edgeRaySideOffset;
        Vector3 rightOffset = forwardOffset + carRight * edgeRaySideOffset;
        forwardOffset.y = 0; leftOffset.y = 0; rightOffset.y = 0; // Обнуляем Y смещений
        Vector3 forwardPos = carPos + forwardOffset;
        Vector3 leftPos = carPos + leftOffset;
        Vector3 rightPos = carPos + rightOffset;
        Vector3 verticalOffset = carUp * 0.1f; // Небольшой подъем старта луча
        float rayLength = edgeDetectionMaxDrop + verticalOffset.magnitude;

        // Используем маску только для террейна
        bool groundUnderLeft = Physics.Raycast(leftPos + verticalOffset, Vector3.down, rayLength, terrainMaskOnly);
        bool groundUnderCenter = Physics.Raycast(forwardPos + verticalOffset, Vector3.down, rayLength, terrainMaskOnly);
        bool groundUnderRight = Physics.Raycast(rightPos + verticalOffset, Vector3.down, rayLength, terrainMaskOnly);

        // Если хотя бы под одной точкой НЕТ земли - значит, впереди обрыв
        bool edgeDetected = !groundUnderLeft || !groundUnderCenter || !groundUnderRight;
        // if (edgeDetected) logger?.Log($"Edge Detected! L={groundUnderLeft}, C={groundUnderCenter}, R={groundUnderRight}"); // Лог только при обнаружении

        return edgeDetected;
    }

    // Расчет руления для избегания препятствий и склонов
    private float CalculateObstacleAvoidanceSteer()
    {
        float totalAvoidance = 0f;
        int hitCount = 0; // Считаем, сколько лучей что-то задело (для нормализации?)

        // Рассчитываем угол между лучами
        float angleStep = 0;
        int halfFeelerCount = 0;
        if (obstacleFeelerCount > 1)
        {
            angleStep = obstacleFeelerAngle * 2f / (obstacleFeelerCount - 1);
            halfFeelerCount = obstacleFeelerCount / 2; // Целочисленное деление
        }

        for (int i = 0; i < obstacleFeelerCount; i++)
        {
            float currentAngle = 0;
            if (obstacleFeelerCount > 1)
            {
                 currentAngle = -obstacleFeelerAngle + i * angleStep;
            }

            Quaternion rotation = Quaternion.AngleAxis(currentAngle, transform.up);
            Vector3 direction = rotation * transform.forward;
            RaycastHit hit;
            Vector3 rayStart = transform.position + transform.up * 0.5f; // Старт луча из центра машины

            // Пускаем луч, используя комбинированную маску
            if (Physics.Raycast(rayStart, direction, out hit, obstacleDetectionDistance, combinedObstacleMask))
            {
                hitCount++;
                float avoidanceForce = 0f;
                bool needsAvoidance = false;

                // Проверяем слой попадания
                int hitLayer = hit.collider.gameObject.layer;

                // Если попали в террейн
                if (((1 << hitLayer) & terrainMaskOnly) != 0)
                {
                    float slopeAngle = Vector3.Angle(Vector3.up, hit.normal);
                    if (slopeAngle > maxClimbAngle)
                    {
                        // Склон слишком крутой - избегаем
                        avoidanceForce = 1.0f - (hit.distance / obstacleDetectionDistance);
                        needsAvoidance = true;
                        // logger?.Log($"Obstacle: Steep slope {slopeAngle:F0} deg at {hit.distance:F1}m angle {currentAngle:F0}");
                    }
                    // else { // Пологий склон - игнорируем }
                }
                // Если попали в слой препятствия (не террейн)
                else if (((1 << hitLayer) & obstacleLayerMask) != 0)
                {
                    // Другое препятствие - избегаем
                    avoidanceForce = 1.0f - (hit.distance / obstacleDetectionDistance);
                    needsAvoidance = true;
                    // logger?.Log($"Obstacle: Object '{hit.collider.name}' detected at {hit.distance:F1}m angle {currentAngle:F0}");
                }

                // Если нужно избегать, добавляем силу руления
                if (needsAvoidance)
                {
                    // Рулим в сторону, противоположную лучу
                    float steerDirection = 0;
                    if (Mathf.Abs(currentAngle) < 0.1f) steerDirection = 1f; // От центрального - вправо
                    else steerDirection = -Mathf.Sign(currentAngle);

                    totalAvoidance += steerDirection * avoidanceForce;
                }
            }
        }

        // Можно нормализовать или просто ограничить
        // if (hitCount > 0) totalAvoidance /= hitCount; // Усреднение? Или нет?
        return Mathf.Clamp(totalAvoidance, -1f, 1f);
    }


    // Обработка состояний избегания края
    private void HandleEdgeAvoidance()
    {
        avoidanceTimer -= Time.fixedDeltaTime;
        if (currentAvoidanceState == AvoidanceState.Reversing)
        {
            if (logger) logger.Log($"NPC State: Avoiding Edge - Reversing ({avoidanceTimer:F1}s left)");
            UpdatePowertrain(0f, 1f, -1f, -1f);
            if (avoidanceTimer <= 0f)
            {
                currentAvoidanceState = AvoidanceState.Turning;
                avoidanceTimer = turnTime;
                if (logger) logger.Log("NPC State: Avoiding Edge - Switching to Turning");
            }
        }
        else if (currentAvoidanceState == AvoidanceState.Turning)
        {
            if (logger) logger.Log($"NPC State: Avoiding Edge - Turning { (turnDirection > 0 ? "Right" : "Left") } ({avoidanceTimer:F1}s left)");
            float turnSteerAngle = turnDirection * steerAngle * avoidanceSteerForce;
            float turnMotorTorque = 0.3f;
            UpdatePowertrain(turnMotorTorque, 0f, turnSteerAngle, 0.3f);
            if (avoidanceTimer <= 0f)
            {
                currentAvoidanceState = AvoidanceState.None;
                if (logger) logger.Log("NPC State: Avoiding Edge - Finished");
            }
        }
    }


    // Корутина для задержки перед респауном
    private IEnumerator RespawnAfterDelay(float delay)
    {
        if (logger) logger.Log("NPC State: Respawn pending");
        yield return new WaitForSeconds(delay);
        Respawn();
    }


    protected override void OnRespawned()
    {
        base.OnRespawned();
        if (!aliveDetector) return;
        
        aliveDetector.Recover();
        isRespawnPending = false;
        currentAvoidanceState = AvoidanceState.None;
        if (logger) logger.Log("NPC State: Respawn completed");
    }
}
