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
            GameObject player = GameObject.Find("LocalPlayer");
            if (player == null) player = GameObject.Find("GamePlayer");
            if (player == null) player = GameObject.FindGameObjectWithTag("Player");

            var tester = (InjectionTester)FindObjectOfType(typeof(InjectionTester));

            if (player == null)
            {
                if (tester != null) tester.Log("[ReflectionProbe] ERROR: No player found.");
                return;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- DEEP PROBE: " + player.name + " ---");

            // 1. ALL COMPONENTS
            sb.AppendLine("[COMPONENTS]");
            foreach (var c in player.GetComponents<Component>())
            {
                if (c == null) continue;
                sb.AppendLine(" - " + c.GetType().Name);
                
                // Check for Health/Damage fields in ANY component
                foreach (var f in c.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.Name.ToLower().Contains("health") || f.Name.ToLower().Contains("hp"))
                    {
                        sb.AppendLine("    -> Found Field: " + f.Name + " (" + f.FieldType.Name + ") = " + f.GetValue(c));
                    }
                }
            }

            // 2. ALL CHILDREN (Hierarchy)
            sb.AppendLine("[CHILDREN]");
            DumpChildren(player.transform, sb, "  ");

            // 3. INSPECT PLAYERBASE (The Body)
            Transform pb = player.transform.Find("PlayerBase");
            if (pb != null)
            {
                sb.AppendLine("[PLAYERBASE COMPONENTS]");
                foreach(var c in pb.GetComponents<Component>())
                {
                     if (c == null) continue;
                     sb.AppendLine(" - " + c.GetType().Name);
                }
            }

            // 4. GLOBAL GAMESTATE
            sb.AppendLine("[GAMESTATE]");
            try {
                System.Type gs = System.Type.GetType("GameState, Assembly-CSharp");
                if (gs != null)
                {
                    foreach (var prop in gs.GetProperties(BindingFlags.Public | BindingFlags.Static))
                    {
                        sb.AppendLine(" -> " + prop.Name + " (" + prop.PropertyType.Name + ")");
                    }
                }
            } catch {}

            sb.AppendLine("--- END DEEP REPORT ---");

            if (tester != null) tester.Log(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        void DumpChildren(Transform t, StringBuilder sb, string indent)
        {
            foreach (Transform child in t)
            {
                sb.AppendLine(indent + "-> " + child.name + " [Layer: " + child.gameObject.layer + "]");
                if (child.childCount > 0) DumpChildren(child, sb, indent + "  ");
            }
        }
    }
}