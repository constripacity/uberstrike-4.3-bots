using UnityEngine;
using System;
using System.Reflection;
using System.Text;
using System.IO;

namespace UberStrikeBot
{
    public class CharacterHitAreaProbe : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                ProbeLocalPlayer();
            }
        }

        void ProbeLocalPlayer()
        {
            GameObject player = GameObject.Find("LocalPlayer");
            if (player == null) player = GameObject.Find("GamePlayer");
            if (player == null) player = GameObject.FindGameObjectWithTag("Player");
            
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== DEEP DIVE PROBE (F7) ===");
            
            if (player != null) {
                sb.AppendLine("Target: " + player.name);
                Component localPlayer = player.GetComponent("LocalPlayer");
                if (localPlayer != null) {
                    InspectComponent(localPlayer, sb);
                    
                    // Inspect MoveController
                    try {
                        PropertyInfo moveProp = localPlayer.GetType().GetProperty("MoveController");
                        // UNITY 3.5 FIX: Use (object) cast for PropertyInfo null checks
                        if ((object)moveProp != null) {
                            object moveCtrl = moveProp.GetValue(localPlayer, null);
                            if (moveCtrl != null) {
                                sb.AppendLine("\n>>> INSPECTING: CharacterMoveController <<<");
                                InspectObject(moveCtrl, sb);
                            }
                        }
                    } catch {} // Ignore exceptions during MoveController inspection
                }
            } else {
                sb.AppendLine("Player Object NOT FOUND.");
            }

            // Inspect GameState
            try {
                Type gsType = Type.GetType("GameState, Assembly-CSharp");
                if ((object)gsType != null) {
                    sb.AppendLine("\n>>> INSPECTING: GameState (Static) <<<");
                    PropertyInfo currProp = gsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    // UNITY 3.5 FIX: Use (object) cast for PropertyInfo null checks
                    if ((object)currProp != null) {
                        object current = currProp.GetValue(null, null);
                        if (current != null) {
                            InspectObject(current, sb);
                        } else {
                            sb.AppendLine("GameState.Current is null");
                        }
                    }
                }
            } catch (Exception ex) { sb.AppendLine("GameState Error: " + ex); } // Catch and report GameState errors

            // Scan Assembly for Damage/Health types
            sb.AppendLine("\n>>> TYPE SCANNER <<<");
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.FullName.Contains("Assembly-CSharp")) {
                    foreach (Type t in asm.GetTypes()) {
                        string name = t.Name.ToLower();
                        if (name.Contains("damage") || name.Contains("health") || name.Contains("hitarea")) {
                            sb.AppendLine("Found Type: " + t.Name);
                        }
                    }
                }
            }

            Debug.Log(sb.ToString());
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "DeepProbe.txt");
            File.WriteAllText(path, sb.ToString());
        }

        void InspectObject(object obj, StringBuilder sb)
        {
            Type type = obj.GetType();
            sb.AppendLine("Type: " + type.Name);
            
            sb.AppendLine("[METHODS]");
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                sb.AppendLine(" * " + method.Name);
            }
        }

        void InspectComponent(Component comp, StringBuilder sb)
        {
            InspectObject(comp, sb);
        }
    }
}