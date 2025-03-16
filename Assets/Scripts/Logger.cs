using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class Logger : MonoBehaviour
{
    public TextMeshProUGUI logText;
    
    // stack for logs
    private Stack<string> log = new Stack<string>();
    
    public void Log(string message)
    {
        string currentTime = System.DateTime.Now.ToString("HH:mm:ss");
        log.Push($"[{currentTime}] {message}");
        logText.text = string.Join("\n", log.TakeLast(16).Reverse().ToArray());
    }
}
