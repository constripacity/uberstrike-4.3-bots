# UberStrike 4.3 Bot Unity Integration

This directory contains the C# source code for injecting bot logic directly into the UberStrike 4.3 game client (Unity Engine).

## Files

1.  **BotInjector.cs**:
    - The main entry point.
    - Monitors the scene for the `GamePlayer` object.
    - Disables existing human input scripts (e.g., `FirstPersonController`, `PlayerInput`).
    - Attaches the `BotController` component to the player.

2.  **BotController.cs**:
    - The "Brain" of the in-game bot.
    - Uses Reflection to find and control the original game components (`PlayerMovement`, `WeaponSystem`).
    - Implements basic AI:
        - Finds nearest enemy.
        - Navigates/Strafes.
        - Aims and Fires with simulated human delay and jitter.

3.  **InputEmulator.cs**:
    - A helper static class to store virtual input states.
    - Useful if you decide to use a library like Harmony to patch `UnityEngine.Input`.

## How to Use

### Method A: Unity Editor (If you have source access)
1.  Drop this folder into your Unity Project's `Assets/Scripts` folder.
2.  Create an empty GameObject in your scene named `BotSystem`.
3.  Add the `BotInjector` component to it.
4.  Run the game. Press `F12` to toggle the bot on/off.

### Method B: DLL Injection (For compiled game)
1.  Create a new C# Class Library project in Visual Studio.
2.  Add references to:
    - `UnityEngine.dll`
    - `UnityEngine.CoreModule.dll`
    - (Found in `UberStrike_Data/Managed/` folder of the game)
3.  Include these `.cs` files in the project.
4.  Compile to a DLL (e.g., `UberStrikeBot.dll`).
5.  Use a Unity Mono Injector (like SharpMonoInjector) to inject the DLL into the running game process.
    - **Namespace**: `UberStrikeBot`
    - **Class**: `BotInjector`
    - **Method**: `Load` (You may need to add a static `Load` method to `BotInjector` that creates the GameObject).

### Static Load Example
Add this to `BotInjector.cs` for DLL injection support:
```csharp
public static void Load()
{
    GameObject go = new GameObject("BotLoader");
    go.AddComponent<BotInjector>();
    DontDestroyOnLoad(go);
}
```

## Testing & Debugging

### BotTestingHarness.cs
A comprehensive debugging tool to visualize bot logic and track performance.

1.  **Installation**: Attach the `BotTestingHarness` component to the same GameObject as `BotController`.
2.  **Usage**:
    -   **F11**: Toggle Visual Debug Overlay (Vision Cone, State Labels, Target Lines).
    -   **F10**: Dump current performance metrics to CSV on Desktop.
3.  **Features**:
    -   **Visual Overlay**: Shows what the bot "sees" and "thinks".
    -   **Live Metrics**: Displays Accuracy, Reaction Time, and DPM in real-time.
    -   **Parameter Tuning**: Adjust `ReactionTime` and `Aggression` on the fly via on-screen sliders.

## Configuration
- **BotController.cs** has public fields for `ReactionTime`, `AimSpeed`, and `SearchRadius` that can be tuned.
