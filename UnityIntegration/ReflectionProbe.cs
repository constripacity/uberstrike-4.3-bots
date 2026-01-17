using UnityEngine;
using System.Reflection;
using System.Text;

namespace UberStrikeBot
{
    /// <summary>
    /// Utility to discover the actual component and method names on the Player object.
    /// Usage: Attach to a GameObject, run game, press F9 when Player is alive.
    /// </summary>
    public class ReflectionProbe : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                ProbePlayer();
            }
        }

        void ProbePlayer()
        {
            // Try to find local player
            GameObject player = GameObject.Find("GamePlayer");
            if (player == null) player = GameObject.Find("LocalPlayer");
            if (player == null) player = GameObject.FindGameObjectWithTag("Player");

            if (player == null)
            {
                Debug.LogError("[ReflectionProbe] Could not find player object!");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- PROBE REPORT FOR: " + player.name + " ---");

            Component[] comps = player.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                System.Type type = c.GetType();
                sb.AppendLine("[Component] " + type.Name);

                // List public methods that might be useful
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    // Filter out standard Unity methods to reduce noise
                    if (m.Name == "Update" || m.Name == "FixedUpdate" || m.Name == "Start" || m.Name == "Awake") continue;
                    
                    string paramsStr = "";
                    foreach (var p in m.GetParameters())
                    {
                        paramsStr += p.ParameterType.Name + " " + p.Name + ", ";
                    }
                    if (paramsStr.Length > 0) paramsStr = paramsStr.Substring(0, paramsStr.Length - 2);

                    sb.AppendLine("    -> Method: " + m.Name + "(" + paramsStr + ")");
                }
            }
            sb.AppendLine("--- END REPORT ---");

            Debug.Log(sb.ToString());
            
            // Also write to file for easy copy-paste
            string path = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "UberStrike_Probe.txt");
            System.IO.File.WriteAllText(path, sb.ToString());
            Debug.Log("[ReflectionProbe] Report saved to: " + path);
        }
    }
}