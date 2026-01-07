using UnityEngine;
using System;
using System.Reflection;

namespace UberStrikeBot
{
    public static class VersionDetector
    {
        public static string Detect()
        {
            try
            {
                // Method 1: Application.unityVersion (Unity 5+)
                // In older Unity, this property might not exist or behave differently.
                // We use reflection to access it safely if we are compiled against a version that doesn't have it explicitly linked.
                PropertyInfo p = typeof(Application).GetProperty("unityVersion");
                if (p != null)
                {
                    return "Unity " + (string)p.GetValue(null, null);
                }

                // Method 2: Check assembly versions
                // GameObject is in UnityEngine.dll (or CoreModule)
                var unityAssembly = typeof(GameObject).Assembly;
                var version = unityAssembly.GetName().Version;
                return $"Unity {version.Major}.{version.Minor} (Assembly)";
            }
            catch (Exception ex)
            {
                return $"Unknown (Error: {ex.Message})";
            }
        }

        public static bool IsUnity2022OrNewer()
        {
            string v = Detect();
            return v.Contains("2022") || v.Contains("2023") || v.Contains("6000"); // Unity 6
        }
    }
}
