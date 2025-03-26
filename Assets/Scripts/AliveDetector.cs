using UnityEngine;

public class AliveDetector : MonoBehaviour
{
    public bool isDead { get; private set; } = false;
    public LayerMask groundLayer;
    public float groundCheckDistance = 2f;
    public float maxAirTime = 5f;
    public Transform frame;

    // settiings
    public float airTime = 0f;
    private bool isGrounded;
    private float absoluteFallThreshold = -200f;
    
    
    private void FixedUpdate()
    {
        // Проверка на землю
        isGrounded = Physics.Raycast(frame.position, -frame.up, groundCheckDistance, groundLayer);
        // Debug.DrawRay (frame.position, frame.down*groundCheckDistance, Color.yellow,1,true);

        // Подсчет времени в воздухе
        if (!isGrounded) airTime += Time.fixedDeltaTime;
        else airTime = 0f;

        // Проверка на падение
        isDead = airTime > maxAirTime || transform.position.y < absoluteFallThreshold;
    }

    public void Recover()
    {
        airTime = 0f;
        isDead = false;
    }
}