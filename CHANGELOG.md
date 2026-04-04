# Changelog — UberStrike 4.3 Bot Framework

All notable changes to this project are documented here.

---

## Native Unity 2022 Integration — Sessions 1–10 (2026-02-20 – 2026-02-22)

---

### Session 10 — 2026-02-22 (Final Session)

**Focus:** Clean project rebuild for full 1:1 fidelity

#### ✅ Changes

**Clean Project Rebuild**
- Full copy of working UberStrike Unity 2022 project with all visual systems intact:
  shaders, particle effects, lightmaps, materials all restored to 1:1 with original
- All 30 bot-related files (6 scripts, 5 modified game files, NavMesh data, editor tools,
  boundary assets) layered on top of the clean project
- No `.git` connection to the source project — clean standalone deployment artifact
- Confirmed all 20 features working end-to-end in the rebuilt project

---

### Session 9b — 2026-02-22

**Focus:** LevelBoundary, cliff avoidance, kill attribution polish

#### ✅ Fixes & Features

**`LevelBoundary.cs` — Boundary Death System**
- New `OnTriggerExit()` implementation kills bots leaving map bounds
- Clears `BotController.LastBotAttacker` and `LastBotAttackBodyPart` before killing —
  boundary deaths count as environment deaths, not player kills
- Required on Gideon's Tower and Temple of the Raven where bot pathfinding could exit map geometry
- Added `LevelBoundary.mat` + `LevelBoundary.prefab` as new assets in the Unity project

**Cliff Avoidance Per Difficulty**
- NavMesh edge detection now throttled by difficulty level:
  - Hard: 90% chance to avoid detected cliff edges (aggressive but less cautious)
  - Medium: 50% chance (balanced)
  - Easy: 10% chance (mostly ignores cliffs — easier to kill)
- Avoidance uses `NavMesh.Raycast()` to detect drops > 3m ahead of the bot's path

**Kill Attribution Stale Timeout**
- `LastBotAttacker` now explicitly cleared after 3 seconds via `_lastBotAttackTime` timestamp
- Previously relied on `OnPlayerSuicide` consuming and clearing — edge cases existed where
  the flag wasn't consumed and persisted across respawn cycles

**Scoreboard Stats Reset Between Maps**
- `BotController.ResetMatchStats()` now wired to `OnSceneLoaded` in `BotSpawner`
- Prevents kills/deaths from one map bleeding into the next when Training mode reloads

---

### Session 9 — 2026-02-22

**Focus:** Weapon VFX, smart combat AI, difficulty system

#### ✅ Features Added

**Weapon VFX via `decorator.ShowShootEffect`**
- `BotWeaponHandler.FireAtTarget()` now calls the avatar decorator's built-in effect system:
  - Muzzle flash at gun barrel position
  - Bullet tracer trail from muzzle to hit point
  - Hit sparks at impact point (when hitting geometry)
- VFX path: `bot.AvatarDecorator?.GetActiveWeaponDecorator()?.ShowShootEffect(hits)`
- `hits` is a `List<NetworkProjectileHit>` built from the `RaycastHit`
- Non-explosive weapons only (Cannon / Launcher skip VFX, use existing projectile prefabs)

**Smart 4-Mode Combat AI**
- Replaced simple "stop and shoot" combat with a 4-behavior scoring system in `BotController.UpdateCombatAI()`:
  - **Disengage** — when health < 30% or player is too close (< 5m); backs away
  - **Evasive** — when under fire; strafes sideways relative to player
  - **Orbit** — mid-health, mid-range; circles player to avoid standing still
  - **Advance** — player at long range (> 40m); closes distance to engagement range
- Each behavior scores based on health, armor, range, and time since last hit
- Highest scoring behavior wins; hysteresis (0.2 threshold) prevents flickering

**Difficulty System (Easy / Medium / Hard)**
- Added `BotDifficulty` enum: `Easy`, `Medium`, `Hard`
- Per-difficulty configuration in `BotConfig`:
  | Stat | Easy | Medium | Hard |
  |------|------|--------|------|
  | Aim error | 8° | 3.5° | 1.5° |
  | Reaction delay | 0.6s | 0.2s | 0.05s |
  | Sight distance | 25m | 45m | 60m |
  | Cliff avoidance | 10% | 50% | 90% |
- `BotSpawner` assigns difficulty on spawn; **L key** cycles the difficulty mix:
  - All Easy → All Medium → All Hard → Mixed (one of each, cycling for 4+)
- `BotSpawner.OnGUI()` shows current difficulty mix in the HUD overlay

**`NavMeshBakeHelper.cs` (Editor Tool)**
- Menu item `Tools → UberStrike → Bake NavMesh All Maps`
- Iterates all 6 playable map scenes, loads each additively, bakes NavMesh, saves, unloads
- Avoids needing to manually bake each map individually

**Spring Grenade Fix**
- `QuickItemController` throws a `NullReferenceException` when bots are active because it
  tries to access the local player's slot data from a static context
- Fix: reflection patch in `BotController.Awake()` that stubs the offending virtual method
  — applied once, persists for the session

---

### Session 8 — 2026-02-21

**Focus:** CharacterController re-integration, reliability, end-of-match polish

#### ✅ Fixes & Features

**CharacterController Re-integrated (Architecture Fix)**
- Previous Quake-only approach had edge cases with slopes and narrow corridors
- Re-introduced `CharacterController` with correct layer settings to work alongside manual physics:
  - Capsule height 1.8m, radius 0.3m, center Y = 0.0 (feet at origin)
  - `CC.Move(velocity * Time.deltaTime)` applied each frame
  - `CollisionFlags` checked: `Below` = grounded, `Above` = ceiling hit, `Sides` = wall hit
  - Warp pattern: `CC.enabled = false` → `transform.position = newPos` → `CC.enabled = true`
    used for respawn and JumpPad launch to avoid CC fighting the position set

**Ground Raycast from Origin**
- Changed from eye-height downcast to `transform.position` origin downcast
- Old approach occasionally cast from inside geometry, missing the ground
- Source: `Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out hit, 4f, groundMask)`

**Splash Damage Deduplication**
- `ShotID` tracking via `_processedShotIds` `HashSet<int>` cleared each frame
- Prevents Cannon/Launcher/Splattergun explosions from dealing double damage to bots near walls

**Scoreboard Kill Enforcement**
- Training mode applies `-1 kills` penalty per player death (knockback suicide)
- `BotSpawner.Update()` continuously writes:
  `GameState.LocalCharacter.Kills = (short)BotController.TotalBotKills;`
- Runs every frame when bots are active

**End-of-Match Stats Injection**
- `FpsGameMode.OnMatchEnd()` injects bot data into `matchData` before `EndOfMatchStats.Instance.Data` is set
- Fixes: player kill count; zeroes suicide penalty; adds bot MVP entries with kills/deaths/level
- Server respawn cooldown capped to 5s (server default 8s in matchmaking mode)

**Match-End Freeze / Unfreeze with Auto-Ready**
- On match end: all bots frozen via `bot.Freeze()`, NavMesh paths cleared
- On next match start (`OnMatchStart` event): bots unfreeze and auto-ready via `bot.Unfreeze()`
- Stats reset between rounds: `TotalBotKills`, `ScoreboardKills`, `ScoreboardDeaths` cleared

---

### Session 7 — 2026-02-21

**Focus:** Kill attribution, environment deaths, ceiling collision

#### ✅ Fixes & Features

**Kill Attribution Fix — `_isEnvironmentDeath` Flag**
- `DeathArea.cs` `OnTriggerEnter` sets `_isEnvironmentDeath = true` on `BotController`
  before applying 1000 damage — this flag prevents the kill from being credited to any attacker
- Without this: bots that walked into lava/pits would credit the last player who shot them

**`LevelBoundary.cs` and `DeathArea.cs` — Bot Kill-Zone Handling**
- `DeathArea.cs`: `OnTriggerEnter()` checks `GetComponentInParent<BotController>()`,
  sets environment flag, applies 1000 damage
- `LevelBoundary.cs` (initial): `KillPlayer()` clears `LastBotAttacker` before applying damage

**Stale `LastBotAttacker` Cleanup**
- Added `_lastBotAttackTime` timestamp; `LastBotAttacker` invalid after 3 seconds
- `BotController.ShowBotKilledPlayerScreen()` checks timestamp before showing message
- `LastBotAttacker` + `LastBotAttackBodyPart` nulled after being consumed

**Ceiling Collision**
- Upward `SphereCast` to detect ceilings above bots
- When hit detected: `_velocity.y = Mathf.Min(_velocity.y, 0f)` clamps upward velocity
- Prevents bots from bunny-hopping through low ceilings

**Environment Death Floor**
- Bots falling below `BotConfig.DeathFloorY = -200f` die instantly
- `BotNavigation.Update()` checks `transform.position.y < BotConfig.DeathFloorY`
  → calls `_bot.TakeDamage(9999)`

---

### Session 6.5 — 2026-02-21

**Focus:** Crouch raycast fix

#### ✅ Fix

**Crouch Fix — `WORLD_MASK` + `QueryTriggerInteraction.Ignore`**
- Bots crouching through doorways were triggering ForceField/DeathArea triggers
- Root cause: all `Physics.Raycast` calls used default layer mask (includes triggers)
- Fix: all raycasts in `BotNavigation` switched to `WORLD_MASK` (geometry only)
  with `QueryTriggerInteraction.Ignore` — triggers never interfere with physics queries
- Ground detection, wall avoidance, ceiling, and cliff raycasts all updated

---

### Session 6 — 2026-02-21

**Focus:** Environment traversal and map coverage

#### ✅ Features Added

**Water Detection**
- Three water states: `None`, `Wading` (ankle), `Swimming` (fully submerged)
- Via `WaterZone` trigger on `IgnoreRaycast` layer
- Speed scales: Wade = 0.8×, Swim = 0.6×; gravity in water = 0.1×

**`DeathArea.cs` (Initial)**
- `OnTriggerEnter` checks for bot and applies 1000 damage → immediate death + respawn

**Accelerator Pad Integration**
- `ForceField.cs` distinguishes JumpPads from AcceleratorPads by `"accel"` substring in name
- Accelerators apply directional force without forcing airborne state

**JumpPad Landing Momentum**
- `LandingMomentumKeep = 0.3f` — 30% of horizontal launch velocity preserved on landing

**9-Map Spawn Support**
- All maps using `SpawnPointManager` provide spawn positions for bots
- Fallback: `(Random.Range(-5,5), 1, Random.Range(-5,5))` if no spawn manager

---

### Session 5 — 2026-02-21

**Focus:** Real gear loadouts, Quake physics rewrite

#### ✅ Features Added

**Real Gear Loadouts (252 Items)**
- 8 themed loadouts using actual item IDs from `BackendData.cs`:
  Ninja, Pirate, Knight, Juggernaut, Black Corps, Tron Blue, Vampire, Skeleton
- Per-loadout AP values from real header comments (30–100 AP range)

**Quake-Style Physics (Initial)**
- Full velocity-based movement: `_velocity` persists across frames
- Bunny hop: jump on landing preserves full horizontal momentum
- `GroundAcceleration = 15f`, `AirAcceleration = 3f`, `JumpGravity = 50f`

---

### Session 4 — 2026-02-21

**Focus:** Movement accuracy, Quick Switch, crouch

#### ✅ Features Added

- All speed/physics constants matched to UberStrike 4.3.8 (`WalkSpeed = 7f`, `JumpSpeed = 15f`)
- 3.5° aim error, wall-stuck recovery (1s position delta check)
- Crouch: `CrouchHeight = 0.9f`, K-key toggle, 70% speed scale
- Quick Switch: 3 weapons, independent timers, 0.2s switch timeout
- Tiered AP from real loadout data

---

### Session 3 — 2026-02-20

**Focus:** UX integration

#### ✅ Features Added

- Kill feed integration (HUD event feed for bot kills/deaths)
- Scoreboard sync (`CharacterInfo` RPC serialization for bot entries)
- Topless avatar fix (all 7 gear slots populated in `GearLoadouts[]`)
- JumpPad support (initial `ForceField.cs` modification)
- Water avoidance (initial — steer away from water zone)
- `FpsGameMode.OnMatchEnd()` — first pass of bot stat injection
- `GameModeUtil.OnPlayerSuicide()` — initial suicide intercept

---

### Session 2 — 2026-02-20

**Focus:** Hit detection and camera bugs

#### ✅ Critical Fixes

- Root `CapsuleCollider` moved to child `IgnoreRaycast` object (`BotJumpPadTrigger`)
  — fixes weapon raycasts being blocked before reaching `CharacterHitArea` bone colliders
- `_combinedShootMask` fixed: `(1<<0) | (1<<18) | (1<<20)` — was missing `LocalPlayer` layer
- Camera culling: `cullingMask |= (1 << (int)UberstrikeLayer.RemotePlayer)`
- `NavMeshAgent.updatePosition = false` set immediately after `AddComponent<NavMeshAgent>()`

---

### Session 1 — 2026-02-20

**Focus:** Initial implementation

#### ✅ Features Added

- 5 core scripts: `BotConfig`, `BotController`, `BotNavigation`, `BotWeaponHandler`, `BotSpawner`
- `BotController implements IShootable` — `ApplyDamage(DamageInfo)` reduces HP/Armor
- 5-state FSM: Idle / Patrol / Chase / Combat / Dead
- Avatar creation via `AvatarBuilder.Instance.CreateRemoteAvatar()`
- Bot root on `RemotePlayer` (20) layer
- `[RuntimeInitializeOnLoadMethod]` auto-init + F1/F2/F3 hotkeys

---

## DLL Injection History (Archived)

| Date | Work |
|------|------|
| Earlier | BotRunner headless simulation complete (20+ scenarios, utility AI) |
| Earlier | DLL injection framework via SharpMonoInjector |
| Earlier | Phase 1–4: Spawning, DamageForwarder, `CharacterHitArea` discovery |
| 2026-02-03 | Damage API fully reversed (`IShootable`, `DamageInfo`, `CharacterHitArea`) |
| 2026-02-03 | 6 fundamental blockers identified — approach abandoned |
| 2026-02-20 | Native Unity 2022 integration started |
| 2026-02-22 | Native Unity 2022 integration complete (11 sessions) |
