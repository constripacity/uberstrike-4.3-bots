using UnityEngine;
using System;
using System.Reflection;

namespace UberStrikeBots
{
    /// <summary>
    /// responsible for safely determining if the game is in a state where
    /// offline bots can operate (Practice Mode / Offline).
    /// </summary>
    public class PracticeModeDetector : MonoBehaviour
    {
        private static PracticeModeDetector _instance;
        public static PracticeModeDetector Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("PracticeModeDetector");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<PracticeModeDetector>();
                }
                return _instance;
            }
        }

        public bool IsPracticeMode { get; private set; }
        public event Action<bool> OnModeChanged;

        private float _nextCheckTime;
        private const float CHECK_INTERVAL = 2.0f;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            CheckMode();
        }

        void Update()
        {
            if (Time.time >= _nextCheckTime)
            {
                CheckMode();
                _nextCheckTime = Time.time + CHECK_INTERVAL;
            }
        }

        private void CheckMode()
        {
            bool previousState = IsPracticeMode;
            IsPracticeMode = RunSafetyChecks();

            if (previousState != IsPracticeMode)
            {
                Debug.Log(string.Format("[PracticeModeDetector] Mode changed to: {0}", IsPracticeMode ? "OFFLINE/PRACTICE" : "ONLINE/RESTRICTED"));
                if (OnModeChanged != null) OnModeChanged(IsPracticeMode);
            }
        }

        private bool RunSafetyChecks()
        {
            // CHECK 1: Scene Name
            // UberStrike practice scenes often have specific naming conventions or we might be in the 'Menu'
            string sceneName = Application.loadedLevelName; // Unity 4/5 API (compatible with 2017)
            
            // If we are in the main menu, bots should definitely be disabled
            if (string.IsNullOrEmpty(sceneName) || sceneName.ToLower().Contains("menu"))
                return false;

            // CHECK 2: GameConnectionManager / Photon State
            // We use Reflection to check if we are connected to a server
            if (IsConnectedToPhoton())
            {
                // If we are connected to Photon, we are effectively ONLINE (even if just in a lobby)
                // Unless we can verify it's a local photon instance (unlikely for retail client)
                return false;
            }

            // CHECK 3: GameState Check
            // Check if the current GameStateType is Game (match running)
            if (!IsMatchRunning())
                return false;

            // If we passed all checks, we assume it's safe (Offline Practice)
            return true;
        }

        private bool IsConnectedToPhoton()
        {
            try
            {
                // Try to find PhotonNetwork class
                Type photonNetworkType = Type.GetType("PhotonNetwork, Assembly-CSharp");
                if (photonNetworkType != null)
                {
                    PropertyInfo connectedProp = photonNetworkType.GetProperty("connected", BindingFlags.Public | BindingFlags.Static);
                    if (connectedProp != null)
                    {
                        return (bool)connectedProp.GetValue(null, null);
                    }
                }

                // Fallback: Check GameConnectionManager if it exists
                Type gcmType = Type.GetType("GameConnectionManager, Assembly-CSharp");
                if (gcmType != null)
                {
                    PropertyInfo instanceProp = gcmType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                    object instance = instanceProp.GetValue(null, null);
                    if (instance != null)
                    {
                        PropertyInfo isConnectedProp = gcmType.GetProperty("IsConnected", BindingFlags.Public | BindingFlags.Instance);
                        if (isConnectedProp != null)
                        {
                            return (bool)isConnectedProp.GetValue(instance, null);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[PracticeModeDetector] Error checking connection state: " + ex.Message);
            }
            
            // Fail-safe: If we can't determine, assume connected (unsafe) to be sure
            return true; 
        }

        private bool IsMatchRunning()
        {
            try
            {
                Type gameStateType = Type.GetType("GameState, Assembly-CSharp");
                if (gameStateType != null)
                {
                    PropertyInfo currentProp = gameStateType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    object current = currentProp.GetValue(null, null);
                    if (current != null)
                    {
                        // Assuming GameState.Current.State or similar enum exists
                        // For now, if GameState.Current is not null, a match is likely active
                        return true;
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return false;
        }
    }
}
