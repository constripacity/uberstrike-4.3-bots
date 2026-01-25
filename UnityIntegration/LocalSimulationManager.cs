using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace UberStrikeBot
{
    /// <summary>
    /// Manages client-side simulation logic for Offline Practice Mode.
    /// Replaces the authoritative server for hit registration, damage, and death.
    /// </summary>
    public class LocalSimulationManager : MonoBehaviour
    {
        private static LocalSimulationManager _instance;
        public static LocalSimulationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("LocalSimulationManager");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<LocalSimulationManager>();
                }
                return _instance;
            }
        }

        // Tracking simulated entities
        private Dictionary<int, float> _botHealth = new Dictionary<int, float>();
        private Dictionary<int, BotController> _botControllers = new Dictionary<int, BotController>();
        private const float MAX_HEALTH = 100f;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        public void RegisterBot(int botId, BotController controller)
        {
            if (!_botHealth.ContainsKey(botId))
            {
                _botHealth[botId] = MAX_HEALTH;
            }
            _botControllers[botId] = controller;
        }

        public void ApplyDamage(int targetId, float damage, Vector3 hitPoint)
        {
            if (!PracticeModeDetector.Instance.IsPracticeMode) return;

            if (_botHealth.ContainsKey(targetId))
            {
                _botHealth[targetId] -= damage;
                Debug.Log(string.Format("[LocalSim] Bot {0} took {1} damage. HP: {2}", targetId, damage, _botHealth[targetId]));

                // Notify the controller if it exists and hasn't already been updated
                if (_botControllers.ContainsKey(targetId))
                {
                    var controller = _botControllers[targetId];
                    if (controller != null && controller.Health > _botHealth[targetId])
                    {
                        // Note: To avoid recursion, we should ensure ReceiveDamage 
                        // doesn't call ApplyDamage again if it's already being handled here.
                        // However, BotController.ReceiveDamage CURRENTLY calls ApplyDamage.
                        // So we have a choice:
                        // 1. BotController.ReceiveDamage is the entry point.
                        // 2. LocalSimulationManager.ApplyDamage is the entry point.
                        
                        // If we want the bot to flash red and die, ReceiveDamage must be called.
                        controller.Health = _botHealth[targetId];
                        controller.TriggerHitEffects(); // We'll add this method
                        if (controller.Health <= 0) controller.Die();
                    }
                }

                if (_botHealth[targetId] <= 0)
                {
                    HandleBotDeath(targetId);
                }
            }
            else
            {
                // Might be the local player or an untracked entity
                Debug.LogWarning(string.Format("[LocalSim] Damage applied to unknown entity ID: {0}", targetId));
            }
        }

        private void HandleBotDeath(int botId)
        {
            Debug.Log(string.Format("[LocalSim] Bot {0} DIED.", botId));
            
            // In a full implementation, we would:
            // 1. Trigger ragdoll
            // 2. Play sound
            // 3. Show kill feed message
            // 4. Respawn after delay
            
            // For now, simply reset health and respawn immediately to keep loop going
            _botHealth[botId] = MAX_HEALTH;
            
            // Notify BotController to reset
            // This requires a registry of BotControllers, skipped for brevity in this step
        }

        public float GetHealth(int botId)
        {
            if (_botHealth.ContainsKey(botId)) return _botHealth[botId];
            return 0f;
        }
    }
}
