using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Reflection;

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

        // Spawn mode flags
        private bool _useNakedSpawn = false;  // F5 toggle
        private bool _useSafePosition = false; // F8 toggle
        private bool _useDelayedBody = false;  // F10 toggle

        void Update()
        {
            // FPS Calculation
            FPS = 1.0f / Time.deltaTime;

            // Controls
            if (Input.GetKeyDown(KeyCode.F1)) SpawnTestBot();
            if (Input.GetKeyDown(KeyCode.F2)) ToggleAI();
            if (Input.GetKeyDown(KeyCode.F3)) _showGui = !_showGui;
            if (Input.GetKeyDown(KeyCode.F4)) RunDiagnostics();
            if (Input.GetKeyDown(KeyCode.F5)) { _useNakedSpawn = !_useNakedSpawn; Log("Naked Spawn: " + (_useNakedSpawn ? "ON" : "OFF")); }
            if (Input.GetKeyDown(KeyCode.F6)) ProbeUI();
            if (Input.GetKeyDown(KeyCode.F8)) { _useSafePosition = !_useSafePosition; Log("Safe Position (sky): " + (_useSafePosition ? "ON" : "OFF")); }
            if (Input.GetKeyDown(KeyCode.F10)) { _useDelayedBody = !_useDelayedBody; Log("Delayed Body: " + (_useDelayedBody ? "ON" : "OFF")); }

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
            
            // Use GetInstanceID() to ensure unique window ID per instance (prevents flickering on re-inject)
            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "UberStrike Bot - Injection Tester");
        }

        void DrawWindow(int windowID)
        {
            // Safety check for layout phase
            if (Event.current.type == EventType.Layout) { } // Ensure we don't skip layout

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
            GUILayout.Label("F5: Naked Spawn [" + (_useNakedSpawn ? "ON" : "OFF") + "]");
            GUILayout.Label("F8: Safe Position [" + (_useSafePosition ? "ON" : "OFF") + "]");
            GUILayout.Label("F10: Delayed Body [" + (_useDelayedBody ? "ON" : "OFF") + "]");

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
            Log("=== SPAWN START === Naked:" + _useNakedSpawn + " SafePos:" + _useSafePosition + " DelayBody:" + _useDelayedBody);

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

                // SAFE POSITION: Spawn above ground level (lowered from 100 to 20)
                if (_useSafePosition)
                {
                    spawnOrigin = new Vector3(0, 20, 0);
                    Log("Using SAFE spawn position: " + spawnOrigin);
                }
                else if (localPlayer != null)
                {
                    spawnOrigin = localPlayer.transform.position + (localPlayer.transform.forward * 5.0f);
                    spawnOrigin.y += 0.5f; // Small offset above player ground
                    spawnRot = Quaternion.LookRotation(localPlayer.transform.position - spawnOrigin); // Face player
                }
                else
                {
                    spawnOrigin = new Vector3(0, 10, 0); // Fallback
                }

                Log("STEP 1: Creating GameObject at " + spawnOrigin);

                // 2. Create Clean Bot
                GameObject botObj = new GameObject("Bot_" + (BotCount + 1));
                botObj.transform.position = spawnOrigin;
                botObj.transform.rotation = spawnRot;

                Log("STEP 2: GameObject created: " + botObj.name + " (InstanceID: " + botObj.GetInstanceID() + ")");
                
                // 3. Add Physics - TRIGGER MODE (so bullets pass through, use raycast for hits)
                // NO solid collider - UberStrike uses raycast weapons, not physical projectiles
                // The collider is ONLY for trigger detection (jump pads, etc.)
                SphereCollider col = botObj.AddComponent<SphereCollider>();
                col.center = Vector3.up * 1.0f;
                col.radius = 0.6f;
                col.isTrigger = true; // TRIGGER - bullets are raycasts, not physical objects

                // Add a Rigidbody (Kinematic) for trigger detection
                Rigidbody rb = botObj.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;

                // 3b. Physics Cleanup: All child colliders should also be triggers
                foreach (Collider c in botObj.GetComponentsInChildren<Collider>())
                {
                    c.isTrigger = true;
                }

                Log("STEP 3: Adding physics components");

                // Put bot on Layer 8 (Player) - ground raycast excludes this layer
                // This prevents ground detection from hitting our own colliders
                botObj.layer = 8;
                // DON'T use "Player" tag - game might confuse bot with real player!
                botObj.tag = "Untagged";

                // 4. Add Visuals (Body) - DEPENDS ON SPAWN MODE
                if (_useNakedSpawn)
                {
                    // NAKED SPAWN: Large visible capsule, no prefab
                    Log("NAKED SPAWN: Creating LARGE visible capsule");
                    GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    placeholder.name = "BotBody_Capsule";
                    placeholder.transform.parent = botObj.transform;
                    placeholder.transform.localPosition = Vector3.up * 1.0f;
                    placeholder.transform.localScale = new Vector3(1.5f, 2.0f, 1.5f); // LARGER for visibility

                    // CRITICAL: Remove the capsule's built-in collider to prevent physics conflicts!
                    Collider capsuleCol = placeholder.GetComponent<Collider>();
                    if (capsuleCol != null) {
                        Destroy(capsuleCol);
                        Log("NAKED SPAWN: Removed capsule's built-in collider");
                    }

                    // Make it bright and unmissable
                    Renderer rend = placeholder.GetComponent<Renderer>();
                    if (rend != null) {
                        // Create a new material to avoid sharing
                        Material mat = new Material(Shader.Find("Diffuse"));
                        mat.color = new Color(0f, 1f, 1f, 1f); // Bright cyan
                        rend.material = mat;
                        rend.enabled = true;
                    }
                    placeholder.layer = 8; // Layer 8 (Player) - same as bot root

                    Log("NAKED SPAWN: Capsule created - Scale: " + placeholder.transform.localScale + ", Layer: " + placeholder.layer);
                }
                else if (_useDelayedBody)
                {
                    // DELAYED BODY: Create capsule first, attach body later
                    Log("DELAYED BODY: Creating temp capsule, body attaches in 2 seconds");
                    GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    placeholder.name = "TempBody";
                    placeholder.transform.parent = botObj.transform;
                    placeholder.transform.localPosition = Vector3.up * 1.0f;
                    placeholder.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
                    placeholder.layer = 8; // Layer 8 (Player) - same as bot root

                    Renderer rend = placeholder.GetComponent<Renderer>();
                    if (rend != null) {
                        rend.material.color = Color.yellow; // Yellow = waiting for body
                    }

                    // Start coroutine to attach body later
                    StartCoroutine(DelayedBodyAttach(botObj, localPlayer));
                }
                else
                {
                    // NORMAL SPAWN: Try to attach body prefab immediately
                    AttachBodyPrefab(botObj, localPlayer);
                }

                Log("STEP 4: Adding AI Controller");

                // 5. Add AI Controller
                BotController controller = botObj.AddComponent<BotController>();

                // CRITICAL FIX: Enable AI immediately (was disabled before)
                controller.enabled = true;
                controller.Initialize();

                Log("STEP 5: Bot AI ENABLED for " + controller.BotName);
                Log("=== SPAWN COMPLETE === Bot: " + botObj.name + " at " + spawnOrigin);

                // Start survival monitor
                StartCoroutine(MonitorBotSurvival(botObj, controller.BotName));
            }
            catch (Exception ex)
            {
                Log("EXCEPTION during spawn: " + ex);
            }
        }

        IEnumerator MonitorBotSurvival(GameObject bot, string botName)
        {
            Log("[Monitor] Started for " + botName);
            for (int i = 1; i <= 10; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (bot == null)
                {
                    Log("[Monitor] !!! BOT DESTROYED !!! " + botName + " died at tick " + i + " (after " + (i * 0.5f) + "s)");
                    yield break;
                }
                Log("[Monitor] " + botName + " ALIVE at tick " + i + " pos=" + bot.transform.position);
            }
            Log("[Monitor] " + botName + " survived 5 seconds - SUCCESS!");
        }

        void AttachBodyPrefab(GameObject botObj, GameObject localPlayer)
        {
            // STRATEGY: Try multiple prefab names
            GameObject bodyPrefab = FindPrefab("Player0_TGPIG3");
            if (bodyPrefab == null) bodyPrefab = FindPrefab("RemoteCharacter");
            if (bodyPrefab == null) bodyPrefab = FindPrefab("Character");
            if (bodyPrefab == null) bodyPrefab = FindPrefab("Avatar");

            Log("Body prefab search: " + (bodyPrefab != null ? bodyPrefab.name : "NOT FOUND!"));

            if (bodyPrefab != null)
            {
                GameObject bodyClone = (GameObject)Instantiate(bodyPrefab);
                bodyClone.transform.parent = botObj.transform;
                // NO OFFSET - let gravity code handle ground positioning
                // The prefab origin should be at feet level
                bodyClone.transform.localPosition = Vector3.zero;
                bodyClone.transform.localRotation = Quaternion.identity;

                // Unity 3.5/4 compatibility
                bodyClone.active = true;

                // DIAGNOSTIC: List ALL components on the body clone to find problematic ones
                Log("=== BODY CLONE COMPONENTS ===");
                int disabledCount = 0;
                foreach (Component comp in bodyClone.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    string compName = comp.GetType().Name;

                    // Disable potentially problematic scripts that might destroy or hide the bot
                    MonoBehaviour mb = comp as MonoBehaviour;
                    if (mb != null && compName != "Transform" && compName != "Animator" &&
                        compName != "SkinnedMeshRenderer" && compName != "MeshRenderer" &&
                        compName != "MeshFilter" && compName != "AvatarDecorator")
                    {
                        // Disable unknown MonoBehaviours that might cause issues
                        if (compName.Contains("Player") || compName.Contains("Character") ||
                            compName.Contains("Controller") || compName.Contains("Network") ||
                            compName.Contains("Remote") || compName.Contains("Sync") ||
                            compName.Contains("Destroy") || compName.Contains("Kill") ||
                            compName.Contains("Death") || compName.Contains("Cleanup"))
                        {
                            mb.enabled = false;
                            disabledCount++;
                            Log("DISABLED script: " + compName + " on " + comp.gameObject.name);
                        }
                    }
                }
                Log("Disabled " + disabledCount + " potentially dangerous scripts");

                // CRITICAL: Set body to Layer 8 (Player) so ground raycast doesn't hit it!
                // Ground raycast excludes Layer 8 to avoid hitting bot's own colliders
                SetLayerRecursively(bodyClone, 8);

                // Force enable renderers BUT hide ALL weapons aggressively
                int rendererCount = 0;
                int hiddenCount = 0;
                foreach(Renderer r in bodyClone.GetComponentsInChildren<Renderer>(true)) {
                    string objName = r.gameObject.name.ToLower();
                    // Also check parent names for weapon containers
                    string parentName = r.transform.parent != null ? r.transform.parent.name.ToLower() : "";
                    string fullPath = GetTransformPath(r.transform).ToLower();

                    // Hide ANY object that looks like a weapon (very aggressive)
                    bool isWeapon = objName.Contains("weapon") || objName.Contains("gun") ||
                        objName.Contains("rifle") || objName.Contains("pistol") ||
                        objName.Contains("knife") || objName.Contains("grenade") ||
                        objName.Contains("launcher") || objName.Contains("smg") ||
                        objName.Contains("shotgun") || objName.Contains("sniper") ||
                        objName.Contains("melee") || objName.Contains("cannon") ||
                        objName.Contains("splatter") || objName.Contains("magma") ||
                        objName.Contains("force") || objName.Contains("brand") ||
                        objName.Contains("primary") || objName.Contains("secondary") ||
                        objName.Contains("tertiary") || objName.Contains("item") ||
                        parentName.Contains("weapon") || parentName.Contains("decorator") ||
                        fullPath.Contains("weapon");

                    if (isWeapon)
                    {
                        r.enabled = false;
                        hiddenCount++;
                    }
                    else
                    {
                        r.enabled = true;
                        r.gameObject.layer = 8; // Same as bot root - Layer 8 (Player)
                        rendererCount++;
                    }
                }
                Log("Attached PREFAB: " + bodyPrefab.name + " - Visible: " + rendererCount + ", Hidden weapons: " + hiddenCount);

                // --- ATTEMPT TO DRESS ---
                try {
                    Component botDecorator = bodyClone.GetComponent("AvatarDecorator");
                    if (botDecorator != null) {
                         Type decType = botDecorator.GetType();
                         MethodInfo setSkin = decType.GetMethod("SetSkinColor");
                         if ((object)setSkin != null) setSkin.Invoke(botDecorator, new object[] { Color.green });

                         MethodInfo upLayers = decType.GetMethod("UpdateLayers");
                         if ((object)upLayers != null) upLayers.Invoke(botDecorator, null);
                         Log("Applied AvatarDecorator customization");
                    }
                } catch {}

                // DISABLED: Weapon attachment was causing duplicate weapons
                // Bots shoot via raycast, don't need visible weapons
                // AttachWeaponModel(botObj, localPlayer);
            }
            else
            {
                // FALLBACK: Create a simple visible capsule as placeholder
                Log("WARNING: No body prefab found! Creating placeholder capsule.");
                GameObject placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                placeholder.transform.parent = botObj.transform;
                placeholder.transform.localPosition = Vector3.up * 1.0f;
                placeholder.transform.localScale = new Vector3(0.5f, 1.0f, 0.5f);
                placeholder.layer = 8; // Layer 8 (Player) - same as bot root

                // Make it red so it's obvious
                Renderer rend = placeholder.GetComponent<Renderer>();
                if (rend != null) {
                    rend.material.color = Color.red;
                }
            }
        }

        void AttachWeaponModel(GameObject botObj, GameObject localPlayer)
        {
            if (localPlayer == null) return;

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
                            if(t.name.Contains("RightHand") || t.name.Contains("Right Hand") || t.name == "Bone028") {
                                handBone = t;
                                break;
                            }
                        }

                        if (handBone != null) {
                            gunClone.transform.parent = handBone;
                            gunClone.transform.localPosition = Vector3.zero;
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
        }

        IEnumerator DelayedBodyAttach(GameObject botObj, GameObject localPlayer)
        {
            Log("[DelayedBody] Waiting 2 seconds before attaching body...");

            // Wait 2 seconds
            yield return new WaitForSeconds(2.0f);

            // Check if bot still exists
            if (botObj == null)
            {
                Log("[DelayedBody] !!! BOT DIED BEFORE BODY ATTACH !!! This proves body prefab is NOT the cause!");
                yield break;
            }

            Log("[DelayedBody] Bot survived 2s! Attaching body prefab now...");

            // Remove temp body
            Transform tempBody = botObj.transform.Find("TempBody");
            if (tempBody != null)
            {
                Destroy(tempBody.gameObject);
            }

            // Attach real body
            AttachBodyPrefab(botObj, localPlayer);

            Log("[DelayedBody] Body attached! Monitoring survival...");

            // Monitor for 3 more seconds
            for (int i = 1; i <= 6; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (botObj == null)
                {
                    Log("[DelayedBody] !!! BOT DIED AFTER BODY ATTACH !!! Body prefab IS the cause!");
                    yield break;
                }
            }
            Log("[DelayedBody] Bot survived with body attached - SUCCESS!");
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

        string GetTransformPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
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
