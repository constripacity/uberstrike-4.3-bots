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
            sb.AppendLine("=== DEEP DIVE PROBE (F7) - ENHANCED ===");
            sb.AppendLine("Timestamp: " + DateTime.Now.ToString());

            if (player != null) {
                sb.AppendLine("\n========== LOCAL PLAYER ==========");
                sb.AppendLine("Target: " + player.name);
                sb.AppendLine("Layer: " + player.layer + " (" + LayerMask.LayerToName(player.layer) + ")");

                Component localPlayer = player.GetComponent("LocalPlayer");
                if (localPlayer != null) {
                    InspectComponent(localPlayer, sb);

                    // Inspect MoveController
                    try {
                        PropertyInfo moveProp = localPlayer.GetType().GetProperty("MoveController");
                        if ((object)moveProp != null) {
                            object moveCtrl = moveProp.GetValue(localPlayer, null);
                            if (moveCtrl != null) {
                                sb.AppendLine("\n>>> INSPECTING: CharacterMoveController <<<");
                                InspectObject(moveCtrl, sb);
                            }
                        }
                    } catch {}
                }

                // Find ALL CharacterHitArea components in player hierarchy
                sb.AppendLine("\n========== CHARACTER HIT AREAS ON PLAYER ==========");
                FindHitAreasOnObject(player, sb);

            } else {
                sb.AppendLine("Player Object NOT FOUND.");
            }

            // DEEP INSPECT: CharacterHitArea Type
            sb.AppendLine("\n========== CharacterHitArea TYPE ANALYSIS ==========");
            InspectTypeByName("CharacterHitArea", sb);

            // DEEP INSPECT: DamageInfo Type
            sb.AppendLine("\n========== DamageInfo TYPE ANALYSIS ==========");
            InspectTypeByName("DamageInfo", sb);

            // DEEP INSPECT: OnPlayerDamageEvent Type
            sb.AppendLine("\n========== OnPlayerDamageEvent TYPE ANALYSIS ==========");
            InspectTypeByName("OnPlayerDamageEvent", sb);

            // DEEP INSPECT: GetDamageEvent Type
            sb.AppendLine("\n========== GetDamageEvent TYPE ANALYSIS ==========");
            InspectTypeByName("GetDamageEvent", sb);

            // DEEP INSPECT: DamageEffect Type
            sb.AppendLine("\n========== DamageEffect TYPE ANALYSIS ==========");
            InspectTypeByName("DamageEffect", sb);

            // DEEP INSPECT: IShootable Interface (CRITICAL for damage flow!)
            sb.AppendLine("\n========== IShootable INTERFACE ANALYSIS ==========");
            InspectTypeByName("IShootable", sb);

            // DEEP INSPECT: BodyPart Enum
            sb.AppendLine("\n========== BodyPart ENUM ANALYSIS ==========");
            InspectTypeByName("BodyPart", sb);

            // DEEP INSPECT: InstantHitWeapon (how weapons apply damage)
            sb.AppendLine("\n========== InstantHitWeapon TYPE ANALYSIS ==========");
            InspectTypeByName("InstantHitWeapon", sb);

            // Inspect GameState
            try {
                Type gsType = Type.GetType("GameState, Assembly-CSharp");
                if ((object)gsType != null) {
                    sb.AppendLine("\n========== GameState (Static) ==========");
                    InspectTypeDefinition(gsType, sb);

                    PropertyInfo currProp = gsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    if ((object)currProp != null) {
                        object current = currProp.GetValue(null, null);
                        if (current != null) {
                            sb.AppendLine("\n[GameState.Current INSTANCE]");
                            InspectObject(current, sb);
                        } else {
                            sb.AppendLine("GameState.Current is null");
                        }
                    }
                }
            } catch (Exception ex) { sb.AppendLine("GameState Error: " + ex.Message); }

            // Scan for HitManager, DamageController, etc.
            sb.AppendLine("\n========== DAMAGE SYSTEM TYPES ==========");
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.FullName.Contains("Assembly-CSharp")) {
                    foreach (Type t in asm.GetTypes()) {
                        string name = t.Name.ToLower();
                        if (name.Contains("damage") || name.Contains("health") || name.Contains("hitarea") ||
                            name.Contains("hitmanager") || name.Contains("projectile") || name.Contains("weapon")) {
                            sb.AppendLine("  " + t.Name + (t.IsEnum ? " (enum)" : t.IsInterface ? " (interface)" : ""));
                        }
                    }
                }
            }

            Debug.Log(sb.ToString());
            string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop), "DeepProbe.txt");
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[CharacterHitAreaProbe] Results saved to: " + path);
        }

        void FindHitAreasOnObject(GameObject obj, StringBuilder sb)
        {
            try {
                Type hitAreaType = Type.GetType("CharacterHitArea, Assembly-CSharp");
                if ((object)hitAreaType == null) {
                    sb.AppendLine("CharacterHitArea type not found in Assembly-CSharp");
                    return;
                }

                Component[] hitAreas = obj.GetComponentsInChildren(hitAreaType, true);
                sb.AppendLine("Found " + hitAreas.Length + " CharacterHitArea components:");

                foreach (Component ha in hitAreas) {
                    sb.AppendLine("\n  [HitArea on: " + ha.gameObject.name + "]");
                    sb.AppendLine("    Layer: " + ha.gameObject.layer + " (" + LayerMask.LayerToName(ha.gameObject.layer) + ")");

                    // Get all fields and their values
                    foreach (FieldInfo field in hitAreaType.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
                        try {
                            object val = field.GetValue(ha);
                            sb.AppendLine("    " + field.Name + " = " + (val != null ? val.ToString() : "null"));
                        } catch {}
                    }

                    // Get all properties and their values
                    foreach (PropertyInfo prop in hitAreaType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                        try {
                            if (prop.CanRead && prop.GetIndexParameters().Length == 0) {
                                object val = prop.GetValue(ha, null);
                                sb.AppendLine("    " + prop.Name + " => " + (val != null ? val.ToString() : "null"));
                            }
                        } catch {}
                    }

                    // Check for Collider
                    Collider col = ha.GetComponent<Collider>();
                    if (col != null) {
                        sb.AppendLine("    Collider: " + col.GetType().Name + ", isTrigger=" + col.isTrigger);
                    }
                }
            } catch (Exception ex) {
                sb.AppendLine("Error finding HitAreas: " + ex.Message);
            }
        }

        void InspectTypeByName(string typeName, StringBuilder sb)
        {
            try {
                Type t = Type.GetType(typeName + ", Assembly-CSharp");
                if ((object)t == null) {
                    sb.AppendLine("Type '" + typeName + "' not found");
                    return;
                }
                InspectTypeDefinition(t, sb);
            } catch (Exception ex) {
                sb.AppendLine("Error inspecting " + typeName + ": " + ex.Message);
            }
        }

        void InspectTypeDefinition(Type type, StringBuilder sb)
        {
            sb.AppendLine("Type: " + type.FullName);
            sb.AppendLine("IsClass: " + type.IsClass + ", IsEnum: " + type.IsEnum + ", IsInterface: " + type.IsInterface);

            if (type.BaseType != null) {
                sb.AppendLine("BaseType: " + type.BaseType.Name);
            }

            // Fields
            sb.AppendLine("[FIELDS]");
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string mods = (field.IsStatic ? "static " : "") + (field.IsPublic ? "public" : "private");
                sb.AppendLine("  " + mods + " " + field.FieldType.Name + " " + field.Name);
            }

            // Properties
            sb.AppendLine("[PROPERTIES]");
            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                string mods = "";
                MethodInfo getter = prop.GetGetMethod(true);
                if ((object)getter != null) mods = getter.IsStatic ? "static " : "";
                sb.AppendLine("  " + mods + prop.PropertyType.Name + " " + prop.Name + " { " + (prop.CanRead ? "get; " : "") + (prop.CanWrite ? "set; " : "") + "}");
            }

            // Methods
            sb.AppendLine("[METHODS]");
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                if (method.IsSpecialName) continue; // Skip property getters/setters
                string mods = (method.IsStatic ? "static " : "") + (method.IsPublic ? "public" : "private");

                // Build parameter list
                ParameterInfo[] parms = method.GetParameters();
                string parmStr = "";
                for (int i = 0; i < parms.Length; i++) {
                    if (i > 0) parmStr += ", ";
                    parmStr += parms[i].ParameterType.Name + " " + parms[i].Name;
                }

                sb.AppendLine("  " + mods + " " + method.ReturnType.Name + " " + method.Name + "(" + parmStr + ")");
            }

            // Events
            sb.AppendLine("[EVENTS]");
            foreach (EventInfo evt in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                sb.AppendLine("  event " + evt.EventHandlerType.Name + " " + evt.Name);
            }

            // If enum, list values
            if (type.IsEnum) {
                sb.AppendLine("[ENUM VALUES]");
                foreach (string name in Enum.GetNames(type)) {
                    sb.AppendLine("  " + name + " = " + ((int)Enum.Parse(type, name)));
                }
            }
        }

        void InspectObject(object obj, StringBuilder sb)
        {
            Type type = obj.GetType();
            sb.AppendLine("Type: " + type.Name);

            sb.AppendLine("[METHODS]");
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                sb.AppendLine(" * " + method.Name);
            }
        }

        void InspectComponent(Component comp, StringBuilder sb)
        {
            InspectObject(comp, sb);
        }
    }
}