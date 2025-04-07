using UnityEngine;

[System.Serializable]
public class Gearbox
{
    [Tooltip("Передаточные числа. Индекс 0=Задняя(R), 1=Нейтраль(N), 2=1-я(D1), 3=2-я(D2) и т.д.")]
    public float[] gearRatios = { -2.9f, 0f, 3.6f, 2.1f, 1.4f, 1.0f, 0.7f }; // Example: R, N, 1st, 2nd, 3rd, 4th, 5th

    [Tooltip("Передаточное число главной пары (дифференциала)")]
    public float finalDriveRatio = 18f; // Используем значение из твоего файла

    [Tooltip("Обороты двигателя для переключения ВВЕРХ")]
    public float upshiftRPM = 2500f; // Используем значение из твоего файла

    [Tooltip("Обороты двигателя для переключения ВНИЗ")]
    public float downshiftRPM = 1200f; // Используем значение из твоего файла

    [Tooltip("Минимальное время между переключениями передач (секунды)")]
    public float shiftDelay = 0.3f;

    public enum AutomaticMode { D, R, N, P };

    // State
    public bool IsAutomatic { get; private set; } = true; // Автоматическая коробка передач (true) или механическая (false)

    public AutomaticMode CurrentAutomaticMode { get; private set; } = AutomaticMode.N;
    
    public int CurrentGearIndex { get; private set; } = 2; // Start in 1st gear (index 2)

    public float CurrentGearRatio => (CurrentGearIndex >= 0 && CurrentGearIndex < gearRatios.Length)
        ? gearRatios[CurrentGearIndex]
        : 0f;

    private float timeSinceLastShift = 0f;


    public void Initialize()
    {
        CurrentAutomaticMode = AutomaticMode.N;
        IsAutomatic = false;
        CurrentGearIndex = 1; // Start in Neutral (index 1)
        timeSinceLastShift = shiftDelay; // Allow immediate shift if needed
    }

    public bool UpdateGear(float engineRPM, float throttleInput, float vehicleSpeed, float moveYInput, float deltaTime)
    {
        if (!IsAutomatic) return false;
        
        timeSinceLastShift += deltaTime;
        // Если с последнего переключения прошло слишком мало времени, выходим
        if (timeSinceLastShift < shiftDelay) return false;

        int previousGear = CurrentGearIndex; // Сохраняем для проверки в конце

        // --- Автоматическое переключение передач ---
        if (CurrentAutomaticMode == AutomaticMode.D)
        {
            // Upshift
            bool canUpshift = CurrentGearIndex < gearRatios.Length - 1; // Проверяем, что есть куда переключаться вверх
            // Переключаемся, если: (Обороты > порога И газ нажат) ИЛИ (Обороты ЗНАЧИТЕЛЬНО > порога)

            bool shouldUpshift =
                (engineRPM >= upshiftRPM && throttleInput > 0.1f) || // Стандартное переключение при разгоне
                (engineRPM >=
                 upshiftRPM *
                 1.2f); // ИЛИ Переключение при очень высоких оборотах (даже без газа) - Настроить множитель 1.2f

            if (canUpshift && shouldUpshift)
            {
                CurrentGearIndex++; // Переключаемся вверх
            }
            
            // Downshift
            else // Если не переключились вверх, проверяем понижение
            {
                bool canDownshift = CurrentGearIndex > 2; // Не переключаемся автоматически ниже 1-й (индекс 2)
                
                bool shouldDownshift = engineRPM <= downshiftRPM; // && throttleInput < 0.3f;

                if (canDownshift && shouldDownshift)
                {
                    CurrentGearIndex--; // Переключаемся вниз
                }
            }
        }

        // Если передача реально изменилась, сбрасываем таймер задержки
        if (CurrentGearIndex != previousGear)
        {
            timeSinceLastShift = 0f;
            // Debug.Log(
            //     $"Shifted to gear index: {CurrentGearIndex} (Ratio: {CurrentGearRatio:F2}) at RPM: {engineRPM:F0}");
            return true;
        }

        return false;
    }
    
    internal void ToggleGearboxMode()
    {
        IsAutomatic = !IsAutomatic;
        CurrentAutomaticMode = AutomaticMode.N;
    }
    
    internal void GearUp()
    {
        if (IsAutomatic)
        {
            // P -> N -> R -> D
            if (CurrentAutomaticMode == AutomaticMode.D)
            {
                // nothing
            }
            else if (CurrentAutomaticMode == AutomaticMode.R)
            {
                CurrentAutomaticMode = AutomaticMode.D;
                CurrentGearIndex = 2;
            }
            else if (CurrentAutomaticMode == AutomaticMode.N)
            {
                CurrentAutomaticMode = AutomaticMode.R;
                CurrentGearIndex = 0;
            }
            else if (CurrentAutomaticMode == AutomaticMode.P)
            {
                CurrentAutomaticMode = AutomaticMode.N;
                CurrentGearIndex = 1;
            }

        }
        else
        {
            // Manual mode
            if (CurrentGearIndex < gearRatios.Length - 1)
            {
                CurrentGearIndex++;
            }
        }
    }
    
    internal void GearDown()
    {
        if (IsAutomatic)
        {
            // D -> R -> N -> P
            if (CurrentAutomaticMode == AutomaticMode.D)
            {
                CurrentAutomaticMode = AutomaticMode.R;
                CurrentGearIndex = 0;
            }
            else if (CurrentAutomaticMode == AutomaticMode.R)
            {
                CurrentAutomaticMode = AutomaticMode.N;
                CurrentGearIndex = 1;
            }
            else if (CurrentAutomaticMode == AutomaticMode.N)
            {
                CurrentAutomaticMode = AutomaticMode.P;
            }
            else if (CurrentAutomaticMode == AutomaticMode.P)
            {
                // nothing
            }
        }
        else
        {
            // Manual mode
            if (CurrentGearIndex > 0)
            {
                CurrentGearIndex--;
            }
        }
    }
    
    internal void SetGear(int gearIndex)
    {
        if (gearIndex < 0 || gearIndex >= gearRatios.Length)
        {
            // Debug.LogError($"Invalid gear index: {gearIndex}");
            return;
        }

        CurrentGearIndex = gearIndex;
    }
    
    internal void SetAutomaticMode(AutomaticMode mode)
    {
        CurrentAutomaticMode = mode;
        if (mode == AutomaticMode.D)
        {
            CurrentGearIndex = 2; // 1st gear
        }
        else if (mode == AutomaticMode.R)
        {
            CurrentGearIndex = 0; // Reverse
        }
        else if (mode == AutomaticMode.N)
        {
            CurrentGearIndex = 1; // Neutral
        }
    }
    
    public bool IsNeutral()
    {
        return CurrentGearIndex == 1;
    }

}