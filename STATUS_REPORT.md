# UberStrike Bot Development - Status Report
**Date:** January 19, 2026
**Status:** AI Loop Active, Movement Blocked

## 1. Achievements (What Works)
*   **Injection Pipeline:** Fully functional. We can compile and inject C# code into the running game (`UberStrike.exe`) using `SharpMonoInjector`.
*   **Crash Stability:** Solved the "Unity 3.5 Reflection Crash" by fixing `PropertyInfo` null checks in `BotController.cs`. The game no longer crashes on bot spawn.
*   **Logging System:** Overcame the invisible `Debug.Log` issue by routing bot logs to `InjectionTester`'s HUD and text file (`UberStrikeBotLog.txt`).
*   **AI Lifecycle:** Confirmed via logs (`UPDATE CALLED!`) that the `BotController` `Update()` loop is running 30 times a second. The AI is "thinking."
*   **Gravity & Physics:** Fixed the "Floating Bot" issue. Bots now correctly apply gravity and snap to the ground (Y-coordinates dropped from ~117 to ~3.5).
*   **Invincibility:** Fixed by setting the Bot's root GameObject layer to `RemotePlayer` (20), allowing weapons to register hits.

## 2. Current Blocker (The "Statue" Bug)
**Symptoms:**
*   Bots stand perfectly still.
*   Logs show thousands of `UPDATE CALLED!` entries.
*   Logs show **ZERO** `Moving: (x,y,z)` entries.
*   Bots are taking patrol states (`State: Patrol`) but not going anywhere.

**Diagnosis:**
The **Wall Avoidance System** is detecting the bot's own body or weapon as a "Wall" and immediately cancelling movement.

*   **The Cause:** The "Wall Check" Raycast ignores Layer 20 (Body), but `InjectionTester` spawns Weapons on **Layer 0 (Default)**.
*   **The Conflict:** The Raycast hits the gun (which is on Layer 0), thinks it's a wall, and sets `moveDir` to zero.
*   **The Loop:** 
    1. AI picks a destination.
    2. AI calculates direction.
    3. Wall Check hits the gun.
    4. AI cancels move.
    5. Repeat forever.

## 3. Next Steps (The Fix)
1.  **Immediate Fix:** Update `BotController.Initialize()` to recursively set **ALL** children (including weapons) to Layer 20 (`RemotePlayer`) or Layer 2 (`Ignore Raycast`).
2.  **Verify:** Re-inject and watch them move.
3.  **Next Phase:** Verify combat logic (Shooting/Aiming).