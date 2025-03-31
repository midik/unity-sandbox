using UnityEngine;

[System.Serializable] // To allow editing in Inspector when nested in Driveable
public class Engine
{
    [Tooltip("Кривая зависимости крутящего момента (Y-ось, Ньютон-метры) от оборотов в минуту (X-ось, RPM)")]
    public AnimationCurve torqueCurve = new AnimationCurve(new Keyframe(0, 30), new Keyframe(1000, 200), new Keyframe(5000, 250), new Keyframe(8000, 200), new Keyframe(9000, 0)); // Example curve

    [Tooltip("Обороты холостого хода")]
    public float idleRPM = 600f;

    [Tooltip("Максимальные обороты (ограничитель)")]
    public float maxRPM = 3500; // Используем значение из твоего файла

    [Tooltip("Инерция двигателя (влияет на скорость набора/сброса оборотов при свободном вращении). Больше = медленнее.")]
    public float inertia = 0.15f; // kg*m^2 equivalent (tune this)

    [Tooltip("Коэффициент внутреннего трения/сопротивления двигателя (помогает оборотам падать к холостым)")]
    [Range(0f, 1f)]
    public float internalFrictionFactor = 0.05f; // Условный коэффициент

    [Tooltip("Фактор торможения двигателем (больше = сильнее торможение двигателем)")]
    [Range(0f, 5f)]
    public float engineBrakeFactor = 1.0f;

    public float CurrentRPM { get; private set; }
    private float generatedTorque;
    
    private float potentialPositiveTorque;
    private float engineResistanceTorque;
    private float lastNetTorque; // Сохраняем последний рассчитанный чистый момент


    public void Initialize()
    {
        CurrentRPM = idleRPM;
        generatedTorque = 0f;
    }

    // Обновляет состояние двигателя и возвращает МОМЕНТ, который двигатель МОЖЕТ выдать на ТЕКУЩИХ оборотах.
    // Если трансмиссия разомкнута, CurrentRPM устанавливается ИЗВНЕ через SetRPMFromLoad().
    // В этом методе мы его не трогаем в таком случае.
    public float UpdateAndCalculateTorque(float throttleInput, bool isDrivetrainConnected, float deltaTime)
    {
        throttleInput = Mathf.Clamp01(throttleInput);

        // --- ЛОГИКА ОБНОВЛЕНИЯ RPM ---
        
        // Трансмиссия разомкнута: двигатель вращается свободно
        if (!isDrivetrainConnected)
        {
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
                // Коэффициент (здесь 0.5f) определяет скорость возврата к холостым (нужно настроить).
                idleCorrectionTorque = (idleRPM - CurrentRPM) * inertia * 0.5f;
            }

            // Чистый момент = (момент от газа + коррекция к холостым) - трение
            float netTorque = potentialTorque + idleCorrectionTorque - frictionResistance;

            // Изменяем RPM на основе чистого момента и инерции
            CurrentRPM += (netTorque / inertia) * deltaTime;

            // Гарантируем, что обороты не упадут слишком низко (например, половина холостых) и не превысят максимум
            CurrentRPM = Mathf.Clamp(CurrentRPM, idleRPM * 0.5f, maxRPM);

             // Дополнительно плавно подтягиваем к idleRPM, если обороты ниже и газ отпущен
             if (CurrentRPM < idleRPM && throttleInput < 0.01f) {
                  // Множитель в Lerp (здесь 1.5f) определяет скорость подтягивания (настроить)
                  CurrentRPM = Mathf.Lerp(CurrentRPM, idleRPM, deltaTime * 1.5f);
             }
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
    public void SetRPMFromLoad(float loadCalculatedRPM)
    {
        // Просто устанавливаем RPM, полученный от колес, ограничивая его снизу нулем и сверху максимумом
        CurrentRPM = Mathf.Clamp(loadCalculatedRPM, 0, maxRPM);
    }

     // Public getter for the torque generated this frame (остался из прошлой версии, может быть полезен)
     public float GetGeneratedTorque()
     {
         return generatedTorque;
     }

    // Force set RPM (e.g., for initialization or stalling)
    public void SetRPM(float rpm)
    {
        CurrentRPM = Mathf.Clamp(rpm, 0, maxRPM);
    }
}