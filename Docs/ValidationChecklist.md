# Phase 1: Validation Checklist

**Context:** Verification of Offline Practice Mode Bots for UberStrike 4.3.

## 1. Prerequisites
- [ ] **UberStrike 4.3 Client**: Installed and verified working manually.
- [ ] **Practice Mode**: Accessible via the main menu.
- [ ] **Injection Tools**: SharpMonoInjector or similar ready.
- [ ] **Compilation**: `UberStrikeBots.dll` built successfully with .NET 3.5 target.

## 2. Phase 1 Success Criteria
### Functionality
- [ ] **Spawn**: Bots appear in the practice map at valid spawn points.
- [ ] **Move**: Bots patrol and navigate the map without getting stuck in walls.
- [ ] **See**: Bots detect the local player when in line of sight.
- [ ] **Shoot**: Bots fire weapons at the player.
- [ ] **Hit**: Shots register damage locally (via `LocalSimulationManager`).
- [ ] **Die**: Bots play death animation/disappear when health reaches 0.

### Performance
- [ ] **FPS**: Maintained > 60 FPS with 1 bot.
- [ ] **FPS**: Maintained > 60 FPS with 8 bots.
- [ ] **Memory**: Stable usage over 10 minutes of gameplay.

### Stability
- [ ] **No Crashes**: Game does not CTD (Crash to Desktop) upon injection.
- [ ] **Error Logs**: No null reference exceptions in `output_log.txt`.

## 3. Testing Protocol

### Test A: Injection Safety
1. Start UberStrike.
2. Enter Main Menu.
3. Inject `UberStrikeBots.dll`.
4. Verify `PracticeModeDetector` logs "Mode changed to: ONLINE/RESTRICTED" (or similar safe state).
5. **PASS**: No crash, logs confirm detection.

### Test B: Practice Mode Activation
1. From Main Menu, start "Offline Practice".
2. Select Map: "Outpost" (or any standard map).
3. Wait for spawn.
4. Verify `PracticeModeDetector` logs "Mode changed to: OFFLINE/PRACTICE".
5. **PASS**: Log confirmation.

### Test C: Bot Lifecycle
1. Spawn 1 Bot (using debug key or auto-spawn).
2. Observe Bot behavior (Patrol).
3. Engage Bot (shoot at it).
4. Allow Bot to shoot back.
5. Kill Bot.
6. **PASS**: Bot moves, shoots, takes damage, dies.

### Test D: Stress Test
1. Spawn 8 Bots.
2. Run around the map for 5 minutes.
3. Monitor Frame Rate.
4. **PASS**: Smooth gameplay, responsive AI.

## 4. Common Issues & Solutions
- **Bots don't spawn**: Check `BotSpawnManager` logic and spawn point discovery.
- **Bots don't move**: `PlayerMovement` component might be named differently or obfuscated; check Reflection logic.
- **Bots shoot but no damage**: `LocalSimulationManager` might not be tracking the specific ID; verify registration.
- **Performance issues**: Reduce `ViewDistance` or raycast frequency in `BotController`.

## 5. Artifacts
- **Logs**: Save `output_log.txt` after each test session.
- **Screenshots**: Capture debug overlays showing bot states.
