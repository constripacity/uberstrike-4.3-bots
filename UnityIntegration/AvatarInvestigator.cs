using UnityEngine;
using System;
using System.Reflection;
using System.Text;

namespace UberStrikeBot
{
    public class AvatarInvestigator : MonoBehaviour
    {
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                InvestigateSystems();
            }
        }

        public void InvestigateSystems()
        {
            var tester = (InjectionTester)FindObjectOfType(typeof(InjectionTester));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("--- SYSTEM INVESTIGATION (F8) ---");

            // 1. Investigating AvatarDecorator
            sb.AppendLine("[AVATAR DECORATOR SEARCH]");
            Type decoratorType = Type.GetType("AvatarDecorator, Assembly-CSharp");
            if (decoratorType == null) decoratorType = FindTypeInLoadedAssemblies("AvatarDecorator");

            if (decoratorType != null)
            {
                sb.AppendLine("Found AvatarDecorator Type: " + decoratorType.FullName);
                // LIST ALL METHODS to be sure we don't miss anything
                foreach (var method in decoratorType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    sb.AppendLine(" - " + method.Name + " (" + GetParams(method) + ")");
                }
            }
            else
            {
                sb.AppendLine("AvatarDecorator NOT FOUND directly.");
            }

            // 2. Checking GameState for instances
            sb.AppendLine("[GAMESTATE PROBE]");
            try {
                Type gsType = Type.GetType("GameState, Assembly-CSharp");
                if (gsType != null) {
                    PropertyInfo currentProp = gsType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                    object gameState = currentProp.GetValue(null, null);
                    
                    if (gameState != null) {
                        sb.AppendLine("GameState.Current is active.");
                        foreach (var prop in gameState.GetType().GetProperties()) {
                            try {
                                object val = prop.GetValue(gameState, null);
                                sb.AppendLine(" - Prop: " + prop.Name + " Type: " + prop.PropertyType.Name + " Value: " + (val != null ? "Present" : "Null"));
                            } catch {
                                sb.AppendLine(" - Prop: " + prop.Name + " (Error reading value)");
                            }
                        }
                    } else {
                        sb.AppendLine("GameState.Current is NULL.");
                    }
                }
            } catch (Exception ex) {
                sb.AppendLine("GameState Probe Error: " + ex.Message);
            }

            // 3. Resource Scanning
            sb.AppendLine("[RESOURCE SCAN]");
            try {
                // Scan for anything that looks like a character mesh
                UnityEngine.Object[] allObjects = Resources.FindObjectsOfTypeAll(typeof(GameObject));
                int prefabCount = 0;
                foreach (GameObject go in allObjects) {
                    if (go.name.ToLower().Contains("character") || go.name.ToLower().Contains("player") || go.name.ToLower().Contains("enemy")) {
                        if (prefabCount < 10) {
                            sb.AppendLine(" - Found potential prefab: " + go.name + " (Layer: " + go.layer + ")");
                        }
                        prefabCount++;
                    }
                }
                sb.AppendLine("Total potential prefabs found: " + prefabCount);
            } catch (Exception ex) {
                sb.AppendLine("Resource Scan Error: " + ex.Message);
            }

            sb.AppendLine("--- END INVESTIGATION ---");
            if (tester != null) tester.Log(sb.ToString());
            else Debug.Log(sb.ToString());
        }

        private string GetParams(MethodInfo method)
        {
            var ps = method.GetParameters();
            string s = "";
            foreach (var p in ps) s += p.ParameterType.Name + " " + p.Name + ", ";
            return s.TrimEnd(',', ' ');
        }

        private Type FindTypeInLoadedAssemblies(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = assembly.GetType(typeName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
