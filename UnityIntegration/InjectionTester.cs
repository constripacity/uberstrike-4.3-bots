using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection; // ADDED for Reflection

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
            Log("Attempting to spawn CLEAN test bot...");
            
            if (!IsPracticeMode)
            {
                Log("ERROR: Not in practice mode. Aborting spawn.");
                return;
            }

            try
            {
                // STRATEGY CHANGE: Do not clone LocalPlayer. Create fresh GameObject.
                
                // 1. Find a reference position (Player)
                Vector3 spawnOrigin = Vector3.zero;
                Quaternion spawnRot = Quaternion.identity;
                
                GameObject localPlayer = GameObject.Find("LocalPlayer");
                if (localPlayer == null) localPlayer = GameObject.FindWithTag("Player");
                
                if (localPlayer != null)
                {
                    spawnOrigin = localPlayer.transform.position + (localPlayer.transform.forward * 5.0f) + (Vector3.up * 2.0f);
                    spawnRot = Quaternion.LookRotation(localPlayer.transform.position - spawnOrigin); // Face player
                }
                else
                {
                    spawnOrigin = new Vector3(0, 10, 0); // Fallback
                }

                // 2. Create Clean Bot
                GameObject botObj = new GameObject("Bot_" + (BotCount + 1));
                botObj.transform.position = spawnOrigin;
                botObj.transform.rotation = spawnRot;
                
                // 3. Add Physics
                // CharacterController handles gravity and collision for us
                CharacterController cc = botObj.AddComponent<CharacterController>();
                cc.height = 2.0f;
                cc.radius = 0.5f;
                cc.center = Vector3.up * 1.0f;
                
                // 4. Add Visuals (Body)
                // STRATEGY: Try specific player mesh -> RemoteCharacter
                GameObject bodyPrefab = FindPrefab("Player0_TGPIG3"); 
                if (bodyPrefab == null) bodyPrefab = FindPrefab("RemoteCharacter");
                
                // IMPORTANT: Set Tag and Layer for Hit Detection
                botObj.tag = "Player";
                botObj.layer = 20; // Layer 20 = RemotePlayer (from F8 log)

                if (bodyPrefab != null)
                {
                    GameObject bodyClone = (GameObject)Instantiate(bodyPrefab);
                    bodyClone.transform.parent = botObj.transform;
                    bodyClone.transform.localPosition = Vector3.zero;
                    bodyClone.transform.localRotation = Quaternion.identity;
                    
                    // Unity 3.5/4 compatibility
                    bodyClone.active = true;
                    
                    // Recursive fix for visibility
                    // Keep Layer 20 for body parts so weapons hit them
                    SetLayerRecursively(bodyClone, 20); 
                    foreach(var r in bodyClone.GetComponentsInChildren<Renderer>(true)) {
                        r.enabled = true;
                    }
                    Log("Attached PREFAB: " + bodyPrefab.name);

                    // --- ATTEMPT TO DRESS ---
                    try {
                        Component botDecorator = bodyClone.GetComponent("AvatarDecorator");
                        if (botDecorator != null) {
                             // ... (Existing Reflection Code - Simplified for stability) ...
                             // Call SetSkinColor as a test
                             Type decType = botDecorator.GetType();
                             MethodInfo setSkin = decType.GetMethod("SetSkinColor");
                             if (setSkin != null) setSkin.Invoke(botDecorator, new object[] { Color.green });
                             
                             // Call UpdateLayers
                             MethodInfo upLayers = decType.GetMethod("UpdateLayers");
                             if (upLayers != null) upLayers.Invoke(botDecorator, null);
                        }
                    } catch {}
                }

                
                // 4b. Add Weapon Model (Clone from LocalPlayer)
                try {
                    Transform weaponRoot = localPlayer.transform.Find("CameraTarget/Weapons/Decorators");
                    if (weaponRoot != null && weaponRoot.childCount > 0)
                    {
                        // Collect all valid weapons
                        List<Transform> validWeapons = new List<Transform>();
                        foreach(Transform child in weaponRoot) {
                            if (child.name.Contains("Weapon")) validWeapons.Add(child);
                        }

                        if (validWeapons.Count > 0)
                        {
                            // Pick Random Weapon
                            Transform chosenWeapon = validWeapons[UnityEngine.Random.Range(0, validWeapons.Count)];
                            
                            GameObject gunClone = (GameObject)Instantiate(chosenWeapon.gameObject);
                            gunClone.transform.parent = botObj.transform;
                            // Tweaked offset for "holding" gun (approximate since we don't have IK)
                            gunClone.transform.localPosition = new Vector3(0.3f, 1.4f, 0.5f); 
                            gunClone.transform.localRotation = Quaternion.identity;
                            
                            SetLayerRecursively(gunClone, 0); 
                            Log("Attached weapon: " + chosenWeapon.name);
                        }
                    }
                } catch (Exception ex) {
                    Log("Could not attach weapon model: " + ex.Message);
                }
                
                // 5. Add AI Controller
                var controller = botObj.AddComponent<BotController>();
                
                // Disable initially so user can toggle with F12
                controller.enabled = false; 
                controller.Initialize();
                
                Log("Spawned CLEAN bot at " + spawnOrigin);
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during spawn: " + ex);
            }
        }

        GameObject FindPrefab(string name)
        {
            foreach (GameObject go in Resources.FindObjectsOfTypeAll(typeof(GameObject)))
            {
                if (go.name == name) return go;
            }
            return null;
        }

        void SetLayerRecursively(GameObject obj, int newLayer)
        {
            if (obj == null) return;
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                if (child == null) continue;
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }

        void ToggleAI()
        {
            var bots = UnityEngine.Object.FindObjectsOfType(typeof(BotController));
            bool newState = false;
            if (bots.Length > 0) newState = !((BotController)bots[0]).enabled; // Toggle based on first bot
            
            foreach (BotController b in bots)
            {
                b.enabled = newState;
            }
            Log("Toggled AI for " + bots.Length + " bots. State: " + newState);
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
