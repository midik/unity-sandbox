using System;
using UnityEngine;

[RequireComponent(typeof(Driveable))] // Звук зависит от управления
public class CarAudioController : MonoBehaviour
{
    [Header("Audio Sources")]
    public AudioSource engineAudioSource;
    public AudioSource rollingAudioSource; // <-- Добавили

    [Header("Engine Sound Settings")]
    public float minPitch = 0.5f; // Минимальный Pitch на холостых
    public float maxPitch = 2.5f; // Максимальный Pitch на высоких оборотах
    public float pitchChangeSpeed = 12.0f; // Плавность изменения Pitch
    
    // Опционально: модуляция громкости
    public float minVolume = 0.8f;
    public float maxVolume = 1f;
    public float volumeChangeSpeed = 8.0f;
    
    [Header("Rolling Sound Settings")]
    [Tooltip("Мин. громкость звука качения")]
    public float rollingMinVolume = 0.1f;
    [Tooltip("Громкость звука качения при максимальной скорости")]
    public float rollingMaxVolume = 0.5f;
    [Tooltip("Скорость (км/ч) для макс. громкости качения")]
    // public float rollingMaxSpeed = 80f; 

    // Компоненты машины
    private Driveable driveable;
    private Rigidbody rb;
    private WheelCollider[] wheels; // Все колеса

    // Текущие значения для плавности
    private float currentPitch = 1.0f;
    private float currentVolume = 0.6f;
    // private float currentMotorInput = 0f; // Храним текущий газ


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
        EngineSound();
        RollingSound();
    }

    void EngineSound()
    {
        if (!engineAudioSource) return;

        engineAudioSource.mute = !driveable.engine.isRunning;

        // Нормализуем RPM
        float targetPitchFactor = Mathf.InverseLerp(0f, driveable.engine.maxRPM, driveable.engine.CurrentRPM);

        // Целевой Pitch зависит от оборотов/скорости и немного от газа
        float targetPitch = Mathf.Lerp(minPitch, maxPitch, targetPitchFactor);
        // targetPitch += driveable.throttleNormalized * 0.1f; // Немного повышаем Pitch под газом
        targetPitch = Mathf.Clamp(targetPitch, minPitch, maxPitch);

        // --- Расчет целевой Громкости ---
        // // Громкость зависит от газа и немного от оборотов/скорости
        // float targetVolume =
        //     Mathf.Lerp(minVolume, maxVolume, driveable.throttleNormalized); // Основная зависимость от газа
        // // targetVolume += targetPitchFactor * 0.1f; // Чуть громче на высоких оборотах
        // targetVolume = Mathf.Clamp(targetVolume, minVolume, maxVolume);

        // --- Плавное изменение Pitch и Volume ---
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, Time.deltaTime * pitchChangeSpeed);
        // currentVolume = Mathf.Lerp(currentVolume, targetVolume, Time.deltaTime * volumeChangeSpeed);
        currentVolume = maxVolume;

        engineAudioSource.pitch = currentPitch;
        engineAudioSource.volume = currentVolume;
    }

    // --- Звук Качения Колес ---
    void RollingSound()
    {
        if (!rollingAudioSource) return;
        
        float speed = rb.linearVelocity.magnitude * 3.6f;
        // Громкость зависит от скорости и от того, касается ли хоть одно колесо земли
        bool anyWheelGrounded = false;
        foreach (var wheel in wheels)
        {
            if (wheel.isGrounded)
            {
                anyWheelGrounded = true;
                break;
            }
        }

        // Целевая громкость: 0, если не на земле, иначе зависит от скорости
        float targetRollingVolume = 0f;
        if (anyWheelGrounded)
        {
            // targetRollingVolume = Mathf.Lerp(rollingMinVolume, rollingMaxVolume, Mathf.InverseLerp(0f, driveable.maxSpeed, speed));
            targetRollingVolume = Mathf.Lerp(rollingMinVolume, rollingMaxVolume, Mathf.InverseLerp(0f, 60f, speed));
        }

        // Плавно меняем громкость (можно использовать одну общую скорость volumeChangeSpeed)
        rollingAudioSource.volume = Mathf.Lerp(rollingAudioSource.volume, targetRollingVolume, Time.deltaTime * volumeChangeSpeed);

        // Можно также немного менять Pitch качения от скорости
        // rollingAudioSource.pitch = Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(0f, driveable.maxSpeed, speed));
        rollingAudioSource.pitch = Mathf.Lerp(0.8f, 1.2f, Mathf.InverseLerp(0f, 60f, speed));
    }
}