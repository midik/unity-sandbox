using UnityEngine;

[System.Serializable]
public class Clutch
{
    [Tooltip("Кривая включения сцепления. X=Обороты выше холостых (норм. 0..1), Y=Фактор сцепления (0..1)")]
    public AnimationCurve engagementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Tooltip("Кривая включения сцепления. X=Обороты выше холостых (норм. 0..1), Y=Фактор сцепления (0..1)")]
    public AnimationCurve slippingCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 1f);
    [Tooltip("Диапазон оборотов (выше холостых), на котором происходит включение сцепления от 0 до 1")]
    public float engageRPMRange = 1000f; // Например, сцепление полностью включится при idleRPM + 600 RPM

    [Header("Readouts (Read Only)")]
    [SerializeField, ReadOnly] protected float clutchFactor { get; private set; }
    [SerializeField, ReadOnly] protected float clutchSlippingFactor { get; private set; }

    private float idleRPM;


    public void Initialize(float idleRPM)
    {
        this.idleRPM = idleRPM;
        Reset();
    }

    public void UpdateClutchFactor(float currentRPM, int currentGearIndex)
    {
        // Бросаем сцепление, если не первая или задняя передача
        if (currentGearIndex != 0 && currentGearIndex != 2)
        {
            clutchFactor = 1.0f;
            clutchSlippingFactor = 1f;
        }
        else
        {
            float rpmAboveIdle = currentRPM - idleRPM;
            float normalizedEngageRPM = Mathf.Clamp01(rpmAboveIdle / engageRPMRange);
            clutchFactor = engagementCurve.Evaluate(normalizedEngageRPM);
            clutchSlippingFactor = slippingCurve.Evaluate(normalizedEngageRPM);
            // clutchSlippingFactor = Mathf.InverseLerp(idleRPM, idleRPM + engageRPMRange, currentRPM) / 2;
        }

        clutchFactor = Mathf.Clamp01(clutchFactor);
        clutchSlippingFactor = Mathf.Clamp01(clutchSlippingFactor);
    }

    public void Reset()
    {
        clutchFactor = 0f;
        clutchSlippingFactor = 0f;
    }
    
    public float GetClutchFactor() => clutchFactor;
    
    public float GetClutchSlippingFactor() => clutchSlippingFactor;
}