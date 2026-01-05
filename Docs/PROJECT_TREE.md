# Project Structure (Architectural Overview)

This document describes the **intentional, logical structure** of the UberStrike 4.3 Headless Bot Framework.

It is **not** a raw filesystem dump.
It exists to help developers quickly understand:
- where responsibilities live
- how systems are layered
- where to extend or integrate safely

For exact file names and implementations, rely on IDE navigation or search.

---

## Top-Level Layout

| Directory | Purpose |
|---------|--------|
| `BotRunner/` | Core executable and all bot logic |
| `Docs/` | Developer documentation and handoff material |
| `scripts/` | Validation, determinism checks, benchmarks |
| `LICENSE` | Project license |
| `README.md` | Entry-point overview |
| `.gitignore` | Build and artifact exclusions |

---

## BotRunner (Core Runtime)

This is the **entire offline bot system**.  
Everything under this directory is deterministic, testable, and M1-safe.

### Entry Point
- **`Program.cs`**
  - Application bootstrap
  - Scenario selection and execution
  - Simulation loop
  - `run-summary.json` generation
  - Validation orchestration

---

### Bot (AI & Decision Making)

This directory contains **all intelligence**.

#### Core Orchestration
- **`BotBrain.cs`**
  - Finite-state machine (Joining → Spawning → Roaming → Engaging → Dead)
  - High-level decision ownership
  - Bridges sensing → decision → action

#### Utility AI
- **`AI/`**
  - `BehaviorContext.cs` — Immutable snapshot of the bot’s world
  - `IUtilityBehavior.cs` — Scored behavior contract
  - `UtilityAISelector.cs` — Hysteresis, stickiness, min-hold logic
  - `UtilityBehaviors.cs` — Utility wrappers for movement behaviors

Purpose:
> Stable, human-like decision making without oscillation.

#### Action Resolution
- **`ActionPipeline.cs`**
  - Resolves movement + combat proposals
  - Produces a single coherent `ActionFrame`
  - Prevents contradictory actions (e.g. flee + shoot)

- **`ActionFrame.cs`**
  - Immutable representation of a single decision tick

#### Combat Logic
- **`Combat/`**
  - `CombatIntent.cs` — Data-only combat decision
  - `CombatIntentGenerator.cs` — Aim, fire, reload decisions
  - `WeaponRangeEvaluator.cs` — Distance-based weapon logic
  - `LineOfSightSimulator.cs` — Offline LOS approximation

> ⚠️ Combat is **log-only** in Phase 2 (no weapon RPCs).

#### Movement & Behaviors
- **`Behaviors/`**
  - Wander, Chase, Disengage, Strafe, OrbitStrafe, Hold, etc.
- **`BotMovement.cs`**
  - Movement helpers and smoothing
- **`BotConfig.cs`**
  - All tunable parameters (difficulty, reaction time, hysteresis)

---

### State (World & Match Models)

- **`WorldState.cs`**
  - Thread-safe player registry
- **`MatchState.cs`**
  - Match lifecycle, spawn windows
- **`PlayerState.cs`**
  - Position, health, team, visibility
- **`SimulationTime.cs`**
  - Deterministic time source (no wall-clock logic)

---

### Scenarios (Deterministic Testing)

- **`ScenarioRunner.cs`**
  - Scenario registry and dispatcher
- Individual scenario classes:
  - Duel, Swarm, Retreat, FlippingTest, LoadSpike, ManyActors, etc.
- Suite runners:
  - `regression_suite`
  - `deterministic_suite`

Purpose:
> Prove behavior correctness, determinism, and stability.

---

### Utilities

- **`Logger.cs`** — Structured logging with levels
- **`RunMetrics.cs`** — Decision metrics and summaries
- **Rate limiting & helpers**

---

## Docs (Developer Handoff)

| File | Purpose |
|----|-------|
| `DeveloperGuide.md` | Architecture and mental model |
| `PROJECT_TREE.md` | This document |
| `SCENARIOS.md` | Scenario catalog and intent |
| `AddingBehavior.md` | How to extend the AI |
| `M2_Integration.md` | Photon integration notes (stubbed) |

---

## scripts (Validation & Proof)

- `final-validation.ps1 / .sh` — Full validation suite
- `validate-determinism.ps1 / .sh` — Multi-run reproducibility checks
- `benchmark.ps1 / .sh` — Performance profiling

These scripts provide **empirical proof**, not just claims.

---

## What Is Intentionally Omitted

The following are **excluded on purpose**:

- `bin/`, `obj/` — Build artifacts
- Generated logs and summaries
- Temporary validation outputs
- Any production credentials or endpoints

This keeps the repository:
- clean
- auditable
- safe to share

---

## Design Philosophy Summary

- **Offline-first**
- **Deterministic by design**
- **Clear M1 / M2 separation**
- **Readable over clever**
- **Reference > production bot**

This repository is meant to be **understood**, not obfuscated.
