using System.Collections;
using UnityEngine;

public class NpcCarController : Driveable
{
    public Transform target; // Цель (игрок)
    public AliveDetector aliveDetector;
    public Logger logger;

    public float stopDistance = 15f;
    public float edgeDetectionDistance = 3f;
    public float rayOffsetY = 0.5f;
    public float sideOffset = 0.6f;

    private bool isRespawnPending;

    protected override void Start()
    {
        base.Start();
        isRespawnPending = false;
        aliveDetector = GetComponent<AliveDetector>();
    }

    void FixedUpdate()
    {
        if (aliveDetector.isDead && !isRespawnPending)
        {
            logger.Log("NPC car stuck");
            Drive(0f, 0f);
            StartCoroutine(RespawnAfterDelay(3f));
            isRespawnPending = true;
            return;
        }

        if (EdgeAhead())
        {
            logger.Log("Edge ahead! Stopping");
            Drive(0f, 0f);
            return;
        }

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        float angle = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
        float steer = Mathf.Clamp(angle / 45f, -1f, 1f) * steerAngle;

        float distance = toTarget.magnitude;
        float motor = distance > stopDistance ? motorTorque : 0f;

        Drive(steer, motor);
    }

    private bool EdgeAhead()
    {
        Vector3 velocity = rb.linearVelocity;
        if (velocity.magnitude < 0.1f) return false; // Стоим на месте — не проверяем

        Vector3 direction = velocity.normalized;
        Vector3 origin = transform.position + Vector3.up * rayOffsetY;
        Vector3[] rayOrigins =
        {
            origin,
            origin + transform.right * sideOffset,
            origin - transform.right * sideOffset
        };

        LayerMask terrainMask = LayerMask.GetMask("TerrainChunk");

        foreach (var rayOrigin in rayOrigins)
        {
            bool hit = Physics.Raycast(rayOrigin, direction, out RaycastHit info, edgeDetectionDistance, terrainMask);

            Debug.DrawRay(rayOrigin, direction * edgeDetectionDistance, hit ? Color.green : Color.red, 0.1f);

            if (hit)
                return false; // Земля есть впереди — всё норм
        }

        return true; // Ни один луч не нашёл землю
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        logger.Log("NPC respawn pending");
        yield return new WaitForSeconds(delay);
        Respawn();
    }

    protected override void OnRespawned()
    {
        aliveDetector.Recover();
        isRespawnPending = false;
        logger.Log("NPC respawn completed");
    }
}
