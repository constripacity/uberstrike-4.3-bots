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
            if (Input.GetKeyDown(KeyCode.F6)) ProbeUI();
            
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
                
                // 3. Add Physics - MANUAL MODE
                // Replace CharacterController with simple Collider for taking damage
                SphereCollider col = botObj.AddComponent<SphereCollider>();
                col.center = Vector3.up * 1.0f;
                col.radius = 0.6f;
                col.isTrigger = false; // Solid collider so bullets hit it!
                
                // Add a Rigidbody (Kinematic) so Unity registers it as a "moving object"
                Rigidbody rb = botObj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                
                // 3b. Physics Cleanup: Disable conflicting child colliders
                foreach (Collider c in botObj.GetComponentsInChildren<Collider>())
                {
                    if (c != col) // Don't disable the main collider
                    {
                        c.isTrigger = true; // Turn off solid collision for limbs
                    }
                }

                // 4. Add Visuals (Body)
                // STRATEGY: Try specific player mesh -> RemoteCharacter
                GameObject bodyPrefab = FindPrefab("Player0_TGPIG3"); 
                if (bodyPrefab == null) bodyPrefab = FindPrefab("RemoteCharacter");
                
                // Revert to Default Layer so bullets can hit it.
                // Our BotController gravity logic now specifically ignores this layer.
                botObj.layer = 0; 
                botObj.tag = "Player";

                if (bodyPrefab != null)
                {
                    GameObject bodyClone = (GameObject)Instantiate(bodyPrefab);
                    bodyClone.transform.parent = botObj.transform;
                    bodyClone.transform.localPosition = Vector3.zero;
                    // Fix twisted back (Moonwalking) - REMOVED 180, set to IDENTITY
                    bodyClone.transform.localRotation = Quaternion.identity; 
                    
                    // Unity 3.5/4 compatibility
                    bodyClone.active = true;
                    
                    // Recursive fix for visibility
                    // Keep Layer 20 for body parts so weapons hit them (Damage)
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
                             if ((object)setSkin != null) setSkin.Invoke(botDecorator, new object[] { Color.green });
                             
                             // Call UpdateLayers
                             MethodInfo upLayers = decType.GetMethod("UpdateLayers");
                             if ((object)upLayers != null) upLayers.Invoke(botDecorator, null);
                        }
                    } catch {}
                }

                
                // 4b. Clean existing weapons on the body
                foreach (var renderer in botObj.GetComponentsInChildren<Renderer>())
                {
                    if (renderer.name.Contains("Weapon") || renderer.transform.parent.name.Contains("Weapon"))
                    {
                        renderer.enabled = false; // Hide default weapons
                    }
                }

                // 4c. Add Weapon Model (Clone from LocalPlayer)
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
                            // Try to find Right Hand bone
                            Transform handBone = null;
                            foreach(Transform t in botObj.GetComponentsInChildren<Transform>()) {
                                if(t.name.Contains("RightHand") || t.name.Contains("Right Hand") || t.name == "Bone028") { // Bone028 is common standard biped
                                    handBone = t;
                                    break;
                                }
                            }

                            if (handBone != null) {
                                // CLEANUP: Remove existing weapons/items in hand
                                foreach(Transform child in handBone) {
                                    Destroy(child.gameObject);
                                }

                                gunClone.transform.parent = handBone;
                                gunClone.transform.localPosition = Vector3.zero;
                                // Fix 90 degree twist. Trial & Error: Usually 90, 180, or 270 on Y axis.
                                gunClone.transform.localRotation = Quaternion.Euler(0, 90, 0); 
                            } else {
                                gunClone.transform.parent = botObj.transform;
                                gunClone.transform.localPosition = new Vector3(0.2f, 1.4f, 0.4f); 
                                gunClone.transform.localRotation = Quaternion.identity;
                            }
                            
                            SetLayerRecursively(gunClone, 0); 
                            Log("Attached weapon: " + chosenWeapon.name);
                        }
                    }
                } catch (Exception ex) {
                    Log("Could not attach weapon model: " + ex.Message);
                }

                // FIX PINK TEXTURE: Copy materials from Player
                try {
                     if (localPlayer != null) {
                        Renderer playerRend = localPlayer.GetComponentInChildren<Renderer>();
                        Renderer botRend = botObj.GetComponentInChildren<Renderer>();
                        if (playerRend != null && botRend != null) {
                             // Copy the shader at least, if not the whole material
                             botRend.material.shader = playerRend.material.shader;
                             Log("Applied Player Shader to Bot");
                        }
                     }
                } catch {}
                
                // 5. Add AI Controller
                var controller = botObj.AddComponent<BotController>();
                
                // CRITICAL FIX: Enable AI immediately (was disabled before)
                controller.enabled = true;
                controller.Initialize();
                
                Debug.Log("[InjectionTester] Bot AI ENABLED for " + controller.BotName);
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
            if (bots.Length == 0)
            {
                Log("No bots found to toggle!");
                return;
            }
            
            bool newState = !((BotController)bots[0]).enabled; // Toggle based on first bot
            
            foreach (BotController b in bots)
            {
                b.enabled = newState;
            }
            
            string stateStr = newState ? "ENABLED (ON)" : "DISABLED (OFF)";
            Log("=== AI TOGGLED === " + bots.Length + " bots are now " + stateStr);
            Log("Press F2 again to toggle AI " + (newState ? "OFF" : "ON"));
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

        void ProbeUI()
        {
            Log("--- UI PROBE ---");
            try {
                // 1. Scan Scene Objects
                foreach(MonoBehaviour m in FindObjectsOfType(typeof(MonoBehaviour))) {
                    string n = m.name.ToLower();
                    if (n.Contains("hud") || n.Contains("chat") || n.Contains("ingame") || n.Contains("message") || n.Contains("feed") || n.Contains("page")) {
                        Log("Found UI Candidate: " + m.name + " (" + m.GetType().Name + ")");
                        foreach(var method in m.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                            Log(" -> " + method.Name);
                        }
                    }
                }

                // 2. Scan GameState
                var gs = Type.GetType("GameState, Assembly-CSharp");
                if ((object)gs != null) {
                    var current = gs.GetProperty("Current").GetValue(null, null);
                    if (current != null) {
                        Log("GameState.Current found.");
                        foreach(var method in current.GetType().GetMethods()) {
                            if (method.Name.Contains("Message") || method.Name.Contains("Chat") || method.Name.Contains("Kill") || method.Name.Contains("Event"))
                                Log(" -> GS Method: " + method.Name);
                        }
                    }
                }
            } catch (Exception ex) {
                Log("Probe Error: " + ex.Message);
            }
            Log("--- END PROBE ---");
        }
    }
}
