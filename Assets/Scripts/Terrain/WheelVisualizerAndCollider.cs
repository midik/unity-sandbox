using UnityEngine;
using System.Collections.Generic; // Нужен для HashSet

public class WheelVisualizerAndCollider : MonoBehaviour
{
    public WheelCollider wheelCollider;
    public Transform wheelMesh;
    public CapsuleCollider capsuleCollider;

    private LayerMask groundLayer;
    private float lastKnownDeformRadius = 0.3f; // Значение по умолчанию или последнее известное

    // Используем HashSet для хранения уже обработанных менеджеров в одном вызове FixedUpdate,
    // чтобы избежать случайной двойной деформации одного и того же чанка.
    private HashSet<ChunkDeformerManager> processedManagers = new HashSet<ChunkDeformerManager>();

    void Start()
    {
        groundLayer = LayerMask.GetMask("TerrainChunk");
        // Попробуем получить радиус из первого попавшегося менеджера при старте, если возможно
        // Это не самый надежный способ, но лучше, чем жестко заданное значение
        ChunkDeformerManager initialManager = Object.FindFirstObjectByType<ChunkDeformerManager>();
        if (initialManager != null && initialManager.TryGetComponent<MeshDeformer>(out MeshDeformer initialDeformer))
        {
             lastKnownDeformRadius = initialDeformer.deformRadius;
        }
    }

    void LateUpdate()
    {
        if (!wheelCollider || !wheelMesh || !capsuleCollider) return;

        wheelCollider.GetWorldPose(out Vector3 pos, out Quaternion rot);
        wheelMesh.position = pos;
        wheelMesh.rotation = rot;

        if (capsuleCollider && capsuleCollider.transform)
        {
            capsuleCollider.transform.position = pos;
            capsuleCollider.transform.rotation = rot;
        }
    }

    void FixedUpdate()
    {
        // Проверяем, есть ли под колесом земля
        if (Physics.Raycast(wheelMesh.position, Vector3.down, out RaycastHit hit, 1.0f, groundLayer))
        {
            // Попробуем получить актуальный радиус деформации из чанка, в который попали
            float currentDeformRadius = lastKnownDeformRadius;
            if (hit.collider.TryGetComponent<MeshDeformer>(out MeshDeformer hitDeformer))
            {
                currentDeformRadius = hitDeformer.deformRadius;
                lastKnownDeformRadius = currentDeformRadius; // Обновляем последнее известное значение
            }

            // Очищаем список обработанных менеджеров перед новым запросом
            processedManagers.Clear();

            // Находим все коллайдеры чанков в радиусе деформации вокруг точки контакта
            Collider[] collidersInRadius = Physics.OverlapSphere(hit.point, currentDeformRadius, groundLayer);

            // Перебираем найденные коллайдеры
            foreach (var col in collidersInRadius)
            {
                // Пытаемся получить ChunkDeformerManager
                if (col.TryGetComponent<ChunkDeformerManager>(out ChunkDeformerManager manager))
                {
                    // Проверяем, не обрабатывали ли мы этот менеджер уже в этом кадре
                    // Add возвращает true, если элемент был успешно добавлен (т.е. его не было)
                    if (processedManagers.Add(manager))
                    {
                        // Вызываем деформацию
                        manager.DeformAtWorldPoint(hit.point);
                    }
                }
            }
        }
    }
}