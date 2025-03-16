using System.Collections;
using UnityEngine;

public class NpcCarController : MonoBehaviour
{
    public Transform target; // Цель (игрок)
    public WheelCollider[] wheels; // 4 коллайдера
    public float motorTorque = 1000f;
    public float steerAngle = 30f;
    public float maxSpeed = 20f;
    public float stopDistance = 10f; // расстояние, когда NPC "доволен"

    public Rigidbody rb;
    public AliveDetector aliveDetector;
    public Logger logger;

    private WheelCollider FL;
    private WheelCollider FR;
    private WheelCollider RL;
    private WheelCollider RR;

    private Vector3 spawnPosition;
    private Vector3 spawnRotation;

    void Start()
    {
        FL = wheels[0];
        FR = wheels[1];
        RL = wheels[2];
        RR = wheels[3];
        
        spawnPosition = transform.position;
        spawnRotation = transform.rotation.eulerAngles;
    }

    void FixedUpdate()
    {
        if (aliveDetector.isDead)
        {
            logger.Log("NPC car stuck");
            
            Drive(0f, 0f);
            StartCoroutine(RespawnAfterDelay(3f));
            return;
        }

        // Вектор к цели
        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0f;
        
        Debug.DrawRay(transform.position, toTarget.normalized * 10, Color.green); // цель
        Debug.DrawRay(transform.position, transform.forward * 10, Color.red); // морда

        // Угол между forward и направлением на цель
        float angle = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);

        // Поворот колес (-1 до 1)
        float steer = Mathf.Clamp(angle / 45f, -1f, 1f) * steerAngle;

        // Если далеко — газуем, иначе останавливаемся
        float distance = toTarget.magnitude;
        float motor = distance > 10f ? motorTorque : 0f;

        // --- Управляем колесами ---
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
        Respawn();
    }

    public void Respawn()
    {
        logger.Log("NPC respawn started");
        transform.position = spawnPosition + Vector3.up * 0.5f;
        transform.rotation = Quaternion.Euler(spawnRotation);
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Drive(0f, 0f);

        aliveDetector.recover();
        logger.Log("NPC respawn completed");
    }
}
