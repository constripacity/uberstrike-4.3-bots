# UberStrike 4.3 Headless Bot Reference

This repository is a reference/inspiration project for building headless UberStrike 4.3 bots that speak the same Photon RPC surface as the retail client.

It is intentionally scoped and transparent, designed to:

- Demonstrate client-side architecture
- Enable offline experimentation
- Serve as a clean handoff to maintainers or contributors

## Project Structure

> **Note:** This is a high-level overview of the repository.
> For the complete, authoritative project tree generated from the codebase, see `Docs/PROJECT_TREE.md`.

```plaintext
uberstrike-4.3-bots/
├── BotRunner/ — Bot runner application and source
│   ├── BotRunner.csproj — .NET project definition (Targets .NET 10 preview)
│   ├── Program.cs — Application entry point and runtime loop
│   ├── Bot/ — Core bot control components
│   │   ├── BotBrain.cs — Bot state machine and orchestration
│   │   ├── BotCombat.cs — Combat behavior helpers
│   │   ├── AI/ — Utility AI selection and scoring
│   │   │   ├── BehaviorContext.cs — Context for utility scoring
│   │   │   ├── IUtilityBehavior.cs — Utility behavior contract
│   │   │   ├── UtilityAISelector.cs — Hysteresis + hold-time selector
│   │   │   └── UtilityBehaviors.cs — Utility wrappers for movement behaviors
│   │   ├── BotConfig.cs — Bot parameter configuration
│   │   ├── BotMovement.cs — Movement and navigation helpers
│   │   └── Behaviors/ — Pluggable bot behavior implementations
│   ├── Scenarios/ — Scenario execution orchestration
│   │   └── ScenarioRunner.cs — Scenario runner and registry
│   ├── State/ — Game and player state models
│   └── Utils/ — Shared utilities (Logger, SimulationTime, RunMetrics)
├── Extras/vision_demo/ — Optional Python vision demo (standalone)
├── Docs/ — Developer-facing guides (determinism, scenarios, behaviors)
├── scripts/ — Validation and benchmarking scripts (PowerShell & Bash)
├── LICENSE — Project license
├── README.md — Repository overview
└── .gitignore — Git ignore rules
```

## Operating Modes

The project currently supports two modes:

### M1 — Offline Demo (Implemented)
Uses a mock transport and scripted scenario to simulate a real match loop.

### Phase 2 — Offline Utility AI + Combat Intent (Implemented)
A fully deterministic offline simulation mode for testing bot behaviors, utility scores, and combat decision logic without requiring a live server.

### M2 — Online / Photon Integration (Intentionally Stubbed)
Reserved for authorized, private server environments only.

---

## First Run (Offline Demo – M1)

### Prerequisites

**Windows/Linux/macOS**
- **.NET 10 SDK (preview)** (required for building and running; matches `TargetFramework` in `BotRunner/BotRunner.csproj`)
- **PowerShell 7+** (for Windows scripts) or **bash** (for Linux/macOS)
- **python3** (only for optional scripts/determinism checks)
- **jq** (optional, for advanced JSON processing in validation scripts)

### Clone & Build

```bash
git clone https://github.com/constripacity/uberstrike-4.3-bots.git
cd uberstrike-4.3-bots
dotnet build
```

### Run the Offline Demo

```bash
# List all scenarios
dotnet run --project BotRunner -- --list-scenarios

# Run the default demo scenario
dotnet run --project BotRunner -- --scenario demo

# Run a specific behavioral scenario
dotnet run --project BotRunner -- --scenario duel
```

> **⚠️ Note:** The double dash `--` before `--scenario` is required so the argument is forwarded to the application.

---

## Submission Intent

This repository is a **reference implementation**, not a drop-in production bot.

**Goals:**
- Demonstrate headless client architecture for UberStrike 4.3.
- Provide a deterministic environment for AI experimentation.
- Offer a clean, decoupled handoff for future M2 integration.

**Non-goals:**
- Public server usage or "plug-and-play" cheating.
- Real-time competitive play against humans in this phase.
- Obfuscation or anti-detection mechanisms.

## Scenario Catalog

> **Note:** Scenario names are registered in `ScenarioRunner` and may evolve; use `--list-scenarios` to see the authoritative list for your build. A complete catalog is also maintained in `Docs/SCENARIOS.md`.

The framework includes 20+ scenarios covering:

### 🤖 AI Behavior Tests
- `duel` — 1v1 at varying distances to exercise chase/disengage behavior.
- `swarm` — survival against multiple enemies in waves.
- `retreat` — forces disengage decisions under pressure.
- `flipping_test` — enemy hovers at the engage threshold to detect oscillation.
- `flipping_regression` — fixed-step hysteresis stress test with oscillating offsets.

### 🎯 Combat Proficiency
- `weapon_test` — range-based weapon switching and efficiency.
- `moving_target` — velocity-based aim prediction validation.
- `shoot_window_test` — firing interval consistency and timing.
- `ammo_pressure` — resource management and reload logic under fire.

### 👥 Team Coordination
- `team_duel` — multi-bot focus fire and positioning testing.
- `spawn_wave` — survival against increasing waves of enemies.

### ⚡ Stress & Performance
- `many_actors` — spawns 10+ actors with independent movement for stress profiling.
- `load_spike_test` — rapid position update bursts to stress timing and serialization.
- `loop` — repeated position batches followed by MatchEnd to exercise lifecycle reset.
- `respawn_loop` — cycles the bot through death/respawn instructions.

### 🛡️ Failure & Recovery
- `bad_payload` — injects malformed RPC payloads to confirm graceful error handling.
- `reorder_drop` — replays out-of-order position updates with deterministic packet loss.
- `state_integrity_test` — forces MatchEnd → MatchStart transitions to validate state resets.

### 📊 Validation Suites
- `regression_suite` — deterministic bundle (bad payload, reorder/drop, duel, swarm, retreat, load spike) with pass/fail summary.
- `deterministic_suite` — curated fixed-step bundle (flipping_test, swarm_retreat_test, load_spike_test, state_integrity_test).

---

## Determinism & Validation

The framework guarantees **logical determinism**: same seed = identical AI decisions and outcomes.

### Determinism Checklist
- **Time source:** Everything uses `SimulationTime.Instance`; no decision logic calls wall-clock APIs.
- **Randomness:** All `Random` instances are seeded from the scenario configuration.
- **Metrics:** `RunMetrics` uses simulation ticks for all duration calculations.
- **Checksum:** Identical seeds produce identical decision and behavior metrics. The checksum excludes or normalizes wall-clock performance fields (execution time, GC counts) to avoid false mismatches.

### Validation Scripts
```powershell
# Run full validation suite
.\scripts\final-validation.ps1

# Run quick determinism check
.\scripts\validate-determinism.ps1

# Performance benchmark
.\scripts\benchmark.ps1
```
```bash
# Bash equivalents
./scripts/final-validation.sh
./scripts/validate-determinism.sh
./scripts/benchmark.sh
```
> The determinism scripts compare `ChecksumMd5` inside `run-summary.json`, not the whole file, so wall-clock performance does not affect pass/fail.

---

## Troubleshooting

### "Framework not found"
Ensure the **.NET 10 SDK** is installed. Verify with `dotnet --version`.

### Logs are too noisy
Set `LOG_LEVEL` to filter output:
```bash
LOG_LEVEL=warn dotnet run --project BotRunner -- --scenario demo
```
Levels: `error`, `warn`, `info` (default), `debug`, `trace`.

---

## License / Disclaimer

This project is provided for **educational and reference purposes only**.

- ✅ Use only in safe, authorized environments
- ✅ Keep bots clearly identifiable
- ✅ Respect server authority and rules
- ❌ Do not deploy in public or competitive environments

**No warranty is provided.**

## Optional Python Vision Demo (Standalone)

There is a small Python computer-vision demo under `Extras/vision_demo/`. It is **not wired into the .NET BotRunner** and runs entirely offline.
>>>>>>> origin/copilot/sub-pr-34
=======
There is a small Python computer-vision demo under `Extras/vision_demo/`. It is **not wired into the .NET BotRunner** and runs entirely offline.
>>>>>>> origin/copilot/sub-pr-34

### What it does
- Uses a RandomForest-based pixel classifier to flag “red enemy” blobs in frames.
- Reports approximate FPS and bounding boxes for detected blobs.

### How to run it
pip install -r Extras/vision_demo/requirements.txt
python Extras/vision_demo/vision_system/test_vision.py
>>>>>>> origin/copilot/sub-pr-34
```
=======
pip install -r Extras/vision_demo/requirements.txt
python Extras/vision_demo/vision_system/test_vision.py
>>>>>>> origin/copilot/sub-pr-34
```

### Optional wrapper usage
```python
from vision_system.vision_integration import VisionEnhancedBot
bot = VisionEnhancedBot()
result = bot.update_with_vision(frame)
```

> Run from the repository root (or set `PYTHONPATH=Extras/vision_demo`) so `vision_system` imports resolve.

### Determinism proof
- `run-summary.json` contains `ChecksumMd5`, computed only from logic/state fields (not wall-clock performance).
- Use `scripts/validate-determinism.sh` (or `.ps1`) to run the same scenario/seed multiple times and verify the checksum stays identical.
>>>>>>> origin/copilot/sub-pr-34
=======
> Run from the repository root (or set `PYTHONPATH=Extras/vision_demo`) so `vision_system` imports resolve.

### Determinism proof
- `run-summary.json` contains `ChecksumMd5`, computed only from logic/state fields (not wall-clock performance).
- Use `scripts/validate-determinism.sh` (or `.ps1`) to run the same scenario/seed multiple times and verify the checksum stays identical.
>>>>>>> origin/copilot/sub-pr-34
