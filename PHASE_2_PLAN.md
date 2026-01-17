# Phase 2: Stable Combatants & Remote Prefabs

## Objectives
1.  **Fix Local Player Hijack**: Ensure the bot AI never attaches to the user's character.
2.  **Fix Gravity/Physics**: Stop bots from falling through the map.
3.  **Improve Visuals**: Use the Third-Person (Enemy) model instead of the First-Person (Arms) model.

## Action Plan

### Step 1: Secure the Injector (Priority High)
- Modify `MonitorForPlayer()` in `BotInjector.cs`.
- Explicitly check `obj.name != "LocalPlayer"` and `!obj.GetComponent<Camera>()` before attaching AI.

### Step 2: Discover Remote Player Prefab
- The current cloning method uses `LocalPlayer`, which is why bots are invisible (arms only) and have weird physics.
- We need to find the **"RemotePlayer"** or **"Enemy"** prefab.
- **Action**: Update `ReflectionProbe.cs` to dump the hierarchy of *other* players in a multiplayer match (if possible) or scan the `GameResourceManager`.

### Step 3: Implement "Puppet" Spawning
- Instead of `Instantiate(LocalPlayer)`, we will try:
    - `Instantiate(EnemyPrefab)`
    - OR create a primitive capsule that *manually* handles gravity using `SimpleMove`.

### Step 4: UI & Feedback
- Add a visual kill feed or simple OnGUI list of active bots.
- Add an "Enabled" toggle that defaults to **OFF**.
