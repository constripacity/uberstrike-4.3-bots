using UnityEngine;

namespace UberStrikeBot
{
    /// <summary>
    /// Utility class to help emulate Unity Input behaviors if we need to hook into
    /// systems that read Input.GetAxis() or Input.GetKey() directly.
    /// Note: Replacing standard Unity Input is hard without a custom InputManager replacement
    /// or hacking the components reading it.
    /// 
    /// This class provides a centralized place to store "Virtual Input" state that
    /// the BotController writes to, and hacked Game classes could read from.
    /// </summary>
    public static class InputEmulator
    {
        public static float VirtualHorizontal = 0f;
        public static float VirtualVertical = 0f;
        public static bool FireButton = false;
        public static bool JumpButton = false;

        public static void Reset()
        {
            VirtualHorizontal = 0f;
            VirtualVertical = 0f;
            FireButton = false;
            JumpButton = false;
        }

        // Usage:
        // Ideally, we would use Harmony (Lib.Harmony) to patch UnityEngine.Input.GetAxis
        // to return these values when the bot is active.
        // Since we are limited to C# scripts here, this class serves as a conceptual bridge
        // or for use if the user can modify the game scripts to read from here.
    }
}
