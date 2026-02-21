# Changelog — UberStrike 4.3 Bot Framework

All notable changes to this project are documented here.

---

## Native Unity 2022 Integration — Sessions 1–8 (2026-02-20 / 21)

This section documents the complete native integration that replaced the DLL injection approach.

---

### Session 8 — 2026-02-21 (Final Session)

**Focus:** Architecture fixes, reliability, and end-of-match polish

#### ✅ Fixes & Features

**`CharacterController` Removed (Critical Layer Fix)**
- Removed all `CharacterController` usage from `BotNavigation`
- Root cause: `CharacterController` on `RemotePlayer` layer (20) has no ground
  collision — it's simply how Unity's layer matrix was configured for UberStrike
- Replacement: manual velocity integration with direct `Transform.position` writes
- Result: bots stand on geometry correctly without any workaround

**Ground Raycast from Origin (Noclip Fix)**
- Changed ground detection from muzzle-eye height down to casting from `transform.position` origin downward
- Old approach occasionally cast from inside geometry, missing the ground
- New: `Physics.Raycast(origin, Vector3.down, out hit, 4f, groundMask)` from feet position
- Eliminated all remaining noclip-through-floor cases

**Splash Damage Deduplication**
- Added `ShotID` tracking to prevent the same explosion from dealing damage twice
- Cannon/Launcher/Splattergun explosions use radius-based damage; without this fix
  bots near walls received 2x damage from reflected raycasts
- `_processedShotIds` `HashSet<int>` cleared each frame; `ShotID` reused from `BotWeaponHandler`

**Scoreboard Kill Enforcement**
- Training mode applies a `-1 kills` penalty on every player death (suicide knockback)
- Added continuous write in `BotSpawner.Update()`:
  `GameState.LocalCharacter.Kills = (short)BotController.TotalBotKills;`
- Runs every frame when bots are alive — overrides the server's suicide math

**End-of-Match Stats Injection**
- `FpsGameMode.OnMatchEnd()` now injects bot data into `matchData` BEFORE `EndOfMatchStats.Instance.Data` is set
- Fixes: player's displayed kill count, zeroes out suicide penalty, adds bot MVP entries
- Bot entries appear in the end-of-match scoreboard with their kills, deaths, and a fake level

**Stats Reset Between Rounds**
- `BotController.ResetMatchStats()` called at round start clears `TotalBotKills`, `ScoreboardKills`, `ScoreboardDeaths`
- Prevents stats from accumulating incorrectly across multiple rounds

---

### Session 7 — 2026-02-21

**Focus:** Kill attribution, environment deaths, ceiling collision

#### ✅ Fixes & Features

**Kill Attribution Fix**
- `LastBotAttacker` was persisting too long — if a bot hit a player and the player
  died 10+ seconds later to a fall, the bot's name still appeared on the death screen
- Added `_lastBotAttackTime` timestamp; `LastBotAttacker` clears after 3 seconds
- `BotController.ShowBotKilledPlayerScreen()` checks timestamp before showing message

**Environment Death Handling**
- Bots falling below `BotConfig.DeathFloorY = -200f` now die with a proper respawn
- Previously: bots fell through the world and kept simulating, causing stuck bots
- Fix: `BotNavigation.Update()` checks `transform.position.y < BotConfig.DeathFloorY`
  and calls `_bot.TakeDamage(9999)` to force instant death + respawn

**Ceiling Collision**
- Added upward `SphereCast` to detect ceilings above bots
- Prevents bots from bunny-hopping through low ceilings
- When ceiling hit detected: `_velocity.y = Mathf.Min(_velocity.y, 0f)` clamps upward velocity

**Stale Attacker Cleanup**
- `LastBotAttacker` and `LastBotAttackBodyPart` are now nulled after being consumed
- Prevents double-triggering the "killed by bot" feedback in both `OnPlayerSuicide` and `BotSpawner.CheckPlayerDeath`

---

### Session 6 — 2026-02-21

**Focus:** Environment traversal and 9-map support

#### ✅ Fixes & Features

**Water Detection**
- Checks `WaterZone` trigger colliders on `IgnoreRaycast` layer
- Three water states: `None`, `Wading` (ankle), `Swimming` (fully submerged)
- Movement speeds: `WadeSpeedScale = 0.8f`, `SwimSpeedScale = 0.6f`
- Gravity in water: `WaterGravityScale = 0.1f`, upward buoyancy force aids surfacing

**Death Zone Handling**
- `DeathFloor` (`y < -200f`) triggers instant kill + respawn
- Added to all 9 supported maps (Temple, Arena, Docks, etc.)

**Accelerator Pad Integration**
- `ForceField.cs` distinguishes JumpPads from AcceleratorPads by name (`"accel"` substring)
- Accelerators apply a directional force without forcing bot airborne state

**JumpPad Landing**
- `LandingMomentumKeep = 0.3f` — 30% of horizontal launch velocity preserved on landing
- Prevents bots from stopping dead after a JumpPad lands them
- Models the "bunny hop flow" behavior players use after pad launches

**9-Map Support Confirmed**
- All maps that use `SpawnPointManager` now correctly provide spawn positions for bots
- `BotSpawner` falls back to `(Random.Range(-5,5), 1, Random.Range(-5,5))` if no spawn manager

---

### Session 5 — 2026-02-21

**Focus:** Real gear loadouts, physics rewrite

#### ✅ Fixes & Features

**Real Gear Loadouts (252 Items)**
- Replaced placeholder helmet/body IDs with 8 themed loadouts using actual item IDs from `BackendData.cs`:
  - 0: Ninja (SForce) — IDs 1101-1107
  - 1: Pirate (Cap'n Bradford) — IDs 1108-1113
  - 2: Knight Golden (Sir Magnus) — IDs 1167-1171
  - 3: Juggernaut — IDs 1272-1282
  - 4: Black Corps — IDs 1272-1282
  - 5: Tron Blue (T500) — IDs 1218-1230
  - 6: Vampire (Lucius the Cruel) — IDs 1339-1344
  - 7: Skeleton — IDs 1138-1144
- Per-loadout armor values from real `BackendData.cs` AP header comments

**Quake-Style Physics Rewrite**
- Full velocity-based movement: `_velocity` persists across frames
- Bunny hop: jump on landing maintains full horizontal momentum
- `GroundAcceleration = 15f`, `AirAcceleration = 3f` (from `EnviromentSettings`)
- `JumpGravity = 50f` (from `EnviromentSettings.Gravity`)

**Compile Fixes**
- Resolved all remaining .NET compatibility issues in the 5 bot scripts

---

### Session 4 — 2026-02-21

**Focus:** Movement accuracy, Quick Switch, crouch

#### ✅ Fixes & Features

**Movement 1:1 with 4.3.8**
- All speed/physics constants matched to `LevelEnviroment.cs` and `EnviromentSettings`:
  - `WalkSpeed = 7f`, `JumpSpeed = 15f`, `Gravity = 50f`
  - `GroundAcceleration = 15f`, `AirAcceleration = 3f`

**Aiming Fix**
- Bots were shooting at `target.position` (feet). Changed to `target.position + Vector3.up * 0.8f`
- Added 3.5° aim spread (`AimErrorDegrees`) for realistic miss behavior

**Wall-Stuck Recovery**
- Monitors bot position every 1 second
- If delta < 0.5m in 1s while in Chase/Combat and not on JumpPad: forces random direction movement
- Clears NavMesh path and sets new target after 3 stuck checks

**Crouch System**
- `BotNavigation.SetCrouching(bool)` adjusts `CapsuleCollider` height/center, speed scale
- `CrouchHeight = 0.9f`, `NormalHeight = 1.6f` (matched to original `CheckDuck`)
- `K` hotkey toggles crouch for all active bots

**Quick Switch**
- Each bot carries 3 weapons with independent fire rate timers
- After firing, switches to next ready weapon after `SWITCH_TIMEOUT = 0.2s`
- Allows higher overall DPS by alternating weapons without waiting for slowest cooldown

**Tiered Armor Points (AP)**
- Per-loadout AP values from `LoadoutArmorValues[]`:
  - Ninja: 60 AP, Pirate: 40 AP, Knight: 60 AP, Juggernaut: 80 AP
  - Black Corps: 80 AP, Tron: 55 AP, Vampire: 100 AP (capped), Skeleton: 30 AP

---

### Session 3 — 2026-02-20

**Focus:** UX integration (kill feed, scoreboard, JumpPads)

#### ✅ Fixes & Features

**Kill Feed Integration**
- Bot kills appear in HUD event feed: "[BotName] killed [PlayerName]"
- Player killing a bot: "[PlayerName] killed [BotName]"
- GameModeUtil.cs `OnPlayerSuicide()` intercepted to show bot killer name

**Scoreboard Sync**
- `CharacterInfo` RPC serialization for bot entries
- Bot kills/deaths track in `ScoreboardKills` / `ScoreboardDeaths`

**Topless Fix (Avatar)**
- `AvatarBuilder.CreateRemoteAvatar()` requires gear array `[Head, Face, Gloves, Upper, Lower, Boots, Holo]`
- Without `UpperBody` ID set, avatar rendered without torso mesh
- Fixed by ensuring all 7 slots populated in `GearLoadouts[]`

**JumpPad Support (Initial)**
- `ForceField.cs` `OnTriggerEnter` modified: checks `GetComponentInParent<BotNavigation>()`
- Calls `botNav.ApplyJumpPadForce(_direction.normalized * _force)`
- Added 3D audio on bot JumpPad activation

**Water Avoidance (Initial)**
- Bots detect water via trigger collider on `IgnoreRaycast` layer
- Simple avoidance: steer away from water zone center when detected

**End-of-Match Stats (Initial)**
- First pass of bot kill injection into `matchData` in `FpsGameMode.OnMatchEnd()`

---

### Session 2 — 2026-02-20

**Focus:** Hit detection and camera bugs

#### ✅ Fixes & Features

**Root `CapsuleCollider` Blocking Hits (Critical Fix)**
- Adding a trigger `CapsuleCollider` to the bot root was intercepting weapon raycasts
  before they reached the avatar's `CharacterHitArea` bone colliders
- Fix: moved trigger collider to a **child object** on `IgnoreRaycast` layer
  (`BotJumpPadTrigger` child) — this is why `QueryTriggerInteraction.Ignore` is used
  in `BotWeaponHandler.FireAtTarget()`

**Wrong `ShootMask`**
- `_combinedShootMask` was missing `LocalPlayer` layer (18)
- Bot raycasts weren't hitting the player
- Fix: `(1 << 0) | (1 << 18) | (1 << 20)` — Default + LocalPlayer + RemotePlayer

**Camera Culling Fix**
- Bot avatar meshes weren't rendering in the player's camera
- Root cause: `RemotePlayer` layer was excluded from camera's culling mask
- Fix: `LevelCamera.Instance.MainCamera.cullingMask |= (1 << 20)`

**NavMesh Agent `updatePosition = false`**
- Default `NavMeshAgent` snaps `gameObject` to NavMesh surface on first frame
- NavMesh surface is often below actual floor geometry
- Fix: set `agent.updatePosition = false` **immediately** after `AddComponent<NavMeshAgent>()`
  before the agent's first Update tick

---

### Session 1 — 2026-02-20

**Focus:** Initial implementation of 5 bot scripts

#### ✅ Features Added

**5 Core Scripts Created**
- `BotConfig.cs` — all parameters as public static fields for runtime tweaking
- `BotController.cs` — `IShootable` implementation, 5-state FSM (Idle/Patrol/Chase/Combat/Dead)
- `BotNavigation.cs` — initial physics system with `CharacterController` (later removed in Session 8)
- `BotWeaponHandler.cs` — raycast shooting with `DamageInfo` construction
- `BotSpawner.cs` — `[RuntimeInitializeOnLoadMethod]` auto-init, F1/F2/F3 hotkeys

**Avatar Creation**
- `AvatarBuilder.Instance.CreateRemoteAvatar()` with gear + skin color arrays
- Bot root on `RemotePlayer` (20) layer for weapon hit detection

**FSM States**
- `Idle` → wait for game start
- `Patrol` → random waypoint walking via NavMesh
- `Chase` → move toward player when detected in LOS
- `Combat` → stop and shoot when within `EngageDistance`
- `Dead` → ragdoll + respawn after `RespawnDelay`

**IShootable Implementation**
- `BotController implements IShootable` — same interface as `LocalPlayer`
- `IsVulnerable = true`, `IsLocal = false`
- `ApplyDamage(DamageInfo)` reduces Health + Armor with `ArmorAbsorptionRate = 0.66f`
- Damage path: weapon raycast → `CharacterHitArea.ApplyDamage()` → `IShootable.ApplyDamage()` — no reflection needed

---

## DLL Injection History (Archived)

The earlier DLL injection work (UnityIntegration/) is preserved as historical reference.
It was abandoned after identifying 6 fundamental architectural blockers. See `README.md` → *Why DLL Injection Was Abandoned*.

### Key DLL Injection Milestones

| Date | Work |
|------|------|
| Earlier | BotRunner headless simulation complete (20+ scenarios, utility AI) |
| Earlier | Initial DLL injection framework via SharpMonoInjector |
| Earlier | Phase 1–4: Basic spawning, DamageForwarder, CharacterHitArea discovery |
| 2026-02-03 | Damage API fully reverse-engineered (IShootable, DamageInfo, CharacterHitArea) |
| 2026-02-03 | Identified 6 fundamental blockers — approach abandoned |
| 2026-02-20 | Native Unity 2022 integration started |
| 2026-02-21 | Native Unity 2022 integration complete |

---

*For the BotRunner (headless simulation) release history, see `docs/ROADMAP.md`.*
