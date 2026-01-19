using UnityEngine;
using System;
using System.Reflection;

namespace UberStrikeBot
{
    // Helper to interact with the game's internal systems via Reflection
    public static class GameFacade
    {
        private static Type _hudType;
        private static PropertyInfo _hudInstance;
        private static MethodInfo _addEventText;
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Find EventStreamHud
                _hudType = Type.GetType("EventStreamHud, Assembly-CSharp");
                if ((object)_hudType != null)
                {
                    // Singleton<EventStreamHud>.Instance
                    // Actually, Singleton<T> usually has a static Instance property on T itself if implemented simply,
                    // OR on the base class. The decompiled code showed "public class EventStreamHud : Singleton<EventStreamHud>".
                    // So EventStreamHud.Instance should work.
                    
                    _hudInstance = _hudType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    _addEventText = _hudType.GetMethod("AddEventText");
                    
                    Debug.Log("[GameFacade] Hooked into EventStreamHud");
                }
                else
                {
                    Debug.LogError("[GameFacade] Could not find EventStreamHud type");
                }
                _initialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogError("[GameFacade] Init Error: " + ex.Message);
            }
        }

        public static void SendKillMessage(string killer, string verb, string victim)
        {
            if (!_initialized) Initialize();
            if ((object)_hudInstance == null || (object)_addEventText == null) return;

            try
            {
                object instance = _hudInstance.GetValue(null, null);
                if (instance != null)
                {
                    // Signature: AddEventText(string subjective, TeamID subTeamId, string verb, string objective, TeamID objTeamId)
                    // TeamID enum needs to be handled. We can use integer 0 (NONE), 1 (RED), 2 (BLUE).
                    // System.Enum.ToObject might be needed if strictly typed.
                    
                    // Let's assume TeamID is an enum in Assembly-CSharp. We need to find it or pass ints casted.
                    // Reflection usually accepts ints for enums.
                    
                    object[] args = new object[] {
                        killer,
                        0, // TeamID.NONE
                        verb,
                        victim,
                        0  // TeamID.NONE
                    };
                    
                    _addEventText.Invoke(instance, args);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[GameFacade] SendKillMessage Failed: " + ex.Message);
            }
        }
    }
}
