# UberStrike 4.3 Bot Framework

> **⚠️ NOTICE: The DLL injection approach (Mode 2) has been superseded.**
>
> A fully-working **Native Unity 2022 integration** was completed across 11 sessions (2026-02-20 – 2026-02-22).
> Bots run as in-engine MonoBehaviours with full avatars, Quake-style physics + CharacterController, smart combat AI,
> difficulty levels, weapon VFX, and the same `IShootable` damage path used by real players.
>
> **→ New code lives in [`NativeUnity/`](./NativeUnity/)**  
> **→ Setup guide: [`NativeUnity/INTEGRATION_GUIDE.md`](./NativeUnity/INTEGRATION_GUIDE.md)**

---

![Status](https://img.shields.io/badge/Status-Native%20Unity%20Integration%20Complete-success)
![Unity](https://img.shields.io/badge/Unity-2022-black)
![Bots](https://img.shields.io/badge/Bots-Fully%20Working-brightgreen)
![Sessions](https://img.shields.io/badge/Sessions-11-blue)

---

## 🎯 Project Overview

| Approach | Status | Location |
|----------|--------|----------|
| **BotRunner** (headless simulation) | ✅ Complete | `BotRunner/` |
| **DLL Injection** (Unity 2017 / SharpMonoInjector) | ❌ Abandoned — 6 fundamental blockers | `UnityIntegration/` (historical) |
| **Native Unity 2022** | ✅ **FULLY WORKING** | `NativeUnity/` |

---

## 🚀 Quick Start — Native Unity 2022 (Recommended)

```
1. Copy NativeUnity/Bots/*.cs        →  Assets/Scripts/Bots/
2. Apply NativeUnity/ModifiedGameFiles/ patches to 5 game scripts
3. Bake NavMesh (Window → AI → Navigation → Bake)
4. Enter Training mode → press Play
5. Press F1 to spawn a bot
```

Full guide: → [`NativeUnity/INTEGRATION_GUIDE.md`](./NativeUnity/INTEGRATION_GUIDE.md)

---

## 🏗️ Architecture Comparison

| Aspect | DLL Injection (Old) | Native Unity 2022 (New) |
|--------|---------------------|--------------------------|
| Bot process | External .NET + SharpMonoInjector | In-engine `MonoBehaviour` |
| Visual | None / fallback capsule | Full avatar, 252+ gear items, 8 themed loadouts |
| Damage routing | Reflection + DamageForwarder | `IShootable` (same path as real players) |
| Movement | `CharacterController` (falls through floor) | Quake velocity + `CC.Move()` + `CollisionFlags` |
| Game mode | Death Match (server-dependent) | Training Mode (fully local) |
| Hit detection | Network packet spoofing | `Physics.Raycast` + `CharacterHitArea` bone colliders |
| Weapon switching | Not implemented | Quick Switch, 3 weapons, independent fire timers |
| Weapon VFX | None | Muzzle flash, bullet trails, hit sparks |
| Combat AI | Simple chase/shoot | 4-mode smart AI (disengage/evasive/orbit/advance) |
| Difficulty | None | Easy / Medium / Hard per-bot with L-key cycling |
| Layer system | Unresolvable conflicts | RemotePlayer (20) for hits, IgnoreRaycast for triggers |
| Compilation | Compile DLL + inject + pray | Press Play in Unity Editor |

---

## ❌ Why DLL Injection Was Abandoned — The 6 Fundamental Blockers

| Blocker | Details |
|---------|---------|
| **Layer Collision Matrix** | `RemotePlayer` layer (20) has no ground collision. `CharacterController.Move()` passes through all geometry. Bots either fell through floors OR were unhittable — never both. |
| **Reflection overhead** | Every API call required `GetComponent`, `GetField`, `GetMethod` with binding flags. `DamageInfo` unreferenceable at compile time. |
| **Self-collision loops** | Ground raycast hit bot's own `SphereCollider`. Wall-avoidance hit bot's own weapon mesh. |
| **.NET 3.5 constraints** | No LINQ, no `async`/`await`, broken `MemberInfo` equality operators. |
| **No spawning system** | Required hijacking `LocalPlayer` or cloning prefabs via reflection. No `AvatarBuilder` access. |
| **Server dependency** | Death Match routes all damage via `SendMethodToServer`. Bot hits cannot be processed locally. |

---

## ✅ Confirmed Working Features (20)

| # | Feature |
|---|---------|
| 1 | Bot spawning with full player avatars (8 themed loadouts) |
| 2 | `IShootable` damage — player shoots bots, bots shoot player |
| 3 | Quake-style velocity physics with bunny hopping |
| 4 | `CharacterController` integration (`CC.Move()`, warp pattern, proper capsule settings) |
| 5 | NavMesh pathfinding with fallback movement |
| 6 | Quick-switch weapon spam (3 weapons per bot, independent fire timers) |
| 7 | **Weapon VFX** — muzzle flash, bullet trails, hit sparks via `decorator.ShowShootEffect` |
| 8 | **Smart combat AI** — 4 behavior modes: disengage / evasive / orbit / advance |
| 9 | **Difficulty system** — Easy / Medium / Hard with per-difficulty accuracy, reactions, cliff avoidance |
| 10 | Water movement (Monkey Island, Lost Paradise 2) |
| 11 | JumpPad / Accelerator support (all playable maps) |
| 12 | Crouch with K toggle |
| 13 | Death zones — `DeathArea` (lava, pits) + `LevelBoundary` (Gideon's Tower, Temple of the Raven) |
| 14 | Kill attribution — environment vs player vs bot + 3s stale timeout |
| 15 | Scoreboard integration (kills, deaths, weapon display, skull icons) |
| 16 | End-of-match stats injection (fix player kills, zero suicides, add bot MVP entries) |
| 17 | Match-end freeze / unfreeze with auto-ready |
| 18 | Spring grenade fix (reflection patch for `QuickItemController`) |
| 19 | **Cliff avoidance** per difficulty (Hard 90%, Medium 50%, Easy 10%) |
| 20 | Map shaders, particles, and lightmaps 1:1 with original |

---

## 🎮 Hotkeys

| Key | Action |
|-----|--------|
| F1 | Spawn a bot (up to 8) |
| F2 | Remove all bots |
| F3 | Toggle AI on/off |
| G | Toggle ESP overlay (names/health through walls) |
| J | Send all bots to nearest JumpPad |
| K | Toggle crouch |
| L | Cycle difficulty mix (All Easy → All Medium → All Hard → Mixed) |

---

## 📂 Repository Structure

```
uberstrike-4.3-bots/
│
├── NativeUnity/                          # ✅ CURRENT: Native Unity 2022 Integration
│   ├── Bots/                             # 6 C# scripts — drop into Assets/Scripts/Bots/
│   │   ├── BotConfig.cs                  # Config: movement, weapons, gear, difficulty
│   │   ├── BotController.cs              # Main brain: IShootable, FSM, smart combat AI
│   │   ├── BotNavigation.cs              # Quake physics + CC.Move + pathfinding
│   │   ├── BotWeaponHandler.cs           # Quick Switch + weapon VFX
│   │   ├── BotSpawner.cs                 # Spawning, hotkeys, ESP, difficulty cycling
│   │   └── BotDebugLogger.cs             # Per-category debug logging
│   ├── ModifiedGameFiles/                # Surgical additions to 5 existing game files
│   │   ├── ForceField.cs                 # JumpPad bot trigger
│   │   ├── GameModeUtil.cs               # Suicide → bot kill intercept
│   │   ├── FpsGameMode.cs                # End-of-match stats injection
│   │   ├── DeathArea.cs                  # Bot kill-zone handling (lava, pits)
│   │   └── LevelBoundary.cs              # Boundary death + stale attacker clear
│   └── INTEGRATION_GUIDE.md             # Step-by-step setup guide
│
├── BotRunner/                            # ✅ HISTORICAL: Headless .NET simulation
│   ├── BotRunner.csproj                  # .NET 10 project
│   ├── Bot/                              # FSM, utility AI, combat
│   ├── Scenarios/                        # 20+ deterministic test scenarios
│   └── ...
│
├── UnityIntegration/                     # ❌ ABANDONED: DLL injection approach
│   └── ...                              # Kept for reference
│
├── CHANGELOG.md                          # Full 11-session development history
└── README.md                             # This file
```

---

## 📐 Bot Scripts Summary

| File | Lines | Purpose |
|------|-------|---------|
| `BotConfig.cs` | ~170 | Movement (exact 4.3.8 values), 6 weapon types, 8 gear loadouts, difficulty settings |
| `BotController.cs` | ~1500 | `IShootable`, 5-state FSM, avatar creation, `CC.Move()` + warp, smart 4-mode combat AI, scoreboard, kill attribution, end-of-match injection |
| `BotNavigation.cs` | ~1000 | Quake velocity, `CC.Move()` + `CollisionFlags`, NavMesh steering, JumpPad launch, water, cliff avoidance |
| `BotWeaponHandler.cs` | ~300 | Quick Switch 3 weapons, difficulty-based aim error, VFX via `decorator.ShowShootEffect` |
| `BotSpawner.cs` | ~435 | Auto-init, F1–F3/G/J/K/L hotkeys, ESP overlay, difficulty cycling, scoreboard enforcement |
| `BotDebugLogger.cs` | ~50 | Centralized logging with per-category filters (AI, nav, combat, weapons) |

### Modified Game Files

| File | Change |
|------|--------|
| `ForceField.cs` | `OnTriggerEnter` — `GetComponentInParent<BotNavigation>()` → `ApplyJumpPadForce()` |
| `GameModeUtil.cs` | `OnPlayerSuicide()` — intercept with 3s stale timeout — show "killed by [BotName]" |
| `FpsGameMode.cs` | `OnMatchEnd()` — inject bot kills, zero suicides, add bot MVP entries. Cap respawn to 5s. |
| `DeathArea.cs` | `OnTriggerEnter` — sets `_isEnvironmentDeath` flag, applies 1000 damage to bots |
| `LevelBoundary.cs` | `OnTriggerExit()` — kills bots leaving map bounds, clears `LastBotAttacker` |

---

## 📜 Development Timeline

| Session | Date | Key Work |
|---------|------|----------|
| 1 | 2026-02-20 | Initial 5 files, basic spawning, avatar creation, 5-state FSM |
| 2 | 2026-02-20 | Fixed root `CapsuleCollider` blocking hits, wrong `ShootMask`, camera culling |
| 3 | 2026-02-20 | Kill feed, scoreboard sync, topless fix, JumpPad, water avoidance, end-of-match stats |
| 4 | 2026-02-21 | Movement 1:1 with 4.3.8, aiming fix, wall-stuck recovery, crouch, Quick Switch, tiered AP |
| 5 | 2026-02-21 | Real gear loadouts (252 items), compile fixes, Quake physics rewrite |
| 6 | 2026-02-21 | Water detection, `DeathArea`, accelerators, JumpPad landing, 9-map support |
| 6.5 | 2026-02-21 | Crouch fix — `WORLD_MASK` on all raycasts + `QueryTriggerInteraction.Ignore` |
| 7 | 2026-02-21 | Kill attribution (`_isEnvironmentDeath`), stale `LastBotAttacker` (3s), ceiling collision |
| 8 | 2026-02-21 | **CC re-integrated** (`CC.Move` + `CollisionFlags`, warp pattern), splash dedup, scoreboard enforcement, stats reset |
| 9 | 2026-02-22 | **Weapon VFX**, **smart 4-mode combat AI**, **difficulty system** (Easy/Medium/Hard), `NavMeshBakeHelper` |
| 9b | 2026-02-22 | **`LevelBoundary` fix**, kill attribution stale timeout, **cliff avoidance** per difficulty, stats reset between maps |
| 10 | 2026-02-22 | **Clean project rebuild** — shaders, particles, lightmaps 1:1 + all 30 bot files layered on top |

Full details: → [`CHANGELOG.md`](./CHANGELOG.md)

---

## 🤝 What Was Kept from BotRunner

The headless BotRunner's **FSM behavior patterns** (patrol/chase/combat states, utility AI with hysteresis, perception intervals) directly informed the native bot AI design. The utility AI framework (8 scored behaviors) could be ported as a replacement for the hand-coded FSM for more advanced behavior: flanking, cover-seeking, disengaging when low health.

The 20+ deterministic test scenarios remain useful for offline AI algorithm development without a running game instance.

---

## ⚠️ Usage Guidelines

**✅ Permitted:** Offline AI research (Training mode only), educational study, private server experimentation with authorization, academic purposes.

**❌ Not Permitted:** Public server disruption, unauthorized multiplayer interference, commercial exploitation without permission.

---

## 📜 License

MIT License. This project is independently developed and not affiliated with the original UberStrike developers or publishers.
