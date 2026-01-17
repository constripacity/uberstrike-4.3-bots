# How to Inject UberStrike Bots

## Prerequisites
1. **UberStrike 4.3 Client** running.
2. **SharpMonoInjector** (or any Mono Injector).
   - Recommended: [SharpMonoInjector.GUI](https://github.com/warbler/SharpMonoInjector)

## Steps

1. **Start the Game**:
   - Launch `UberStrike.exe`.
   - Go to **Offline / Practice Mode**.
   - Start a match (e.g., Duel or Deathmatch).

2. **Configure Injector**:
   - Open your Injector.
   - **Process**: Select the `UberStrike` process.
   - **Assembly**: Browse and select the compiled DLL:
     `bin/UberStrikeBots.dll` (in your cloned repository)
   - **Namespace**: `UberStrikeBot`
   - **Class**: `BotInjector`
   - **Method**: `Load`

3. **Inject**:
   - Click **Inject**.

## Verifying Injection
- If successful, you should see a log message in the game's debug console (if enabled) or the injector's log.
- Press **F1** to spawn a test bot (via `InjectionTester`).
- Press **F3** to toggle the Bot Debug HUD.
- Press **F12** to toggle Bot AI on/off.

## Troubleshooting
- **Crash on Inject?** Ensure the game is running Unity 2017.4.x and you are injecting into the correct process.
- **Nothing happens?** Check if you are in Practice Mode. The bots require a "Player" tag or "LocalPlayer" object to function.
- **"Class not found"?** Double check the Namespace (`UberStrikeBot`) and Class (`BotInjector`).
