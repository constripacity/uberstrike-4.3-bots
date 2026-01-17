using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UberStrikeBot
{
    /// <summary>
    /// Test Harness for validating Bot injection, spawning, and logic in Phase 1.
    /// Provides immediate visual feedback via OnGUI and keyboard shortcuts for testing.
    /// </summary>
    public class InjectionTester : MonoBehaviour
    {
        private bool _showGui = true;
        private string _logPath;
        private StringBuilder _logBuffer = new StringBuilder();
        
        // Status Flags
        public bool IsPracticeMode { get; private set; }
        public int BotCount { get; private set; }
        public float FPS { get; private set; }
        
        // GUI
        private Rect _windowRect = new Rect(10, 10, 400, 500);
        
        void Awake()
        {
            _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "UberStrikeBotLog.txt");
            Log("InjectionTester Initialized.");
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            // Initial check
            CheckEnvironment();
        }

        void Update()
        {
            // FPS Calculation
            FPS = 1.0f / Time.deltaTime;

            // Controls
            if (Input.GetKeyDown(KeyCode.F1)) SpawnTestBot();
            if (Input.GetKeyDown(KeyCode.F2)) ToggleAI();
            if (Input.GetKeyDown(KeyCode.F3)) _showGui = !_showGui;
            if (Input.GetKeyDown(KeyCode.F4)) RunDiagnostics();
            
            // Continuous Checks
            if (Time.frameCount % 60 == 0) // Every ~1 sec
            {
                var bots = UnityEngine.Object.FindObjectsOfType(typeof(BotController));
                BotCount = bots.Length;
                CheckEnvironment();
            }
        }

        void CheckEnvironment()
        {
            // Simple heuristic for Practice Mode: Check for expected managers
            var detector = (PracticeModeDetector)UnityEngine.Object.FindObjectOfType(typeof(PracticeModeDetector));
            if (detector != null)
            {
                IsPracticeMode = detector.IsPracticeMode;
            }
            else
            {
                // Fallback: Check if we are offline (UberStrike usually has 'GameState' or similar)
                // For now, assume true if we are injected in this test phase
                IsPracticeMode = true; 
            }
        }

        void OnGUI()
        {
            if (!_showGui) return;

            _windowRect = GUI.Window(0, _windowRect, DrawWindow, "UberStrike Bot - Injection Tester");
        }

        void DrawWindow(int windowID)
        {
            GUILayout.BeginVertical();

            // Status Section
            GUILayout.Label("Status", GUI.skin.box);
            GUILayout.Label("FPS: " + FPS.ToString("F1"));
            GUILayout.Label("Mode: " + (IsPracticeMode ? "Practice/Offline" : "Online/Unknown"));
            GUILayout.Label("Active Bots: " + BotCount);
            GUILayout.Label("Time: " + Time.time.ToString("F1") + "s");

            GUILayout.Space(10);
            
            // Controls Section
            GUILayout.Label("Controls", GUI.skin.box);
            GUILayout.Label("F1: Spawn Test Bot");
            GUILayout.Label("F2: Toggle AI (All Bots)");
            GUILayout.Label("F3: Toggle HUD");
            GUILayout.Label("F4: Run Diagnostics");

            GUILayout.Space(10);

            // Actions
            if (GUILayout.Button("Force Spawn Bot")) SpawnTestBot();
            if (GUILayout.Button("Clear Logs")) { _logBuffer.Length = 0; File.WriteAllText(_logPath, ""); }

            GUILayout.Space(10);

            // Log View
            GUILayout.Label("Recent Logs:", GUI.skin.box);
            GUILayout.TextArea(_logBuffer.ToString(), GUILayout.Height(150));

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        public void Log(string message)
        {
            string line = string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, message);
            
            // Console
            Debug.Log("[InjectionTester] " + message);
            
            // HUD Buffer
            if (_logBuffer.Length > 2000) _logBuffer.Remove(0, _logBuffer.Length - 2000);
            _logBuffer.AppendLine(line);
            
            // File
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to write log: " + ex.Message);
            }
        }

        void SpawnTestBot()
        {
            Log("Attempting to spawn test bot...");
            
            if (!IsPracticeMode)
            {
                Log("ERROR: Not in practice mode. Aborting spawn.");
                return;
            }

            try
            {
                // Verify we have a prefab or can clone an existing player
                // In UberStrike, usually we want to clone the local player or load a resource
                // For Phase 1, we might just look for ANY player object and clone it
                GameObject template = GameObject.FindWithTag("Player");
                if (template == null)
                {
                     // Fallback reflection
                     template = GameObject.Find("LocalPlayer");
                }

                if (template != null)
                {
                    GameObject botObj = (GameObject)Instantiate(template, template.transform.position + Vector3.right * 2, Quaternion.identity);
                    botObj.name = "Bot_" + (BotCount + 1);
                    
                    // Cleanup inputs on the clone
                    foreach (var comp in botObj.GetComponents<MonoBehaviour>())
                    {
                        // Disable known input scripts
                        if (comp.GetType().Name.Contains("Input") || comp.GetType().Name.Contains("Controller"))
                        {
                            comp.enabled = false;
                        }
                    }

                    // Add BotController
                    var controller = botObj.AddComponent<BotController>();
                    controller.Initialize();
                    Log("Spawned " + botObj.name + " successfully.");
                }
                else
                {
                    Log("ERROR: No player template found to clone.");
                }
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during spawn: " + ex);
            }
        }

        void ToggleAI()
        {
            var bots = UnityEngine.Object.FindObjectsOfType(typeof(BotController));
            foreach (BotController b in bots)
            {
                b.enabled = !b.enabled;
            }
            Log("Toggled AI for " + bots.Length + " bots.");
        }

        void RunDiagnostics()
        {
            Log("Running Diagnostics...");
            Log("Unity Version: " + Application.unityVersion);
            Log("Platform: " + Application.platform);
            Log("Level: " + Application.loadedLevelName);
            
            var players = GameObject.FindGameObjectsWithTag("Player");
            Log("Players found (Tag): " + players.Length);
            
            // Check Components
            if (players.Length > 0)
            {
                var p = players[0];
                Log("Player 0 Name: " + p.name);
                Log("Player 0 Components: ");
                foreach(var c in p.GetComponents<Component>())
                {
                    Log(" - " + c.GetType().Name);
                }
            }
        }
    }
}