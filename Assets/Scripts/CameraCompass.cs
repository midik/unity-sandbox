using UnityEngine;

public class CameraCompass : MonoBehaviour
{
    public RectTransform compassArrow; // UI Image стрелка
    public Transform cameraTransform;  // Ссылка на Main Camera

    void Update()
    {
        // Получаем направление взгляда камеры по XZ (без высоты)
        Vector3 forward = cameraTransform.forward;
        forward.y = 0;
        forward.Normalize();

        // Угол относительно мировой оси Z
        float angle = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;

        // Вращаем стрелку
        compassArrow.localEulerAngles = new Vector3(0, 0, -angle);
    }
}
