using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AliveDetector))]
public class NpcCarController : Driveable
{
    [Header("Targeting")] public Transform target;
    public float stopDistance = 15f;

    [Header("AI Settings")] public float edgeDetectionForwardDistance = 2.0f;
    public float edgeDetectionMaxDrop = 10.0f;
    public float edgeRaySideOffset = 0.6f;
    public float avoidanceSteerForce = 1.0f;
    public float reverseTime = 1.0f;
    public float turnTime = 1.5f;
    public float throttleWhileSteering = 0.6f;

    [Tooltip("How close the car needs to be to a waypoint to switch to the next one.")]
    public float waypointSwitchDistance = 3.0f;

    [Header("Obstacle & Slope Avoidance")] public bool useObstacleAvoidance = true;
    public float obstacleDetectionDistance = 10f;
    public float maxClimbAngle = 35f;
    public LayerMask obstacleLayerMask; // Слои препятствий (НЕ TerrainChunk)
    public int obstacleFeelerCount = 5; // Нечетное число >= 1
    public float obstacleFeelerAngle = 30f; // Угол для крайних лучей
    public float obstacleAvoidanceWeight = 2.0f; // Приоритет избегания

    private AliveDetector aliveDetector;
    private Logger logger;

    private List<Vector3> currentPath = new List<Vector3>();
    private int currentPathIndex = 0;

    private bool isRespawnPending;

    private enum AvoidanceState
    {
        None,
        Reversing,
        Turning
    }

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

        if (!logger) logger = FindFirstObjectByType<Logger>();
        if (!logger) Debug.LogWarning("Logger not found on NPC car!", this);

        terrainMaskOnly = LayerMask.GetMask("TerrainChunk");
        combinedObstacleMask = obstacleLayerMask | terrainMaskOnly;

        drivetrainMode = DrivetrainMode.AWD;
        if (!gearbox.IsAutomatic) gearbox.ToggleGearboxMode();
        gearbox.SetAutomaticMode(Gearbox.AutomaticMode.D);
    }

    [ContextMenu("Respawn")]
    void ForceRespawn()
    {
        if (logger) logger.Log("[NPC] Force respawn called");
        StartCoroutine(RespawnAfterDelay(1f));
    }

    void FixedUpdate()
    {
        if (isRespawnPending) return;

        if (aliveDetector.isDead)
        {
            logger?.Log($"[NPC] Detected isDead=true. Starting respawn.");
            Drive(0f, 0f, 0f, 0f);
            StartCoroutine(RespawnAfterDelay(3f));
            isRespawnPending = true;
            currentAvoidanceState = AvoidanceState.None;
            return;
        }

        // if (currentAvoidanceState != AvoidanceState.None)
        // {
        //     HandleEdgeAvoidance();
        //     return;
        // }

        bool edgeAhead = CheckEdgeAheadNew();
        if (edgeAhead)
        {
            logger?.Log("[NPC] Edge ahead! Starting avoidance.");
            currentAvoidanceState = AvoidanceState.Reversing;
            avoidanceTimer = reverseTime;
            turnDirection = 1f;
            Drive(0f, 1f, -1f, -1f);
            return;
        }

        if (!target)
        {
            Drive(0f, 0f, 0f, 0f);
            return;
        }

        // Проверяем, достигли ли цели
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= stopDistance)
        {
            logger?.Log("[NPC] Target reached. Stopping.");
            Drive(0f, 1f, 0f, 0f);
            return;
        }

        if (currentPath.Count == 0 || currentPathIndex >= currentPath.Count)
        {
            logger?.Log("[NPC] Path is empty or index out of range. Updating path.");
            UpdatePathToTarget();
            return;
        }

        // visualize path
        for (int i = 0; i < currentPath.Count; i++)
        {
            Debug.DrawLine(currentPath[i], currentPath[i] + Vector3.up, Color.red);
        }

        Vector3 nextWaypoint = currentPath[currentPathIndex];
        Vector3 toWaypoint = nextWaypoint - transform.position;
        // Keep vertical component for accurate distance check? Decide based on gameplay needs.
        // If waypoints are always on the ground, ignoring Y is fine.
        Vector3 toWaypointFlat = toWaypoint;
        toWaypointFlat.y = 0; // Use flat vector for steering angle calculation

        logger.Log("[NPC] Current pos: +" + target.position + ", Following path:" + currentPath.ToArray() +
                   ", Next waypoint: " + nextWaypoint);

        if (toWaypoint.magnitude < waypointSwitchDistance) // Check actual 3D distance
        {
            // logger?.Log($"[NPC] Reached waypoint {currentPathIndex}. Switching to next.");
            currentPathIndex++;
            // Check if we reached the end of the path after incrementing
            if (currentPathIndex >= currentPath.Count)
            {
                logger?.Log("[NPC] Reached end of path. Requesting new path.");
                UpdatePathToTarget(); // Request new path immediately

                // Optional: Stop briefly or continue towards final target if path update fails
                if (currentPath == null || currentPath.Count == 0)
                {
                    Drive(0f, 1f, 0f, 0f); // Stop if path update fails
                }

                return; // Exit FixedUpdate for this frame
            }

            // Update nextWaypoint for steering calculation in the same frame if needed
            // This prevents steering towards the old waypoint for one frame
            if (currentPathIndex < currentPath.Count)
            {
                nextWaypoint = currentPath[currentPathIndex];
                toWaypoint = nextWaypoint - transform.position;
                toWaypointFlat = toWaypoint;
                toWaypointFlat.y = 0;
            }
            else
            {
                // This case should ideally be handled by the check above, but as a fallback:
                logger.Log("[NPC] Path index became invalid after increment. Stopping.");
                Drive(0f, 1f, 0f, 0f);
                return;
            }
        }

        // Calculate steering towards the (potentially updated) next waypoint
        float targetSteerAngle = 0f;
        if (toWaypointFlat.sqrMagnitude > 0.01f) // Avoid calculating angle for zero vector
        {
            targetSteerAngle = Vector3.SignedAngle(transform.forward, toWaypointFlat.normalized, transform.up);
        }

        float steerInput = Mathf.Clamp(targetSteerAngle / maxSteeringAngle, -1f, 1f); // Normalize steer angle


        // --- Obstacle Avoidance Steering (Optional) ---
        float avoidanceSteer = 0f;
        if (useObstacleAvoidance)
        {
            avoidanceSteer = CalculateObstacleAvoidanceSteer();
        }

        // --- Combine Steering Inputs ---
        // Simple averaging or weighted sum? Weighted sum gives more control.
        // Example: float finalSteerInput = steerInput * (1 - obstacleAvoidanceWeight) + avoidanceSteer * obstacleAvoidanceWeight;
        // Simpler approach: Prioritize avoidance?
        float finalSteerInput = steerInput;
        if (Mathf.Abs(avoidanceSteer) > 0.1f) // If avoidance is significant
        {
            // Blend or override? Let's blend slightly towards avoidance
            finalSteerInput =
                Mathf.Lerp(steerInput, avoidanceSteer, obstacleAvoidanceWeight * 0.5f); // Adjust blending factor
            // logger?.Log($"[NPC] Blending Steer: Path={steerInput:F2}, Avoid={avoidanceSteer:F2}, Final={finalSteerInput:F2}");
        }

        finalSteerInput = Mathf.Clamp(finalSteerInput, -1f, 1f);


        // --- Throttle Control ---
        // Reduce speed when turning sharply or avoiding obstacles?
        float currentThrottle = throttleWhileSteering; // Base throttle

        // Reduce throttle based on steering angle magnitude
        currentThrottle *= Mathf.Lerp(1f, 0.5f, Mathf.Abs(finalSteerInput)); // Reduce up to 50% for full steer

        // Reduce further if actively avoiding obstacles
        if (Mathf.Abs(avoidanceSteer) > 0.1f)
        {
            currentThrottle *= 0.7f; // Further reduce speed while avoiding
        }


        // --- Final Drive Command ---
        // logger?.Log($"[NPC] Driving: Throttle={currentThrottle:F2}, Steer={finalSteerInput:F2}");
        Drive(currentThrottle, 0f, finalSteerInput * maxSteeringAngle, 1f); // Use final steer, adjusted throttle
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
        forwardOffset.y = 0;
        leftOffset.y = 0;
        rightOffset.y = 0; // Обнуляем Y смещений
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
        if (edgeDetected)
            logger?.Log(
                $"Edge Detected! L={groundUnderLeft}, C={groundUnderCenter}, R={groundUnderRight}"); // Лог только при обнаружении

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
            if (logger) logger.Log($"[NPC] Avoiding Edge - Reversing ({avoidanceTimer:F1}s left)");
            Drive(0f, 1f, -1f, -1f);
            if (avoidanceTimer <= 0f)
            {
                currentAvoidanceState = AvoidanceState.Turning;
                avoidanceTimer = turnTime;
                if (logger) logger.Log("[NPC] Avoiding Edge - Switching to Turning");
            }
        }
        else if (currentAvoidanceState == AvoidanceState.Turning)
        {
            if (logger)
                logger.Log(
                    $"[NPC] Avoiding Edge - Turning {(turnDirection > 0 ? "Right" : "Left")} ({avoidanceTimer:F1}s left)");
            float turnSteerAngle = turnDirection * maxSteeringAngle * avoidanceSteerForce;
            float turnMotorTorque = 0.3f;
            Drive(turnMotorTorque, 0f, turnSteerAngle, 0.3f);
            if (avoidanceTimer <= 0f)
            {
                currentAvoidanceState = AvoidanceState.None;
                if (logger) logger.Log("[NPC] Avoiding Edge - Finished");
            }
        }
    }

    // Корутина для задержки перед респауном
    private IEnumerator RespawnAfterDelay(float delay)
    {
        if (logger) logger.Log("[NPC] Respawn pending");
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
        if (logger) logger.Log("[NPC] Respawn completed");
    }

    private void Drive(float torque, float brake, float steerAngle, float throttle)
    {
        if (torque > 0.01f)
        {
            if (!engine.isRunning)
            {
                engine.StartEngine();
            }
        }

        UpdatePowertrain(torque, brake, steerAngle, throttle);
    }

    void UpdatePathToTarget()
    {
        if (target)
        {
            // pick the point 2m ahead of the car
            Vector3 currentPoint = transform.position + transform.forward * 2f;
            currentPath = PathfindingManager.Instance.FindPath(currentPoint, target.position);
            currentPathIndex = 0;

            // draw path for debugging
            for (int i = 0; i < currentPath.Count; i++)
            {
                Debug.DrawLine(currentPath[i], currentPath[i] + Vector3.up, Color.yellow);
            }
        }
    }
}