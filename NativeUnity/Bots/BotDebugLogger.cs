using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Auto-captures Unity console output to a log file for bot debugging.
/// Logs are written to: [ProjectRoot]/BotDebugLog.txt
/// Auto-initializes via [RuntimeInitializeOnLoadMethod].
/// Captures: all Debug.Log/LogWarning/LogError messages.
/// File is overwritten each session (keeps last session only).
/// </summary>
public class BotDebugLogger : MonoBehaviour
{
    private static BotDebugLogger _instance;
    private static string _logPath;
    private static StreamWriter _writer;
    private static readonly object _lock = new object();
    private static int _lineCount;
    private static string _currentScene = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        // Write log next to the Assets folder (project root)
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        _logPath = Path.Combine(projectRoot, "BotDebugLog.txt");

        try
        {
            // Overwrite previous session log
            _writer = new StreamWriter(_logPath, false);
            _writer.AutoFlush = true;
            _writer.WriteLine("=== Bot Debug Log ===");
            _writer.WriteLine("Session: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _writer.WriteLine("Unity: " + Application.unityVersion);
            _writer.WriteLine("Platform: " + Application.platform);
            _writer.WriteLine(new string('=', 60));
            _writer.WriteLine();
            _lineCount = 5;
        }
        catch (Exception e)
        {
            Debug.LogError("[BotDebugLogger] Failed to create log file: " + e.Message);
            return;
        }

        Application.logMessageReceived += OnLogMessage;
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Create persistent GameObject
        var go = new GameObject("BotDebugLogger");
        _instance = go.AddComponent<BotDebugLogger>();
        DontDestroyOnLoad(go);
    }

    void OnDestroy()
    {
        if (_instance == this)
        {
            Application.logMessageReceived -= OnLogMessage;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            WriteFooter();
            if (_writer != null)
            {
                _writer.Close();
                _writer = null;
            }
            _instance = null;
        }
    }

    void OnApplicationQuit()
    {
        WriteFooter();
        if (_writer != null)
        {
            _writer.Close();
            _writer = null;
        }
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_writer == null) return;
        lock (_lock)
        {
            _currentScene = scene.name;
            _writer.WriteLine();
            _writer.WriteLine(">>> SCENE LOADED: " + scene.name + " (mode=" + mode + ") at " + Time.time.ToString("F2") + "s");
            _writer.WriteLine();
            _lineCount += 3;
        }
    }

    private static void OnLogMessage(string message, string stackTrace, LogType type)
    {
        if (_writer == null) return;
        lock (_lock)
        {
            string prefix;
            switch (type)
            {
                case LogType.Error:
                    prefix = "[ERROR]";
                    break;
                case LogType.Exception:
                    prefix = "[EXCEPTION]";
                    break;
                case LogType.Warning:
                    prefix = "[WARN]";
                    break;
                default:
                    prefix = "[LOG]";
                    break;
            }

            string timestamp = Time.time.ToString("F2").PadLeft(8);
            _writer.WriteLine(timestamp + "s " + prefix + " " + message);
            _lineCount++;

            // Include stack trace for errors and exceptions
            if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
            {
                _writer.WriteLine("  Stack: " + stackTrace.Replace("\n", "\n  "));
                _lineCount++;
            }
        }
    }

    private static void WriteFooter()
    {
        if (_writer == null) return;
        lock (_lock)
        {
            _writer.WriteLine();
            _writer.WriteLine(new string('=', 60));
            _writer.WriteLine("Session ended: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _writer.WriteLine("Total lines: " + _lineCount);
            _writer.WriteLine("Last scene: " + _currentScene);

            // Write bot summary
            try
            {
                _writer.WriteLine("Bots alive: " + BotController.AllBots.Count);
                _writer.WriteLine("Total bot kills: " + BotController.TotalBotKills);
            }
            catch { }
        }
    }
}
