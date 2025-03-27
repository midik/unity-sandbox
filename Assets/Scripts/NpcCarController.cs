using System.Collections;
using UnityEngine;

public class NpcCarController : Respawnable
{
    public Transform target;
    
    public float motorTorque = 1000f;
    public float steerAngle = 30f;
    public float maxSpeed = 20f;
    public float stopDistance = 15f;

    public AliveDetector aliveDetector;
    public Logger logger;

    private WheelCollider FL, FR, RL, RR;

    private bool isRespawnPending = false;

    protected override void Start()
    {
        base.Start();

        FL = transform.Find("Wheel FL").GetComponent<WheelCollider>();
        FR = transform.Find("Wheel FR").GetComponent<WheelCollider>();
        RL = transform.Find("Wheel RL").GetComponent<WheelCollider>();
        RR = transform.Find("Wheel RR").GetComponent<WheelCollider>();
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

    private void Drive(float steering, float motor)
    {
        FL.steerAngle = steering;
        FR.steerAngle = steering;

        FL.motorTorque = motor;
        FR.motorTorque = motor;
        RL.motorTorque = motor;
        RR.motorTorque = motor;
    }

    private IEnumerator RespawnAfterDelay(float delay)
    {
        logger.Log("NPC respawn pending");
        yield return new WaitForSeconds(delay);
        Respawn(); // 🔁 базовый класс Respawnable
    }

    protected override void OnRespawned()
    {
        logger.Log("NPC respawn completed");
        aliveDetector.Recover();
        isRespawnPending = false;
    }
}
