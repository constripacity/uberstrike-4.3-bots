using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace UberStrikeBot
{
    /// <summary>
    /// Debugging and Testing harness for BotController.
    /// Provides OnGUI overlays, metric tracking, and parameter tuning.
    /// Should only be active in Development builds or when debugging is enabled.
    /// </summary>
    public class BotTestingHarness : MonoBehaviour
    {
        public bool ShowDebugOverlay = true;
        public bool RecordMetrics = true;
        
        private BotController _bot;
        private Camera _mainCam;
        public BotMetrics Metrics = new BotMetrics();
        
        // GUI Styles
        private GUIStyle _styleState;
        private GUIStyle _styleAlert;
        
        void Start()
        {
            _bot = GetComponent<BotController>();
            _mainCam = Camera.main;
            if (_bot == null)
            {
                Debug.LogError("[BotTestingHarness] No BotController found on this GameObject!");
                enabled = false;
            }
        }

        void Update()
        {
            if (RecordMetrics)
            {
                Metrics.SurvivalTime += Time.deltaTime;
            }

            if (Input.GetKeyDown(KeyCode.F11))
            {
                ShowDebugOverlay = !ShowDebugOverlay;
            }
            
            if (Input.GetKeyDown(KeyCode.F10))
            {
                DumpMetricsToCSV();
            }
        }

        void OnGUI()
        {
            if (!ShowDebugOverlay || _bot == null) return;

            InitStyles();

            // 1. Draw Status Box above bot
            Vector3 screenPos = _mainCam.WorldToScreenPoint(transform.position + Vector3.up * 2.2f);
            if (screenPos.z > 0)
            {
                screenPos.y = Screen.height - screenPos.y; // Invert Y
                GUI.Label(new Rect(screenPos.x - 50, screenPos.y, 100, 25), _bot._currentState.ToString(), _styleState);
            }

            // 2. Dashboard Top Left
            GUILayout.BeginArea(new Rect(10, 10, 300, 400), "Bot Metrics", GUI.skin.window);
            GUILayout.Label($"State: {_bot._currentState}");
            GUILayout.Label($"Accuracy: {(Metrics.Accuracy * 100):F1}% ({Metrics.ShotsHit}/{Metrics.ShotsFired})");
            GUILayout.Label($"Avg Reaction: {Metrics.AverageReactionTime:F3}s");
            GUILayout.Label($"DPM: {Metrics.DamageDealtPerMinute:F0}");
            GUILayout.Space(10);
            GUILayout.Label("Current Parameters:");
            _bot.ReactionTime = GUILayout.HorizontalSlider(_bot.ReactionTime, 0.05f, 1.0f);
            GUILayout.Label($"Reaction Time: {_bot.ReactionTime:F2}s");
            _bot.Aggression = GUILayout.HorizontalSlider(_bot.Aggression, 0f, 1f);
            GUILayout.Label($"Aggression: {_bot.Aggression:F2}");
            GUILayout.EndArea();
        }

        void OnDrawGizmos()
        {
            if (!ShowDebugOverlay || _bot == null) return;

            // 1. Vision Cone
            Gizmos.color = new Color(0, 1, 0, 0.2f);
            Vector3 forward = transform.forward; // Assuming bot rotates transform, if camera rotates separate use that
            // If BotController tracks camera transform, ideally we use that, but we don't have public access to it easily without reflection
            // We'll use transform.forward for approximation or assume the harness is on the root
            
            Gizmos.DrawFrustum(transform.position + Vector3.up * 1.6f, _bot.ViewAngle, _bot.ViewDistance, 0.1f, 1.0f);

            // 2. Memory Targets
            foreach (var kvp in _bot._targetMemory)
            {
                var mem = kvp.Value;
                float age = Time.time - mem.Timestamp;
                
                // Green = Visible (Fresh), Yellow = Memory, Red = Old
                if (age < 0.5f && mem.IsVisual) Gizmos.color = Color.green;
                else if (age < 5.0f) Gizmos.color = Color.yellow;
                else Gizmos.color = Color.red;

                Gizmos.DrawSphere(mem.Position, 0.5f);
                Gizmos.DrawLine(transform.position + Vector3.up * 1.5f, mem.Position);
            }

            // 3. Current Destination
            if (_bot._currentState == BotState.Search || _bot._currentState == BotState.Patrol)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position, _bot._moveDestination);
                Gizmos.DrawWireSphere(_bot._moveDestination, 1.0f);
            }
            
            // 4. Best Target
            if (_bot._bestTarget != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(_bot._bestTarget.position + Vector3.up, Vector3.one);
            }
        }

        void InitStyles()
        {
            if (_styleState == null)
            {
                _styleState = new GUIStyle(GUI.skin.label);
                _styleState.alignment = TextAnchor.MiddleCenter;
                _styleState.fontSize = 14;
                _styleState.fontStyle = FontStyle.Bold;
                _styleState.normal.textColor = Color.white;
            }
        }

        public void DumpMetricsToCSV()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Timestamp,State,Accuracy,ReactionTime,DPM,SurvivalTime");
            sb.AppendLine($"{System.DateTime.Now},{_bot._currentState},{Metrics.Accuracy},{Metrics.AverageReactionTime},{Metrics.DamageDealtPerMinute},{Metrics.SurvivalTime}");
            
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "BotMetrics.csv");
            File.AppendAllText(path, sb.ToString());
            Debug.Log($"[BotTestingHarness] Metrics saved to {path}");
        }
    }
}
