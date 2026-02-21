# UberStrike 4.3 Bot Framework

> **⚠️ NOTICE: The DLL injection approach (Mode 2) has been superseded.**
>
> A fully-working **Native Unity 2022 integration** was completed on 2026-02-20/21 across 8 sessions.
> Bots now run as in-engine MonoBehaviours with full avatars, Quake-style physics, and the same
> `IShootable` damage path used by real players.
>
> **→ New code lives in [`NativeUnity/`](./NativeUnity/)**  
> **→ Setup guide: [`NativeUnity/INTEGRATION_GUIDE.md`](./NativeUnity/INTEGRATION_GUIDE.md)**

---

![Status](https://img.shields.io/badge/Status-Native%20Unity%20Integration%20Complete-success)
![Unity](https://img.shields.io/badge/Unity-2022-black)
![Bots](https://img.shields.io/badge/Bots-Fully%20Working-brightgreen)
![.NET](https://img.shields.io/badge/.NET-10%20(BotRunner)-purple)

---

## 🎯 Project Overview

This repository documents the history and current state of bot development for UberStrike 4.3:

| Approach | Status | Location |
|----------|--------|----------|
| **BotRunner** (headless simulation) | ✅ Complete | `BotRunner/` |
| **DLL Injection** (Unity 2017 / SharpMonoInjector) | ❌ Abandoned — 6 fundamental blockers | `UnityIntegration/` (historical) |
| **Native Unity 2022** | ✅ **FULLY WORKING** | `NativeUnity/` |

---

## 🚀 Quick Start — Native Unity 2022 (Recommended)

```
1. Copy NativeUnity/Bots/*.cs  →  Assets/Scripts/Bots/ in your Unity project
2. Apply NativeUnity/ModifiedGameFiles/ patches to 3 game scripts
3. Bake NavMesh on your map (Window → AI → Navigation → Bake)
4. Enter Training mode, press Play
5. Press F1 to spawn a bot
```

Full guide: → [`NativeUnity/INTEGRATION_GUIDE.md`](./NativeUnity/INTEGRATION_GUIDE.md)

---

## 🏗️ Architecture Comparison

| Aspect | DLL Injection (Old) | Native Unity 2022 (New) |
|--------|---------------------|--------------------------|
| Bot process | External .NET app + SharpMonoInjector | In-engine `MonoBehaviour` |
| Visual representation | None / fallback capsule | Full avatar — 252+ gear items, 8 themed loadouts |
| Damage routing | Reflection + DamageForwarder | `IShootable` interface (same path as real players) |
| Movement | `CharacterController` (falls through floor) | SphereCast walls + Raycast ground + manual physics |
| Game mode | Death Match (server-dependent) | Training Mode (fully local) |
| Hit detection | Network packet spoofing | `Physics.Raycast` + `CharacterHitArea` bone colliders |
| Weapon switching | Not implemented | Full Quick Switch, 3 weapons, independent fire timers |
| Layer system | Unresolvable conflicts | RemotePlayer (20) for hits, IgnoreRaycast for triggers |
| Compilation | Compile DLL + inject + pray | Press Play in Unity Editor |

---

## ❌ Why DLL Injection Was Abandoned — The 6 Fundamental Blockers

These were not bugs — they were architectural impossibilities:

### 1. Layer Collision Matrix
`RemotePlayer` layer (20) has **no ground collision** in UberStrike's Physics matrix.
`CharacterController.Move()` passes through all geometry. Bots either fell through
floors or were unhittable via weapons — the layer system made both impossible simultaneously.

### 2. Reflection Overhead
Every API call required `GetComponent`, `GetField`, `GetMethod` with binding flags.
`DamageInfo` could not be referenced at compile time.
Result: fragile, slow, and broke with any Unity update.

### 3. Self-Collision Loops
Ground raycast hit the bot's own `SphereCollider` → bot flew into sky.
Wall-avoidance raycast hit bot's own weapon mesh → bot spun in circles.
Impossible to resolve without controlling the layer system.

### 4. .NET 3.5 Constraints
No LINQ, no `async`/`await`, broken `MemberInfo` equality operators, no
string interpolation. Severely limited what could be written in the injected DLL.

### 5. No Spawning System
Required either hijacking `LocalPlayer` (broke the real player) or cloning prefabs
via reflection with no `AvatarBuilder` access. Both approaches caused random crashes.

### 6. Server Dependency for Damage
Death Match routes **all damage via `SendMethodToServer`**. Bot hits could not
be processed locally — they required an active server round-trip. Training mode
only was the only viable path.

---

## ✅ What Works in Native Unity 2022

### Bot Files (`NativeUnity/Bots/`)

| File | Lines | Purpose |
|------|-------|---------|
| `BotConfig.cs` | 171 | Static config: movement (exact 4.3.8 values), weapons (6 types), 8 themed gear loadouts with real item IDs, per-loadout AP |
| `BotController.cs` | ~1500 | Main brain: `IShootable`, FSM, avatar creation, splash damage dedup, scoreboard, kill feed, end-of-match stats injection, match restart |
| `BotNavigation.cs` | ~1000 | Quake-style physics: persistent velocity (bunny hop!), `NavMeshAgent` pathfinding only (`updatePosition=false`), SphereCast walls, ground raycast from origin, ceiling collision, JumpPad/accelerator launch, water detection, death floor, stuck detection |
| `BotWeaponHandler.cs` | 307 | Quick Switch: 3 weapons, independent fire timers, 0.15s switch timeout, raycast shooting (3.5° aim error), tracer rendering |
| `BotSpawner.cs` | 436 | Auto-init, hotkeys (F1–F3/G/J/K), ESP overlay, player death detection, scoreboard kill enforcement |
| `BotDebugLogger.cs` | 122 | Optional verbose logging system |

### Modified Game Files (`NativeUnity/ModifiedGameFiles/`)

3 existing game files needed surgical additions:

| File | Change |
|------|--------|
| `ForceField.cs` | `OnTriggerEnter` — checks for `BotNavigation` on `RemotePlayer` layer, calls `ApplyJumpPadForce()` |
| `GameModeUtil.cs` | `OnPlayerSuicide()` intercept — replaces "killed myself" with "killed by [BotName]" when bot was attacker |
| `FpsGameMode.cs` | `OnMatchEnd()` — injects bot kills into `matchData` before `EndOfMatchStats` reads it |

### Hotkeys

| Key | Action |
|-----|--------|
| F1 | Spawn a bot (up to 8) |
| F2 | Remove all bots |
| F3 | Toggle AI on/off |
| G | Toggle ESP overlay (box, healthbar, snap line) |
| J | Send all bots to nearest JumpPad |
| K | Toggle crouch for all bots |

---

## 📂 Repository Structure

```
uberstrike-4.3-bots/
│
├── NativeUnity/                          # ✅ CURRENT: Native Unity 2022 Integration
│   ├── Bots/                             # 6 C# scripts — drop into Assets/Scripts/Bots/
│   │   ├── BotConfig.cs
│   │   ├── BotController.cs
│   │   ├── BotNavigation.cs
│   │   ├── BotWeaponHandler.cs
│   │   ├── BotSpawner.cs
│   │   └── BotDebugLogger.cs
│   ├── ModifiedGameFiles/                # Surgical additions to 3 existing game files
│   │   ├── ForceField.cs
│   │   ├── GameModeUtil.cs
│   │   └── FpsGameMode.cs
│   └── INTEGRATION_GUIDE.md             # Step-by-step setup guide
│
├── BotRunner/                            # ✅ HISTORICAL: Headless .NET simulation (still useful)
│   ├── BotRunner.csproj                  # .NET 10 project
│   ├── Bot/                              # State machine, utility AI, combat
│   ├── Scenarios/                        # 20+ deterministic test scenarios
│   └── ...
│
├── UnityIntegration/                     # ❌ ABANDONED: DLL injection approach
│   └── ...                              # Kept for reference — see blockers above
│
├── CHANGELOG.md                          # Full 8-session development history
├── README.md                             # This file
└── docs/
    ├── ARCHITECTURE.md
    └── ...
```

---

## 📜 Development History

The native integration was built in 8 sessions over 2 days:

| Session | Date | Key Work |
|---------|------|----------|
| 1 | 2026-02-20 | Initial 5 files, basic spawning, avatar creation |
| 2 | 2026-02-20 | Fixed root `CapsuleCollider` blocking hits, wrong `ShootMask`, camera culling |
| 3 | 2026-02-20 | Kill feed, scoreboard sync, topless fix, JumpPad support, water avoidance, end-of-match stats |
| 4 | 2026-02-21 | Movement 1:1 with 4.3.8, aiming fix, wall-stuck recovery, crouch, Quick Switch, tiered AP |
| 5 | 2026-02-21 | Real gear loadouts (252 items), compile fixes, Quake-style physics rewrite |
| 6 | 2026-02-21 | Water detection, death zones, accelerators, JumpPad landing, 9-map support |
| 7 | 2026-02-21 | Kill attribution fix, environment deaths, ceiling collision, stale attacker cleanup |
| 8 | 2026-02-21 | `CharacterController` removed (layer fix), splash damage dedup, ground raycast from origin, scoreboard kill enforcement, end-of-match stats injection |

Full details: → [`CHANGELOG.md`](./CHANGELOG.md)

---

## 🤝 What Was Kept from BotRunner

The headless BotRunner's **FSM behavior patterns** (patrol/chase/combat states, utility AI with hysteresis,
perception intervals) directly informed the design of the native bot AI. The behavior framework and
20+ test scenarios remain valuable for:
- Offline AI algorithm development without a running game
- Deterministic regression testing of AI decisions
- Prototyping new behaviors before implementing in-engine

The utility AI framework (8 behaviors with hysteresis) could still be ported to the native bots for
more advanced combat: flanking, cover-seeking, disengaging when low health.

---

## 📚 Documentation

| Document | Description |
|----------|-------------|
| [`NativeUnity/INTEGRATION_GUIDE.md`](./NativeUnity/INTEGRATION_GUIDE.md) | Setup guide for adding bots to any UberStrike Unity 2022 project |
| [`CHANGELOG.md`](./CHANGELOG.md) | Full 8-session development timeline |
| [`docs/ARCHITECTURE.md`](./docs/ARCHITECTURE.md) | Technical architecture (historical) |
| [`docs/SCENARIOS.md`](./docs/SCENARIOS.md) | BotRunner scenario catalog |

---

## ⚠️ Usage Guidelines

**✅ Permitted:**
- Offline AI research and development (Training mode only)
- Educational study of game architecture
- Private server experimentation with authorization
- Academic and research purposes

**❌ Not Permitted:**
- Public server disruption or cheating
- Unauthorized multiplayer interference
- Commercial exploitation without permission

---

## 📜 License

MIT License — See `LICENSE` for full details.

**Disclaimer**: This project is independently developed and not affiliated with the original UberStrike developers or publishers.

---

## Credits

Constripacity — founding this project and architecting the bot development across both approaches.
