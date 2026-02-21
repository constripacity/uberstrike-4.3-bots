# Native Unity 2022 Bot Integration Guide

**For: UberStrike 4.3.8 Unity 2022 project** (`uber-client-4-3-8-unity_2022-bots-integration`)

This is a standalone guide for integrating the native bot system into any UberStrike Unity 2022 project.
The bots run entirely in-engine with no external processes or DLL injection required.

---

## Prerequisites

- Unity 2022 (any patch)
- UberStrike 4.3.8 Unity 2022 project (see `uber-client-4-3-8-unity_2022-bots-integration`)
- NavMesh baked on the map you want to use (see Step 3)

---

## Files Overview

### New Files to Add

| File | Destination | Purpose |
|------|-------------|---------|
| `Bots/BotConfig.cs` | `Assets/Scripts/Bots/` | All configuration constants |
| `Bots/BotController.cs` | `Assets/Scripts/Bots/` | Main AI brain (IShootable, FSM, avatar, stats) |
| `Bots/BotNavigation.cs` | `Assets/Scripts/Bots/` | Physics-based movement and pathfinding |
| `Bots/BotWeaponHandler.cs` | `Assets/Scripts/Bots/` | Raycast shooting, Quick Switch |
| `Bots/BotSpawner.cs` | `Assets/Scripts/Bots/` | Spawning, hotkeys, ESP overlay |
| `Bots/BotDebugLogger.cs` | `Assets/Scripts/Bots/` | Optional verbose debug logging |

### Files to Modify (Surgical Edits Only)

| File | Original Location | Change Summary |
|------|------------------|----------------|
| `ModifiedGameFiles/ForceField.cs` | `Assets/Scripts/LevelBehaviour/` | JumpPad bot trigger |
| `ModifiedGameFiles/GameModeUtil.cs` | `Assets/Scripts/GameModes/Util/` | Suicide→bot kill intercept |
| `ModifiedGameFiles/FpsGameMode.cs` | `Assets/Scripts/GameModes/` | End-of-match bot stat injection |

---

## Step 1 — Copy Bot Scripts

Copy all 6 files from `NativeUnity/Bots/` to `Assets/Scripts/Bots/` in your Unity project.

If the `Bots/` folder doesn't exist, create it.

```
Assets/
  Scripts/
    Bots/               ← create this if missing
      BotConfig.cs
      BotController.cs
      BotNavigation.cs
      BotWeaponHandler.cs
      BotSpawner.cs
      BotDebugLogger.cs
```

---

## Step 2 — Apply Modified Game Files

These are **replacement** copies of 3 existing game files with bot support added.
The modifications are marked with `// [BOT INTEGRATION]` comments in the source.

### Option A — Replace Files Directly (Easiest)

Replace the 3 existing files with the versions in `NativeUnity/ModifiedGameFiles/`:

```
NativeUnity/ModifiedGameFiles/ForceField.cs
    → Assets/Scripts/LevelBehaviour/ForceField.cs

NativeUnity/ModifiedGameFiles/GameModeUtil.cs
    → Assets/Scripts/GameModes/Util/GameModeUtil.cs

NativeUnity/ModifiedGameFiles/FpsGameMode.cs
    → Assets/Scripts/GameModes/FpsGameMode.cs
```

### Option B — Apply Changes Manually (If Your Files Have Diverged)

#### ForceField.cs — `OnTriggerEnter`

Find the existing `OnTriggerEnter` method and add the bot block after the player block:

```csharp
private void OnTriggerEnter(Collider collider)
{
    if (collider.tag == "Player")
    {
        // ... existing player code ...
    }
    else
    {
        // [BOT INTEGRATION] Check if this is a bot
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

#### GameModeUtil.cs — `OnPlayerSuicide`

Find `OnPlayerSuicide` and prepend this check at the very top:

```csharp
public static void OnPlayerSuicide(OnPlayerSuicideEvent ev)
{
    // [BOT INTEGRATION] Intercept suicide events caused by bot kills
    if (ev.PlayerInfo.ActorId == GameState.CurrentPlayerID && BotController.LastBotAttacker != null)
    {
        BotController.ShowBotKilledPlayerScreen();
        return;
    }
    // ... rest of original code ...
}
```

#### FpsGameMode.cs — `OnMatchEnd`

Find the line `EndOfMatchStats.Instance.Data = matchData;` and insert the bot injection block **just before it**:

```csharp
// [BOT INTEGRATION] Inject bot stats before EndOfMatchStats reads matchData
try
{
    int botKills = BotController.TotalBotKills;
    if (botKills > 0 && BotController.AllBots.Count > 0)
    {
        // Fix player's displayed kill count
        if (matchData.MostValuablePlayers != null)
        {
            int localCmid = PlayerDataManager.CmidSecure;
            foreach (var mvp in matchData.MostValuablePlayers)
            {
                if (mvp.Cmid == localCmid)
                {
                    mvp.Kills = botKills;
                    break;
                }
            }
        }
        // Fix PlayerStatsTotal kill count + zero out suicide penalty
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
                var botSummary = new StatsSummary();
                botSummary.Name = bot.BotName;
                botSummary.Kills = bot.ScoreboardKills;
                botSummary.Deaths = bot.ScoreboardDeaths;
                botSummary.Level = 10 + (bot.BotIndex % 20);
                botSummary.Cmid = -(900 + bot.BotIndex);
                botSummary.Team = TeamID.NONE;
                botSummary.Achievements = new Dictionary<byte, ushort>();
                matchData.MostValuablePlayers.Add(botSummary);
            }
        }
    }
}
catch (System.Exception) { }

// ← original line stays here:
EndOfMatchStats.Instance.Data = matchData;
```

---

## Step 3 — Bake NavMesh

The bots use Unity's `NavMeshAgent` for pathfinding but with `updatePosition = false`
(they compute their own physics). The NavMesh is required for pathfinding queries only.

1. Open the map scene in the Unity Editor
2. Go to **Window → AI → Navigation**
3. Click the **Bake** tab
4. Click **Bake**

> **Note:** The NavMesh only needs to be walkable area. Bots handle their own
> height/gravity via the custom physics system.

---

## Step 4 — Test

1. Start the game in the Unity Editor
2. Enter **Training** mode on any map
3. Press **Play** to start the session

### Hotkeys

| Key | Action |
|-----|--------|
| F1 | Spawn a bot (up to 8 max) |
| F2 | Remove all bots |
| F3 | Toggle bot AI on/off (freeze/unfreeze) |
| G | Toggle ESP overlay |
| J | Send all bots to nearest JumpPad |
| K | Toggle crouch for all bots |

### What You Should See

- Bots spawn with full avatar models from their themed loadouts
- Bots patrol the map using NavMesh paths
- When the player enters their sight range (45m, 80° cone), they chase
- At engagement distance (30m) they stop and fire with Quick Switch
- Bot kills appear in the HUD kill feed
- "Killed by [BotName]" shows on the death screen when a bot kills you
- Bot entries appear in the end-of-match scoreboard

---

## Step 5 — Configuration

All bot parameters are in `BotConfig.cs` as `public static` fields — tweakable at runtime
via the Unity Inspector or Console without recompiling.

### Key Configuration Values

| Parameter | Default | Description |
|-----------|---------|-------------|
| `MaxBots` | 8 | Maximum simultaneous bots |
| `WalkSpeed` | 7.0f | Normal movement speed (matches 4.3.8 player) |
| `EngageDistance` | 30m | Distance at which bots stop and shoot |
| `SightDistance` | 45m | Detection range |
| `SightAngle` | 80° | Field of view half-angle |
| `AimErrorDegrees` | 3.5° | Inaccuracy spread |
| `ReactionDelay` | 0.2s | Time before bot starts shooting after spotting player |
| `RespawnDelay` | 5s | Time before dead bot respawns |
| `MaxHealth` | 100 | Starting HP |
| `JumpSpeed` | 15f | Jump velocity (matches 4.3.8) |
| `JumpGravity` | 50f | Gravity (matches `EnviromentSettings.Gravity`) |

---

## Architecture Notes

### Why `NavMeshAgent` with `updatePosition = false`?

Unity's `NavMeshAgent` normally controls the `gameObject` position directly.
For UberStrike bots this breaks for two reasons:
1. The NavMesh surface is baked below actual floor geometry (below colliders)
2. We need Quake-style physics: persistent velocity, bunny hopping, air strafing

**Solution:** `agent.updatePosition = false` tells the agent to only compute path queries.
`BotNavigation` reads the **desired velocity** from the agent and uses it as a steering direction,
while applying its own gravity and horizontal velocity integration each frame.

### Why a child trigger collider for JumpPads?

The bot root is on `RemotePlayer` layer (20). A trigger `CapsuleCollider` on this layer
would intercept weapon raycasts **before** they reach the avatar's `CharacterHitArea` bone colliders,
making bots unhittable.

**Solution:** Add the trigger on a **child object** set to `IgnoreRaycast` layer.
`ForceField.cs` uses `GetComponentInParent<BotNavigation>()` to find bots regardless of which
collider fires the trigger.

### Why Training mode only?

UberStrike routes all damage events in Death Match through the server via `SendMethodToServer`.
Bot hits processed locally are unknown to the server — it would reject them as spoofed packets
or ignore them entirely.

Training mode processes damage entirely client-side. The `IShootable` interface allows bots
to be hit by the exact same code path as real players without any server involvement.

### Damage Path

```
Player weapon fires
    → Physics.Raycast (combined mask: Default | LocalPlayer | RemotePlayer)
        → hits CharacterHitArea bone collider on bot avatar
            → hitArea.Shootable.ApplyDamage(DamageInfo)
                → BotController.ApplyDamage()
                    → reduce Health/Armor by damage × (1 - ArmorAbsorption)
                    → trigger death/respawn if health ≤ 0
```

This is identical to hitting a real `RemotePlayer` — no special casing needed.

---

## Troubleshooting

### Bots fall through the floor

- Check that NavMesh is baked
- Verify `NavMeshAgent.updatePosition = false` is set **immediately** after `AddComponent<NavMeshAgent>()`
- `BotDebugLogger.cs` will log ground raycast hits if enabled

### Bots are unhittable

- Confirm the trigger collider is on a **child** `IgnoreRaycast` object, not the root
- Verify the avatar was created with `AvatarBuilder.Instance.CreateRemoteAvatar()`
  so `CharacterHitArea` bone colliders are present

### "Killed by [BotName]" doesn't appear

- Confirm `GameModeUtil.cs` has the `LastBotAttacker` check in `OnPlayerSuicide`
- Verify `LastBotAttacker` is set in `BotWeaponHandler` just before `ApplyDamage`

### Bots don't appear on end-of-match scoreboard

- Confirm `FpsGameMode.OnMatchEnd()` has the bot injection block **before** `EndOfMatchStats.Instance.Data = matchData`

### Bots get stuck

- Stuck recovery triggers automatically after 1 second of no movement in Chase/Combat
- Check that the NavMesh covers the map area (gaps cause pathfinding failures)

---

## Extending the Integration

### Adding to Team Deathmatch

1. Route bot damage to `TeamGameMode` instead of checking `TrainingMode`
2. Assign bots to a `TeamID` (RED or BLUE) in `BotController.Initialize()`
3. Add team color to avatar via `SkinColors[]`

### Adding More Bot Behaviors

The FSM currently has 5 states: `Idle`, `Patrol`, `Chase`, `Combat`, `Dead`.

To add behaviors (e.g., `Flanking`, `TakeCover`):
1. Add enum values to `BotState`
2. Add conditions in `BotController.UpdateFSM()`
3. Add a `NavMesh.SamplePosition()` call to find valid flank/cover positions

### Porting the BotRunner Utility AI

The old `BotRunner` has 8 scored behaviors with hysteresis (see `BotRunner/AI/`).
These could replace (or augment) the current hand-coded FSM for more adaptive behavior.

---

## File Change Log

| File | Type | Lines Changed | Summary |
|------|------|---------------|---------|
| `BotConfig.cs` | New | 171 | All config |
| `BotController.cs` | New | ~1500 | Brain, IShootable, avatar |
| `BotNavigation.cs` | New | ~1000 | Physics, pathfinding |
| `BotWeaponHandler.cs` | New | 307 | Shooting, Quick Switch |
| `BotSpawner.cs` | New | 436 | Spawning, hotkeys, ESP |
| `BotDebugLogger.cs` | New | ~120 | Debug logging |
| `ForceField.cs` | Modified | +8 lines | Bot JumpPad support |
| `GameModeUtil.cs` | Modified | +5 lines | Suicide intercept |
| `FpsGameMode.cs` | Modified | +50 lines | End-of-match injection |
