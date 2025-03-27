using System.Collections;
using UnityEngine;

public class NpcCarController : Driveable
{
    public Transform target; // Цель (игрок)
    public AliveDetector aliveDetector;
    public Logger logger;

    private bool isRespawnPending;

    protected override void Start()
    {
        base.Start();
        // InitializeWheels();
        // InitializeRigidBody();
        // InitializeSpawnData();

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

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;

        float angle = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
        float steer = Mathf.Clamp(angle / 45f, -1f, 1f) * steerAngle;

        float distance = toTarget.magnitude;
        float motor = distance > stopDistance ? motorTorque : 0f;

        Drive(steer, motor);
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

    public float stopDistance = 15f;
}