using UnityEngine;

[RequireComponent(typeof(Driveable))] // Звук зависит от управления
public class CarAudioController : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource engineAudioSource;

    [Header("Engine Sound Settings")]
    public float minPitch = 1f; // Минимальный Pitch на холостых
    public float maxPitch = 5f; // Максимальный Pitch на высоких оборотах
    public float pitchChangeSpeed = 10.0f; // Плавность изменения Pitch

    // Опционально: модуляция громкости
    public float minVolume = 0.2f;
    public float maxVolume = 1f;
    public float volumeChangeSpeed = 8.0f;

    // Компоненты машины
    private Driveable driveable;
    private Rigidbody rb;
    private WheelCollider[] wheels; // Все колеса

    // Текущие значения для плавности
    private float currentPitch = 1.0f;
    private float currentVolume = 0.6f;
    private float currentMotorInput = 0f; // Храним текущий газ


    void Start()
    {
        driveable = GetComponent<Driveable>();
        rb = GetComponent<Rigidbody>();

        // Найдем все WheelColliders в дочерних объектах
        wheels = GetComponentsInChildren<WheelCollider>();

        if (!engineAudioSource)
        {
            Debug.LogError("Engine AudioSource not assigned!", this);
            enabled = false; // Выключаем скрипт, если нет источника
            return;
        }

        // Устанавливаем начальные значения
        engineAudioSource.pitch = minPitch;
        engineAudioSource.volume = minVolume;
        currentPitch = minPitch;
        currentVolume = minVolume;
    }

    void Update()
    {
        if (!engineAudioSource) return;

        // --- Расчет целевого Pitch ---
        float averageRPM = 0f;
        foreach (var wheel in wheels)
        {
            averageRPM += Mathf.Max(0f, wheel.rpm); // Берем положительные обороты
        }

        averageRPM /= 4;

        // Нормализуем RPM (нужно подобрать макс. значение, например 3000)
        float targetPitchFactor = Mathf.InverseLerp(0f, 3000f, averageRPM);

        // Целевой Pitch зависит от оборотов/скорости и немного от газа
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, targetPitchFactor);
        targetPitch += driveable.throttleNormalized * 0.1f; // Немного повышаем Pitch под газом
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);

        // --- Расчет целевой Громкости ---
        // Громкость зависит от газа и немного от оборотов/скорости
        float targetVolume =
            Mathf.Lerp(minVolume, maxVolume, driveable.throttleNormalized); // Основная зависимость от газа
        targetVolume += targetPitchFactor * 0.1f; // Чуть громче на высоких оборотах
        targetVolume = Mathf.Clamp(targetVolume, minVolume, maxVolume);

        // --- Плавное изменение Pitch и Volume ---
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * pitchChangeSpeed);
        currentVolume = Mathf.Lerp(currentVolume, targetVolume, Time.deltaTime * volumeChangeSpeed);

        engineAudioSource.pitch = currentPitch;
        engineAudioSource.volume = currentVolume;
    }
}