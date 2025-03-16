using UnityEngine;
using TMPro;

public class SmartFollowCamera : MonoBehaviour
{
    public Transform target;
    public TextMeshProUGUI modeText; // UI текст для отображения режима

    [Header("Offsets")]
    public float distance = 8f;
    public float minDistance = 4f;
    public float maxDistance = 15f;
    public float height = 3f;
    public float minHeightAboveTarget = 1.5f;

    [Header("Smoothness")]
    public float smoothSpeed = 5f;
    public float lookSmoothSpeed = 5f;

    [Header("Rotation Control")]
    public float rotationSpeed = 70f;
    public LayerMask groundLayer;

    private Vector3 currentVelocity;
    private Vector3 currentLookTarget;

    private float yaw = 0f;
    private float pitch = 15f;

    private enum CameraMode { Follow, Spin, Free, Static }
    private CameraMode currentMode = CameraMode.Follow;

    void Start()
    {
        currentLookTarget = target.position + Vector3.up * 1f;
        yaw = target.eulerAngles.y;
        pitch = 15f;
        UpdateModeText();
    }

    void LateUpdate()
    {
        HandleInput();

        Vector3 finalPosition = Vector3.zero;

        // --- Позиция камеры по режиму ---
        switch (currentMode)
        {
            case CameraMode.Follow:
                finalPosition = CalculateFollowPosition();
                break;

            case CameraMode.Spin:
                finalPosition = CalculateSpinPosition(relativeToCar: true);
                break;

            case CameraMode.Free:
                finalPosition = CalculateSpinPosition(relativeToCar: false);
                break;

            case CameraMode.Static:
                finalPosition = transform.position; // Камера стоит на месте
                break;
        }

        // --- Проверяем препятствия (Raycast) ---
        Vector3 rayDirection = (finalPosition - target.position).normalized;
        float rayDistance = Vector3.Distance(target.position, finalPosition);
        if (Physics.Raycast(target.position, rayDirection, out RaycastHit hit, rayDistance, groundLayer))
        {
            finalPosition = hit.point + Vector3.up * minHeightAboveTarget;
        }

        // --- Минимальная высота ---
        if (finalPosition.y < target.position.y + minHeightAboveTarget)
            finalPosition.y = target.position.y + minHeightAboveTarget;

        // --- Плавное движение камеры ---
        transform.position = Vector3.SmoothDamp(transform.position, finalPosition, ref currentVelocity, 1f / smoothSpeed);

        // --- Плавный взгляд на машину ---
        Vector3 targetLookPosition = target.position + Vector3.up * 1f;
        currentLookTarget = Vector3.Lerp(currentLookTarget, targetLookPosition, Time.deltaTime * lookSmoothSpeed);
        transform.LookAt(currentLookTarget);
    }

    // =============== Режимы камеры ===============

    Vector3 CalculateFollowPosition()
    {
        Vector3 directionBehind = -target.forward;
        return target.position + directionBehind * distance + Vector3.up * height;
    }

    Vector3 CalculateSpinPosition(bool relativeToCar)
    {
        float finalYaw = yaw;
        if (relativeToCar) finalYaw += target.eulerAngles.y;

        Quaternion rotation = Quaternion.Euler(pitch, finalYaw, 0);
        Vector3 direction = rotation * Vector3.back;
        return target.position + direction * distance + Vector3.up * height;
    }

    // =============== Управление камерой ===============

    void HandleInput()
    {
        // Переключение режимов (по кнопке C)
        if (Input.GetKeyDown(KeyCode.C))
        {
            currentMode = (CameraMode)(((int)currentMode + 1) % 4);
            UpdateModeText();
        }

        // Управление мышкой (только в Spin и Free)
        if (currentMode == CameraMode.Spin || currentMode == CameraMode.Free)
        {
            if (Input.GetMouseButton(1)) // Правая кнопка мыши
            {
                yaw += Input.GetAxis("Mouse X") * rotationSpeed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * rotationSpeed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, 5f, 70f); // Ограничения по вертикали
            }
        }

        // Зум колесиком мыши
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance -= scroll * 5f;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }
    }

    // =============== UI текст ===============

    void UpdateModeText()
    {
        switch (currentMode)
        {
            case CameraMode.Follow:
                modeText.text = "Camera: FOLLOW";
                break;
            case CameraMode.Spin:
                modeText.text = "Camera: SPIN";
                break;
            case CameraMode.Free:
                modeText.text = "Camera: FREE";
                break;
            case CameraMode.Static:
                modeText.text = "Camera: STATIC";
                break;
        }
    }
    
    public void ResetToTarget()
    {
        currentLookTarget = target.position + Vector3.up * 1f;
        transform.position = currentLookTarget;
        transform.LookAt(currentLookTarget);
    }
}
