using System.Collections;
using UnityEngine;

public class NpcCarController : MonoBehaviour
{
    public Transform target; // Цель (игрок)
    public Transform frame;
    public WheelCollider[] wheels; // 4 коллайдера
    
    public float motorTorque = 1000f;
    public float steerAngle = 30f;
    public float maxSpeed = 20f;
    public float stopDistance = 15f; // расстояние, когда NPC "доволен"

    public Rigidbody rb;
    public AliveDetector aliveDetector;
    public Logger logger;

    private WheelCollider FL;
    private WheelCollider FR;
    private WheelCollider RL;
    private WheelCollider RR;

    private Vector3 spawnPosition;
    private Vector3 spawnRotation;
    
    private bool isRespawnPending = false;
    

    void Start()
    {
        FL = wheels[0];
        FR = wheels[1];
        RL = wheels[2];
        RR = wheels[3];
        
        spawnPosition = frame.position;
        spawnRotation = frame.rotation.eulerAngles;
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

        // Вектор к цели
        Vector3 toTarget = target.position - frame.position;
        toTarget.y = 0f;
        
        Debug.DrawRay(frame.position, toTarget.normalized * 10, Color.green); // цель
        Debug.DrawRay(frame.position, frame.forward * 10, Color.red); // морда

        // Угол между forward и направлением на цель
        float angle = Vector3.SignedAngle(frame.forward, toTarget, Vector3.up);

        // Поворот колес (-1 до 1)
        float steer = Mathf.Clamp(angle / 45f, -1f, 1f) * steerAngle;

        // Если далеко — газуем, иначе останавливаемся
        float distance = toTarget.magnitude;
        float motor = distance > stopDistance ? motorTorque : 0f;

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
        frame.position = spawnPosition + Vector3.up * 0.5f;
        frame.rotation = Quaternion.Euler(spawnRotation);
        
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        Drive(0f, 0f);

        aliveDetector.recover();
        isRespawnPending = false;
        logger.Log("NPC respawn completed");
    }
}
