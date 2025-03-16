using TMPro;
using UnityEngine;

public class Logger : MonoBehaviour
{
    public TextMeshProUGUI logText;
    
    public void Log(string message)
    {
        logText.text += message + "\n";
    }
}
