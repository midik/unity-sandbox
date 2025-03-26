using UnityEngine;
using System;
using Object = UnityEngine.Object;


public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider; // WheelCollider
    public Transform wheelMesh; // Визуал колеса
    public CapsuleCollider capsuleCollider; // Физический коллайдер (Capsule)
    
    private LayerMask groundLayer;
    private ChunkDeformerManager[] chunkDeformers;

    void Start()
    {
        groundLayer = LayerMask.GetMask("TerrainChunk");
        
        // Один раз собираем все ChunkDeformerManager в сцене
        chunkDeformers = Object.FindObjectsByType<ChunkDeformerManager>(FindObjectsSortMode.None);
    }
   
    void LateUpdate()
    {
        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);

        // Обновляем визуал
        wheelMesh.position = pos;
        wheelMesh.rotation = rot;

        // Обновляем collider для коллизий
        capsuleCollider.transform.position = pos;
        capsuleCollider.transform.rotation = rot;
    }

    void FixedUpdate()
    {
        if (Physics.Raycast(wheelMesh.position, Vector3.down, out RaycastHit hit, 5f, groundLayer))
        {
            foreach (var chunk in chunkDeformers)
            {
                chunk.DeformAtWorldPoint(hit.point);
            }
        }
    }
    
    private void OnEnable()
    {
        ChunkedTerrainGenerator.OnChunksRegenerated += RefreshDeformerList;
        RefreshDeformerList(); // при запуске сцены
    }

    private void OnDisable()
    {
        ChunkedTerrainGenerator.OnChunksRegenerated -= RefreshDeformerList;
    }

    private void RefreshDeformerList()
    {
        chunkDeformers = FindObjectsByType<ChunkDeformerManager>(FindObjectsSortMode.None);
    }

}