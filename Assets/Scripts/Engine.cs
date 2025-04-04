using JetBrains.Annotations;
using UnityEngine;

[System.Serializable] // To allow editing in Inspector when nested in Driveable
public class Engine
{
    [Tooltip("Кривая зависимости крутящего момента (Y-ось, Ньютон-метры) от оборотов в минуту (X-ось, RPM)")]
    public AnimationCurve torqueCurve;

    [Tooltip("Обороты холостого хода")] public float idleRPM = 600f;

    [Tooltip("Обороты, при которых двигатель глохнет")]
    public float stallRPM = 400f;

    [Tooltip("Максимальные обороты (ограничитель)")]
    public float maxRPM = 3500;

    [Tooltip( "Инерция двигателя (влияет на скорость набора/сброса оборотов при свободном вращении). Больше = медленнее.")]
    public float inertia = 0.15f; // kg*m^2 equivalent (tune this)

    [Tooltip("Коэффициент внутреннего трения/сопротивления двигателя (помогает оборотам падать к холостым)")]
    [Range(0f, 1f)]
    public float internalFrictionFactor = 0.05f; // Условный коэффициент

    [Tooltip("Фактор торможения двигателем (больше = сильнее торможение двигателем)")] [Range(0f, 5f)]
    public float engineBrakeFactor = 1.0f;

    public float CurrentRPM { get; private set; }
    public bool isRunning { get; private set; }

    private float generatedTorque;
    private float potentialPositiveTorque;
    private float engineResistanceTorque;
    private float lastNetTorque; // Сохраняем последний рассчитанный чистый момент


    public void Initialize()
    {
        StartEngine();
    }

    // Обновляет состояние двигателя и возвращает МОМЕНТ, который двигатель МОЖЕТ выдать на ТЕКУЩИХ оборотах.
    // Если трансмиссия разомкнута, CurrentRPM устанавливается ИЗВНЕ через SetRPMFromLoad().
    // В этом методе мы его не трогаем в таком случае.
    public float UpdateAndCalculateTorque(float throttleInput, float deltaTime, float clutchFactor, float externalRPM)
    {
        if (!isRunning)
        {
            // Если двигатель не запущен, возвращаем 0 момент
            generatedTorque = 0f;
            potentialPositiveTorque = 0f;
            engineResistanceTorque = 0f;
            lastNetTorque = 0f;
            return 0f;
        }
        
        throttleInput = Mathf.Clamp01(throttleInput);
        

        // --- ЛОГИКА ОБНОВЛЕНИЯ RPM ---

        // Рассчитываем потенциальный момент от нажатия газа на текущих оборотах
        float potentialTorque = torqueCurve.Evaluate(CurrentRPM) * throttleInput;

        // Рассчитываем момент сопротивления (трение + стремление к холостым)
        // Трение пропорционально оборотам и инерции (для масштаба)
        float frictionResistance = CurrentRPM * inertia * internalFrictionFactor;

        float idleCorrectionTorque = 0f;
        // Если газ не нажат, добавляем "силу", тянущую к idleRPM
        if (throttleInput < 0.01f)
        {
            // Сила пропорциональна отклонению от холостых.
            idleCorrectionTorque = (idleRPM - CurrentRPM) * inertia;
        }

        // Чистый момент = (момент от газа + коррекция к холостым) - трение
        float netTorque = potentialTorque + idleCorrectionTorque - frictionResistance;

        // Изменяем RPM на основе чистого момента и инерции
        CurrentRPM += (netTorque / inertia) * deltaTime;

        CurrentRPM = Mathf.Clamp(CurrentRPM, idleRPM * 0.5f, maxRPM);
            
        // Подтягиваем с целевыми оборотами от колес по фактору сцепления
        CurrentRPM = Mathf.Lerp(CurrentRPM, externalRPM, clutchFactor);
        
        // Если обороты упали слишком низко, двигатель глохнет
        if (CurrentRPM <= stallRPM)
        {
            StallEngine($"Free RPM = {CurrentRPM}");
            return 0f;
        }
            
        // Если обороты превышают максимум, ограничиваем их
        if (CurrentRPM > maxRPM)
        {
            CurrentRPM = maxRPM;
        }

        // --- РАСЧЕТ ВЫХОДНОГО МОМЕНТА ---
        // Рассчитываем момент, который двигатель может выдать на текущих (уже установленных) оборотах
        potentialPositiveTorque = torqueCurve.Evaluate(CurrentRPM);
        engineResistanceTorque = CurrentRPM * internalFrictionFactor * engineBrakeFactor;

        // Рассчитываем чистый момент
        lastNetTorque = (potentialPositiveTorque * throttleInput) - engineResistanceTorque;

        // Debug.Log($"[{Time.frameCount}] Engine Update: RPM={CurrentRPM:F0}, throttle={throttleInput:F2}, clutch={clutchEngaged}, potentialTorque={potentialPositiveTorque:F1}, resistance={engineResistanceTorque:F1}, NET TORQUE={lastNetTorque:F1}");

        return lastNetTorque;
    }

    // Метод для ВНЕШНЕЙ установки RPM (когда сцепление включено)
    public void SetRPM(float rpm)
    {
        // Если обороты упали слишком низко, двигатель глохнет
        if (CurrentRPM <= stallRPM)
        {
            StallEngine($"Load RPM = {CurrentRPM}");
            return;
        }

        // Просто устанавливаем RPM, полученный от колес, ограничивая его снизу нулем и сверху максимумом
        CurrentRPM = Mathf.Clamp(rpm, 0, maxRPM);
    }

    public void StartEngine()
    {
        isRunning = true;
        CurrentRPM = idleRPM;
        Debug.Log("Engine started.");
    }

    public void StopEngine()
    {
        isRunning = false;
        CurrentRPM = 0f;
        generatedTorque = 0f;
        potentialPositiveTorque = 0f;
        Debug.Log("Engine stopped.");
    }
    
    public void StallEngine(string reason = "")
    {
        isRunning = false;
        CurrentRPM = 0f;
        generatedTorque = 0f;
        potentialPositiveTorque = 0f;
        Debug.Log($"Engine stalled! ({reason})");
    }
}