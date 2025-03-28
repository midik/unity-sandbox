using System;
using TMPro;
using UnityEngine;

public class DayNightController : MonoBehaviour
{
    [Header("Time Settings")] 
    [Tooltip("Продолжительность полного игрового дня в реальных секундах")]
    public float secondsInFullDay = 120f; // 2 минуты на игровые сутки

    [Range(0f, 1f)]
    public float currentTimeOfDay = 0.25f; // Текущее время (0 = полночь, 0.25 = рассвет, 0.5 = полдень, 0.75 = закат)

    public float timeMultiplier = 1f; // Ускоритель времени

    [Header("UI")]
    public TextMeshProUGUI timeText; // Для стандартного UI Text

    [Header("Sun Settings")]
    public Light sunLight; // Сюда перетащить Directional Light солнца
    public Gradient sunColorGradient; // Цвет солнца в течение дня
    public AnimationCurve sunIntensityCurve; // Интенсивность солнца в течение дня

    private float sunInitialIntensity; // Сохраним начальную интенсивность

    void Start()
    {
        if (!sunLight)
        {
            Debug.LogError("Sun Light не назначен!", this);
            enabled = false;
            return;
        }

        sunInitialIntensity = sunLight.intensity; // Запоминаем начальную интенсивность
    }

    void Update()
    {
        UpdateTime();
        UpdateClockUI();
        UpdateSun();
    }

    void UpdateTime()
    {
        currentTimeOfDay += (Time.deltaTime / secondsInFullDay) * timeMultiplier;
        currentTimeOfDay = Mathf.Repeat(currentTimeOfDay, 1f);
    }

    void UpdateSun()
    {
        // --- Вращение Солнца ---
        // Вращаем солнце вокруг оси X (имитация движения с востока на запад)
        // Угол зависит от времени суток. -90 = восход, 0 = полдень, +90 = закат
        // 0.5 (полдень) * 360 = 180. 180 - 90 = 90 градусов (солнце сверху)
        // 0.25 (восход) * 360 = 90. 90 - 90 = 0 градусов (солнце на горизонте)
        // 0.75 (закат) * 360 = 270. 270 - 90 = 180 градусов (солнце на горизонте с другой стороны)
        float sunAngleX = currentTimeOfDay * 360f - 90f;
        // Можно добавить вращение по Y для имитации движения север-юг или просто задать фиксированный угол
        float sunAngleY = 170f; // Пример: солнце чуть смещено к югу
        sunLight.transform.localRotation = Quaternion.Euler(new Vector3(sunAngleX, sunAngleY, 0));

        // --- Интенсивность и Цвет Солнца ---
        // Используем кривую для интенсивности
        // Кривая должна быть настроена так, чтобы Y был 0 ночью и достигал пика (например, 1) днем
        float intensityMultiplier = sunIntensityCurve.Evaluate(currentTimeOfDay);

        // Используем градиент для цвета
        // Градиент должен показывать цвет солнца от восхода до заката
        Color currentColor = sunColorGradient.Evaluate(currentTimeOfDay);

        // Применяем интенсивность и цвет
        sunLight.intensity = sunInitialIntensity * intensityMultiplier;
        sunLight.color = currentColor;

        // Полностью выключаем свет ночью для оптимизации (если интенсивность почти 0)
        // Можно добавить порог
        if (intensityMultiplier <= 0.01f && sunLight.enabled)
        {
            sunLight.enabled = false;
        }
        else if (intensityMultiplier > 0.01f && !sunLight.enabled)
        {
            sunLight.enabled = true;
        }
    }

    void UpdateClockUI()
    {
        if (!timeText) return;

        TimeSpan gameTimeSpan = TimeSpan.FromHours(currentTimeOfDay * 24f);
        int gameHour = gameTimeSpan.Hours;
        int gameMinute = gameTimeSpan.Minutes;
        timeText.text = $"{gameHour:D2}:{gameMinute:D2}";
    }
}