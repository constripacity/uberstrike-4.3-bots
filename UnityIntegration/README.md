# UberStrike 4.3 Bot Unity Integration

This directory contains the C# source code for injecting bot logic directly into the UberStrike 4.3 game client (Unity Engine).

## Files

1. **BotInjector.cs**:
   - The main entry point.
   - Monitors the scene for the local player object.
   - Disables existing human input scripts (e.g., `FirstPersonController`, `PlayerInput`).
   - Attaches the `BotController` component to the player.

2. **BotController.cs**:
   - The "Brain" of the in-game bot.
   - Uses Reflection to find and control original game components (`PlayerMovement`, `WeaponSystem`).
   - Implements basic AI:
     - Finds nearest enemy
     - Navigates / strafes
     - Aims and fires with simulated human delay and jitter

3. **InputEmulator.cs**:
   - Helper static class to store virtual input states.
   - Useful if you decide to patch `UnityEngine.Input` with Harmony or similar.

## How to Use

### Method A: Unity Editor (if you have source access)
1. Drop this folder into your Unity project's `Assets/Scripts` folder.
2. Create an empty GameObject in your scene (name optional).
3. Add the `BotInjector` component to it.
4. Run the game. Press the configured toggle key (default F12) to enable/disable the bot.

### Method B: DLL Injection (compiled game)
1. Create a new C# Class Library project in Visual Studio.
2. Reference the Unity managed assemblies from the running game (use the game’s UberStrike_Data/Managed folder):
   - `UnityEngine.dll`
   - `UnityEngine.CoreModule.dll`
3. Add the UnityIntegration `.cs` files and compile to a DLL (recommended name: `UberStrikeBot.dll`).
4. Inject the DLL into the running Unity process with a Mono injector (e.g., SharpMonoInjector) using these exact parameters:
   - Assembly / DLL: `UberStrikeBot.dll`
   - Type (fully-qualified): `UberStrikeBot.BotInjector`
   - Method: `Load`
   - Method signature: `public static void Load()` (no args)
   - In SharpMonoInjector UI: "Load assembly" → select `UberStrikeBot.dll` → Type `UberStrikeBot.BotInjector` → Method `Load`.
5. After injection the `Load` method will create the injector GameObject and attach the `BotInjector` component.

### Static Load Example
The project already includes a static Load pattern; ensure `BotInjector` exposes a public static Load method:
```csharp
public static void Load()
{
    GameObject go = new GameObject("BotInjector");
    go.AddComponent<BotInjector>();
    DontDestroyOnLoad(go);
}
```
Ensure this method is compiled into `UberStrikeBot.dll` and the injector calls `UberStrikeBot.BotInjector.Load`.

## Configuration
- `BotController.cs` exposes public tuning fields: `ReactionTime`, `AimSpeed`, `SearchRadius`, etc.
- Toggle key and AutoInject are configured on the `BotInjector` component.

## Troubleshooting
- Injector fails to load:
  - Confirm `UberStrikeBot.dll` references the same Unity managed DLLs as the running client (Unity 2017.4.x). Use the game's `UberStrike_Data/Managed/` assemblies.
  - Mismatched Unity assembly versions will crash or cause type resolution failures.
  - Temporarily disable antivirus/real-time protection if the injector is blocked.
- No bot behavior after injection:
  - Check the Unity console/log for:
    - "[BotInjector] System initialized…"
    - "[BotInjector] Found player: …"
    - "[BotInjector] Injection Complete."
  - Ensure the game is in Practice/Offline mode (Phase 1 offline-only).
  - Press the configured toggle key (default F12) to enable the bot at runtime.
- Missing types or reflection failures:
  - UberStrike types may be obfuscated or renamed; update reflection lookups in `BotInjector`/`BotController`.
  - If player lookup fails, wait for full scene load or try a different map.
- Input still active:
  - The injector disables common input components (`FirstPersonController`, `PlayerInput`, `MouseLook`). Add additional component names to `DisableComponent` in `BotInjector` if the client uses different scripts.
- Logging and diagnostics:
  - Use `BotTestingHarness` (F11/F10) to enable visual overlays and dump diagnostics for analysis.
- If all else fails, open an issue with logs and steps taken.

## Testing & Debugging

### BotTestingHarness.cs
- Attach `BotTestingHarness` to the same GameObject as `BotController` for overlays and live metrics.
- Keys:
  - F11: Toggle visual debug overlay (vision cone, target lines)
  - F10: Dump performance metrics to CSV on Desktop

## Notes
- Namespace for injection: `UberStrikeBot` (type `UberStrikeBot.BotInjector`).
- The BotRunner folder is legacy/reference code for a headless runner and is not required for Phase 1 (client-side injection).