using UnityEngine;

public class AliveDetector : MonoBehaviour
{
    public bool isDead { get; private set; } = false;
    public LayerMask groundLayer;
    public Transform target;
    public float groundCheckDistance = 2f;
    public float maxAirTime = 5f;
    public bool isGrounded;

    // settiings
    public float airTime = 0f;
    private float absoluteFallThreshold = -200f;
    
    
    private void FixedUpdate()
    {
        // Проверка на землю
        isGrounded = Physics.Raycast(target.position, Vector3.down, groundCheckDistance, groundLayer);
        // Debug.DrawRay (target.position, transform.down*2f, Color.yellow,1,true);

        // Подсчет времени в воздухе
        if (!isGrounded) airTime += Time.fixedDeltaTime;
        else airTime = 0f;

        // Проверка на падение
        isDead = airTime > maxAirTime || transform.position.y < absoluteFallThreshold;
    }

    public void recover()
    {
        airTime = 0f;
        isDead = false;
    }
}