using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class Logger : MonoBehaviour
{
    private Stack<string> log = new Stack<string>();
    private TextMeshProUGUI logText;

    
    public void Start()
    {
        logText = GetComponent<TextMeshProUGUI>();
    }

    public void Log(string message)
    {
        string currentTime = System.DateTime.Now.ToString("HH:mm:ss");
        log.Push($"[{currentTime}] {message}");
        logText.text = string.Join("\n", log.Take(16).Reverse().ToArray());
        
        // write to file
        System.IO.File.AppendAllText("log.txt", $"{currentTime} {message}\n");
    }
}
