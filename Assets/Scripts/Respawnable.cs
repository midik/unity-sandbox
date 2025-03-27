using UnityEngine;
using System.Collections;

public abstract class Respawnable : MonoBehaviour
{
    protected Rigidbody rb;
    protected Vector3 spawnPosition;
    protected Vector3 spawnRotation;

    protected virtual void Start()
    {
        rb = GetComponent<Rigidbody>();
        spawnPosition = transform.position;
        spawnRotation = transform.rotation.eulerAngles;
    }
    
    protected virtual void Awake()
    {
        InitializeRigidBody();
    }

    public void Respawn()
    {
        StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        rb.isKinematic = true;

        transform.position = spawnPosition + Vector3.up * 0.5f;
        transform.rotation = Quaternion.Euler(spawnRotation);

        yield return new WaitForFixedUpdate();

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = false;

        OnRespawned(); // 🔔 событие для дочернего класса
    }
    
    protected void InitializeRigidBody()
    {
        rb = GetComponent<Rigidbody>();
    }

    protected abstract void OnRespawned();
}