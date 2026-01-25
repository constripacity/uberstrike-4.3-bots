using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace UberStrikeBot
{
    /// <summary>
    /// Main entry point for the bot hook.
    /// This script should be loaded into the game scene (e.g. via a Loader GameObject).
    /// It monitors for Player instantiation and injects the BotController.
    /// </summary>
    public class BotInjector : MonoBehaviour
    {
        private static BotInjector _instance;
        private GameObject _localPlayer;
        private bool _isBotActive = false;

        // Configuration
        public bool AutoInject = false; // CHANGED: Default to FALSE so we don't hijack the player
        public KeyCode ToggleKey = KeyCode.F12;

        /// <summary>
        /// Entry point for DLL Injection loaders (e.g. SharpMonoInjector).
        /// </summary>
        public static void Load()
        {
            // CLEANUP: Destroy old BotLoader if it exists (handles re-injection)
            GameObject old = GameObject.Find("BotLoader");
            if (old != null) 
            {
                UnityEngine.Object.Destroy(old);
            }

            GameObject go = new GameObject("BotLoader");
            go.AddComponent<BotInjector>();
            go.AddComponent<InjectionTester>(); 
            go.AddComponent<CharacterHitAreaProbe>(); // ADDED: Now F7 will work!
            go.AddComponent<ReflectionProbe>(); // ADDED: Now F9 will work!
            go.AddComponent<AvatarInvestigator>(); // ADDED: Now F8 will work!
            DontDestroyOnLoad(go);
        }

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            string v = VersionDetector.Detect();
            Debug.Log("[BotInjector] System initialized. Detected: " + v);
            
            if (v.Contains("2022") || v.Contains("5.") || v.Contains("20")) 
            {
                Debug.Log("[BotInjector] Running in Modern Unity Mode");
            }
            else
            {
                 Debug.Log("[BotInjector] Running in Legacy Mode (Unity 3.5/4)");
            }

            // DISABLED: Auto-monitoring causes local player hijacking
            // Bot spawning is now handled by InjectionTester (F1 key)
            // StartCoroutine(MonitorForPlayer());
            
            Debug.Log("[BotInjector] Auto-monitoring DISABLED. Use InjectionTester (F1) to spawn bots.");
        }

        void Update()
        {
            if (Input.GetKeyDown(ToggleKey))
            {
                _isBotActive = !_isBotActive;
                Debug.Log("[BotInjector] Bot Active: " + _isBotActive);
                
                // Toggle all existing bots (not local player)
                var bots = UnityEngine.Object.FindObjectsOfType(typeof(BotController));
                foreach (BotController bot in bots)
                {
                    bot.enabled = _isBotActive;
                }
                Debug.Log("[BotInjector] Toggled " + bots.Length + " bot(s)");
            }
        }

        /// <summary>
        /// Continuously scans for the local player object.
        /// UberStrike 4.3 typically instantiates the player with a specific tag or name pattern.
        /// </summary>
        IEnumerator MonitorForPlayer()
        {
            while (true)
            {
                if (_localPlayer == null)
                {
                    // Attempt 1: Find by Tag
                    GameObject found = GameObject.FindGameObjectWithTag("Player");

                    // Attempt 2: Find by Component (more reliable if we know the class name)
                    // We use reflection to find "GamePlayer" or "PlayerController" if tag fails.
                    if (found == null)
                    {
                        found = FindPlayerByReflection();
                    }

                    if (found != null)
                    {
                        InjectBot(found);
                    }
                }
                yield return new WaitForSeconds(1.0f);
            }
        }

        GameObject FindPlayerByReflection()
        {
            // Scan all GameObjects (expensive, but necessary if tag is missing)
            // In a real scenario, we might optimize this by searching only root objects or specific layers.
            // Here we assume standard Unity behaviour.
            // Placeholder: searching for a known UberStrike component "GameState" or "GamePlayer"
            // Note: This is where we would hook into specific UberStrike classes.
            
            // For now, we look for a generic "Player" name which is common in 4.3
            var obj = GameObject.Find("LocalPlayer"); // Common name in older versions
            if (obj == null) obj = GameObject.Find("GamePlayer");
            return obj;
        }

        void InjectBot(GameObject player)
        {
            _localPlayer = player;
            Debug.Log("[BotInjector] Found player: " + player.name + ". Injecting BotController...");

            // 1. Disable existing Human Input
            // We need to find standard Unity input scripts or UberStrike specific ones.
            // Typically: "FirstPersonController", "PlayerInput", "GameStateInput"
            DisableComponent(player, "FirstPersonController");
            DisableComponent(player, "PlayerInput");
            DisableComponent(player, "MouseLook"); // Often separate

            // 2. Add Bot Controller
            var bot = player.GetComponent<BotController>();
            if (bot == null)
            {
                bot = player.AddComponent<BotController>();
            }

            bot.Initialize();
            bot.enabled = _isBotActive;
            
            Debug.Log("[BotInjector] Injection Complete.");
        }

        void DisableComponent(GameObject go, string componentName)
        {
            var comp = go.GetComponent(componentName);
            if (comp != null && comp is MonoBehaviour)
            {
                ((MonoBehaviour)comp).enabled = false;
                Debug.Log("[BotInjector] Disabled existing input component: " + componentName);
            }
        }
    }
}