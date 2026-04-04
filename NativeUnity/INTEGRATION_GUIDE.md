# Native Unity 2022 Bot Integration Guide

**For: UberStrike 4.3.8 Unity 2022 project**

Standalone guide for integrating the native bot system into any UberStrike Unity 2022 project.
No external processes, DLL injection, or server connection required — bots run entirely in-engine.

---

## Prerequisites

- Unity 2022 (any patch)
- UberStrike 4.3.8 Unity 2022 project
- NavMesh baked on the map (see Step 3)

---

## Files Overview

### New Files to Add (6 bot scripts)

| File | Destination | Purpose |
|------|-------------|---------|
| `Bots/BotConfig.cs` | `Assets/Scripts/Bots/` | Config: movement, weapons, gear, difficulty |
| `Bots/BotController.cs` | `Assets/Scripts/Bots/` | Brain: `IShootable`, FSM, smart combat AI, avatar |
| `Bots/BotNavigation.cs` | `Assets/Scripts/Bots/` | Physics: Quake velocity + `CC.Move`, pathfinding |
| `Bots/BotWeaponHandler.cs` | `Assets/Scripts/Bots/` | Shooting, Quick Switch, weapon VFX |
| `Bots/BotSpawner.cs` | `Assets/Scripts/Bots/` | Spawning, hotkeys, ESP, difficulty cycling |
| `Bots/BotDebugLogger.cs` | `Assets/Scripts/Bots/` | Per-category debug logging |

### Files to Modify (5 surgical edits)

| File | Original Location | Change |
|------|------------------|--------|
| `ModifiedGameFiles/ForceField.cs` | `Assets/Scripts/LevelBehaviour/` | JumpPad bot trigger |
| `ModifiedGameFiles/GameModeUtil.cs` | `Assets/Scripts/GameModes/Util/` | Suicide → bot kill intercept |
| `ModifiedGameFiles/FpsGameMode.cs` | `Assets/Scripts/GameModes/` | End-of-match bot stat injection |
| `ModifiedGameFiles/DeathArea.cs` | `Assets/Scripts/LevelBehaviour/` | Bot kill-zone death handling |
| `ModifiedGameFiles/LevelBoundary.cs` | `Assets/Scripts/LevelBehaviour/` | Boundary death + stale attacker clear |

---

## Step 1 — Copy Bot Scripts

Copy all 6 files from `NativeUnity/Bots/` to `Assets/Scripts/Bots/` in your Unity project.

```
Assets/Scripts/Bots/
    BotConfig.cs
    BotController.cs
    BotNavigation.cs
    BotWeaponHandler.cs
    BotSpawner.cs
    BotDebugLogger.cs
```

---

## Step 2 — Apply Modified Game Files

### Option A — Replace Files Directly (Easiest)

```
NativeUnity/ModifiedGameFiles/ForceField.cs     → Assets/Scripts/LevelBehaviour/ForceField.cs
NativeUnity/ModifiedGameFiles/GameModeUtil.cs   → Assets/Scripts/GameModes/Util/GameModeUtil.cs
NativeUnity/ModifiedGameFiles/FpsGameMode.cs    → Assets/Scripts/GameModes/FpsGameMode.cs
NativeUnity/ModifiedGameFiles/DeathArea.cs      → Assets/Scripts/LevelBehaviour/DeathArea.cs
NativeUnity/ModifiedGameFiles/LevelBoundary.cs  → Assets/Scripts/LevelBehaviour/LevelBoundary.cs
```

### Option B — Apply Changes Manually

All bot additions are marked `// [BOT INTEGRATION]` in the source. Here's what each file needs:

---

#### ForceField.cs — `OnTriggerEnter`

```csharp
private void OnTriggerEnter(Collider collider)
{
    if (collider.tag == "Player")
    {
        // ... existing player JumpPad code ...
    }
    else
    {
        // [BOT INTEGRATION] Apply JumpPad force to bots
        var botNav = collider.GetComponentInParent<BotNavigation>();
        if (botNav != null)
        {
            botNav.ApplyJumpPadForce(_direction.normalized * _force);
            SfxManager.Play3dAudioClip(SoundEffectType.PropsJumpPad,
                1.0f, 0.1f, 10.0f, AudioRolloffMode.Linear, transform.position);
        }
    }
}
```

---

#### GameModeUtil.cs — `OnPlayerSuicide`

Prepend at the top of the method:

```csharp
public static void OnPlayerSuicide(OnPlayerSuicideEvent ev)
{
    // [BOT INTEGRATION] If bot killed the player, show bot name instead of "killed myself"
    if (ev.PlayerInfo.ActorId == GameState.CurrentPlayerID
        && BotController.LastBotAttacker != null
        && (Time.time - BotController.LastBotAttackTime) < 3f)
    {
        BotController.ShowBotKilledPlayerScreen();
        return;
    }
    // ... rest of original code ...
}
```

---

#### DeathArea.cs — `OnTriggerEnter`

```csharp
private void OnTriggerEnter(Collider collider)
{
    if (collider.tag == "Player")
    {
        // ... existing player death code ...
    }
    else
    {
        // [BOT INTEGRATION] Kill bots that enter a death zone
        var bot = collider.GetComponentInParent<BotController>();
        if (bot != null)
        {
            bot._isEnvironmentDeath = true;
            bot.TakeDamage(1000);
        }
    }
}
```

> **Why `_isEnvironmentDeath`:** Prevents the bot kill from being credited to the last player
> who shot the bot before it walked into lava. Environment kills are attributed to the environment.

---

#### LevelBoundary.cs — `OnTriggerExit` + clear stale attacker

```csharp
private void OnTriggerExit(Collider collider)
{
    // [BOT INTEGRATION] Kill bots that leave map bounds
    var bot = collider.GetComponentInParent<BotController>();
    if (bot != null)
    {
        // Boundary death = environment death, not a bot/player kill
        BotController.LastBotAttacker = null;
        BotController.LastBotAttackBodyPart = BodyPart.Body;
        bot._isEnvironmentDeath = true;
        bot.TakeDamage(1000);
        return;
    }
    // ... existing player boundary code ...
}
```

> **Why:** Without this, bots on Gideon's Tower and Temple of the Raven can pathfind
> off the map edge. The boundary kill clears `LastBotAttacker` so the death is recorded as
> a suicide/environment kill, not a player kill.

---

#### FpsGameMode.cs — `OnMatchEnd`

Find `EndOfMatchStats.Instance.Data = matchData;` and insert **just before** it:

```csharp
// [BOT INTEGRATION] Inject bot stats before EndOfMatchStats reads matchData
try
{
    int botKills = BotController.TotalBotKills;
    if (botKills > 0 && BotController.AllBots.Count > 0)
    {
        // Fix player kill count in MVP table
        if (matchData.MostValuablePlayers != null)
        {
            int localCmid = PlayerDataManager.CmidSecure;
            foreach (var mvp in matchData.MostValuablePlayers)
            {
                if (mvp.Cmid == localCmid) { mvp.Kills = botKills; break; }
            }
        }
        // Fix PlayerStatsTotal: add kills, zero out suicide penalty
        if (matchData.PlayerStatsTotal != null)
        {
            matchData.PlayerStatsTotal.MachineGunKills += botKills;
            matchData.PlayerStatsTotal.Suicides = 0;
        }
        // Add bot entries to MVP scoreboard
        if (matchData.MostValuablePlayers != null)
        {
            foreach (var bot in BotController.AllBots)
            {
                if (bot == null) continue;
                var s = new StatsSummary
                {
                    Name = bot.BotName, Kills = bot.ScoreboardKills,
                    Deaths = bot.ScoreboardDeaths, Level = 10 + (bot.BotIndex % 20),
                    Cmid = -(900 + bot.BotIndex), Team = TeamID.NONE,
                    Achievements = new Dictionary<byte, ushort>()
                };
                matchData.MostValuablePlayers.Add(s);
            }
        }
    }
}
catch (System.Exception) { }
// ← original line follows:
EndOfMatchStats.Instance.Data = matchData;
```

Also find `OnSetNextSpawnPoint` and cap the respawn delay:

```csharp
// [BOT INTEGRATION] Cap respawn to 5s (server sends 8s in matchmaking)
if (coolDownTime > 5) coolDownTime = 5;
```

---

## Step 3 — Bake NavMesh

The bots use `NavMeshAgent` for pathfinding only (`updatePosition = false`). A baked NavMesh is required.

**Manual (per map):**
1. Open map scene in Unity Editor
2. **Window → AI → Navigation → Bake tab → Bake**

**Bulk (editor tool — if `NavMeshBakeHelper.cs` is in your project):**
- **Tools → UberStrike → Bake NavMesh All Maps** — bakes all 6 maps automatically

---

## Step 4 — Test

1. Enter **Training** mode
2. Press **Play**
3. Press **F1** to spawn a bot

### Hotkeys

| Key | Action |
|-----|--------|
| F1 | Spawn a bot (up to `BotConfig.MaxBots = 8`) |
| F2 | Remove all bots |
| F3 | Toggle AI on/off (freeze/unfreeze) |
| G | Toggle ESP overlay (names/health through walls) |
| J | Send bots to nearest JumpPad |
| K | Toggle crouch for all bots |
| L | Cycle difficulty mix: All Easy → All Medium → All Hard → Mixed |

### What You Should See

- Bots spawn with full avatars from 8 themed loadouts
- Bots patrol using NavMesh, chase when you enter their sight cone (45m, 80°)
- At engagement range (30m) they use smart 4-mode combat AI (disengage/evasive/orbit/advance)
- Weapon VFX: muzzle flash, bullet tracer, hit sparks
- "Killed by [BotName]" on death screen when a bot kills you
- Bot entries appear in end-of-match scoreboard

---

## Step 5 — Configuration

All parameters are `public static` fields in `BotConfig.cs` — tweakable at runtime.

### Key Settings

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MaxBots` | 8 | Max simultaneous bots |
| `WalkSpeed` | 7f | Normal speed (matches 4.3.8 player) |
| `EngageDistance` | 30m | Stop-and-fight range |
| `SightDistance` | 45m | Detection range (Hard: 60m, Easy: 25m) |
| `AimErrorDegrees` | 3.5° | Aim spread (Hard: 1.5°, Easy: 8°) |
| `ReactionDelay` | 0.2s | First-shot lag (Hard: 0.05s, Easy: 0.6s) |
| `RespawnDelay` | 5s | Time dead before respawning |
| `JumpSpeed` | 15f | Jump velocity (matches 4.3.8) |
| `JumpGravity` | 50f | Gravity (matches `EnviromentSettings.Gravity`) |
| `WeaponsPerBot` | 3 | Weapons carried (Quick Switch) |

---

## Architecture Notes

### Why `NavMeshAgent` with `updatePosition = false`?

Default `NavMeshAgent` snaps the `gameObject` to the NavMesh surface, which is baked below actual floor colliders — bots would immediately clip through the ground.

**Solution:** `agent.updatePosition = false` — agent only computes path queries. `BotNavigation` reads the agent's desired velocity as a steering direction and applies its own physics each frame.

**Important:** Set `updatePosition = false` **immediately** after `AddComponent<NavMeshAgent>()`, before the agent's first Update tick. If the agent runs even one tick with `updatePosition = true`, it snaps the position.

### Why `CharacterController` + Quake velocity together?

Pure velocity integration has edge cases on slopes and narrow corridors. Pure `CharacterController` fights with manual physics. Solution: Quake velocity accumulates `_velocity`, then `CC.Move(_velocity * dt)` is called with `CollisionFlags` to detect ground/wall/ceiling.

**Warp pattern:** For respawn and JumpPad launch, set `CC.enabled = false` → set `transform.position` → `CC.enabled = true`. This bypasses `CC`'s collision sweep for the instantaneous position change.

### Why a child trigger collider for JumpPads?

Bot root is on `RemotePlayer` layer (20). A trigger `CapsuleCollider` here intercepts weapon raycasts before they hit the avatar's `CharacterHitArea` bone colliders — bots become unhittable.

**Solution:** Trigger collider lives on a child object on `IgnoreRaycast` layer. `ForceField.cs` uses `GetComponentInParent<BotNavigation>()` to reach the bot regardless of which child fired.

### Damage path (player → bot)

```
Player weapon fires
  → Physics.Raycast (Default | LocalPlayer | RemotePlayer layers)
    → CharacterHitArea bone collider on bot avatar
      → hitArea.Shootable.ApplyDamage(DamageInfo)
        → BotController.ApplyDamage()
          → HP/Armor reduced; death + respawn if HP ≤ 0
```

Identical to hitting a real `RemotePlayer` — no reflection, no special casing.

### Kill attribution flow

```
Bot shoots player
  → LastBotAttacker = bot (+ timestamp)
  → Player dies → server sends OnPlayerSuicide (Training mode has no SplatGameEvent)
  → GameModeUtil.OnPlayerSuicide()
    → if LastBotAttacker != null && age < 3s → ShowBotKilledPlayerScreen()
    → else → "killed myself" (actual suicide or stale attacker)
```

```
Player jumps off map
  → LevelBoundary.OnTriggerExit()
    → LastBotAttacker = null (cleared)
  → Player dies → OnPlayerSuicide → no bot attacker → "killed myself"
```

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Bots fall through floor | NavMesh not baked; or `updatePosition` not set to `false` before first tick |
| Bots unhittable | Trigger collider is on root, not child `IgnoreRaycast` object |
| "Killed by bot" doesn't show | `LastBotAttacker` set after `ApplyDamage` instead of before; or `OnPlayerSuicide` not patched |
| Bots don't show on scoreboard | FpsGameMode injection block missing, or placed after `EndOfMatchStats.Instance.Data =` |
| Bots stuck at walls | NavMesh gap at that area; stuck recovery triggers in ~1s |
| Bots walk off cliff | Enable cliff avoidance; check NavMesh covers ledge edges |
| No weapon VFX | `AvatarDecorator` null; confirm avatar created via `AvatarBuilder.Instance.CreateRemoteAvatar()` |
| Spring grenade crash | Apply `QuickItemController` reflection stub in `BotController.Awake()` |

---

## Extending

### Add to Team Deathmatch
- Assign `TeamID` (RED/BLUE) in `BotController.Initialize()`
- Route bot deaths through `TeamGameMode` instead of Training-mode suicide path

### Add More Bot Behaviors
The FSM has 5 states: `Idle`, `Patrol`, `Chase`, `Combat`, `Dead`.
Add new `BotState` enum values + scoring in `BotController.UpdateCombatAI()`.

### Port BotRunner Utility AI
`BotRunner/AI/` has 8 scored behaviors with hysteresis that can replace the hand-coded FSM.

---

## File Change Log

| File | Type | Lines | Summary |
|------|------|-------|---------|
| `BotConfig.cs` | New | ~170 | All config + difficulty presets |
| `BotController.cs` | New | ~1500 | Brain, `IShootable`, smart combat AI, CC.Move |
| `BotNavigation.cs` | New | ~1000 | Quake physics, CC.Move, pathfinding, cliff avoidance |
| `BotWeaponHandler.cs` | New | ~300 | Shooting, Quick Switch, weapon VFX |
| `BotSpawner.cs` | New | ~435 | Spawning, hotkeys, ESP, difficulty cycling |
| `BotDebugLogger.cs` | New | ~50 | Per-category debug logging |
| `ForceField.cs` | Modified | +8 lines | Bot JumpPad trigger |
| `GameModeUtil.cs` | Modified | +6 lines | Suicide intercept with stale timeout |
| `FpsGameMode.cs` | Modified | +55 lines | End-of-match injection + respawn cap |
| `DeathArea.cs` | Modified | +6 lines | Bot kill-zone death (Session 6/7) |
| `LevelBoundary.cs` | Modified | +10 lines | Boundary death + stale attacker clear (Session 9b) |
