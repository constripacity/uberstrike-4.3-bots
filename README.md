# UberStrike 4.3 Headless Bot Reference

This repository is a reference/inspiration project for building headless UberStrike 4.3 bots that speak the same Photon RPC surface as the retail client.

It is intentionally scoped and transparent, designed to:

- Demonstrate client-side architecture
- Enable offline experimentation
- Serve as a clean handoff to maintainers or contributors

## Project Structure

```plaintext
uberstrike-4.3-bots/
├── BotRunner/ — Bot runner application and source
│   ├── BotRunner.csproj — .NET project definition
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
│   │       ├── ChaseNearestEnemyBehavior.cs — Chase nearest target behavior
│   │       ├── DisengageBehavior.cs — Disengage/retreat behavior
│   │       ├── IBotBehavior.cs — Behavior interface contract
│   │       ├── HoldPositionBehavior.cs — Hold-in-place behavior
│   │       ├── StrafeBehavior.cs — Lateral strafe around enemies
│   │       └── WanderBehavior.cs — Random wandering behavior
│   │   ├── Combat/ — Combat intent generation
│   │   │   ├── CombatIntent.cs — Generated combat intent record
│   │   │   └── CombatIntentGenerator.cs — Distance-based firing heuristics
│   ├── Config/ — Application and scenario configuration
│   │   ├── AppSettings.cs — App-level settings model
│   │   ├── ScenarioConfig.cs — Scenario configuration model
│   │   ├── appsettings.Local.json — Local configuration overrides
│   │   └── appsettings.json — Default configuration
│   ├── Docs/ — Internal documentation and planning notes
│   │   ├── LoggingPlan.txt — Logging approach notes
│   │   ├── MessageToCline_OptionA.md — Communication draft
│   │   ├── README.Logging.md — Logging reference
│   │   └── TODO_M2.md — Milestone tasks
│   ├── Networking/ — Transport connectors and RPC plumbing
│   │   ├── ITransportConnection.cs — Transport interface
│   │   ├── MockTransportConnection.cs — Mock transport implementation
│   │   ├── NetEvent.cs — Network event types
│   │   ├── NetReliability.cs — Reliability helper
│   │   ├── Payload/ — Payload representations
│   │   │   ├── ByteConverter.cs — Byte conversion helpers
│   │   │   ├── PayloadSchemas.cs — Payload schema definitions
│   │   │   └── ShortVector3.cs — Compact vector struct
│   │   ├── Photon3TransportConnection.cs — Photon v3 transport connector
│   │   ├── PhotonConnection.cs — Photon transport implementation
│   │   ├── RpcMapping.cs — RPC mapping definitions
│   │   ├── RpcRouter.cs — RPC routing logic
│   │   ├── RpcSender.cs — RPC sending utilities
│   │   └── TransportConnectionFactory.cs — Transport factory
│   ├── Scenarios/ — Scenario execution orchestration
│   │   └── ScenarioRunner.cs — Scenario runner
│   ├── State/ — Game and player state models
│   │   ├── MatchState.cs — Match state snapshot
│   │   ├── PlayerSnapshot.cs — Player snapshot data
│   │   ├── PlayerState.cs — Player state model
│   │   ├── PlayerStub.cs — Player stub representation
│   │   └── WorldState.cs — World state model
│   └── Utils/ — Shared utilities
│       ├── Logger.cs — Logging helper
│       ├── RateLimiter.cs — Rate limiting helper
│       └── RunMetrics.cs — Metrics tracking
├── LICENSE — Project license
├── README.md — Repository overview
├── .gitignore — Git ignore rules
└── .git_changes.txt — Local change log snapshot

Not documented intentionally:
- .git/ (version control metadata)
- .git_changes.txt (local change snapshot)
```

**Not documented intentionally:** `.git/` (version control metadata), `.git_changes.txt` (local change snapshot).

## Operating Modes

The project currently supports two modes:

### M1 — Offline Demo (Implemented)
Uses a mock transport and scripted scenario to simulate a real match loop.

### Phase 2 — Offline Utility AI + Combat Intent (New)
Adds movement Utility AI scoring (wander, chase, disengage, strafe) with hysteresis and minimum hold, plus offline combat intent generation during engaging. No Photon changes are required to validate.

### M2 — Online / Photon Integration (Intentionally Stubbed)
Reserved for authorized, private server environments only.

---

## First Run (Offline Demo – M1)

### Prerequisites

**Windows**
- .NET 10 runtime (no additional installs required)
- The project is currently targeted to `net10.0` to match the runtime used during development

### Clone & Build

```bash
git clone https://github.com/constripacity/uberstrike-4.3-bots.git
cd uberstrike-4.3-bots
dotnet build
```

### Run the Offline Demo

```bash
dotnet run --project BotRunner -- --scenario demo
```

> **⚠️ Note:** The double dash `--` before `--scenario` is required so the argument is forwarded to the application.
>
> Additional deterministic scenarios:
> - `duel` — fixed-cadence enemy path to exercise chase/disengage behavior.
> - `respawn_loop` — cycles the bot through death/respawn instructions to stress spawn handling.
> - `loop` — runs repeated position batches followed by MatchEnd (and an optional second cycle) to exercise lifecycle reset.
> - `flipping_test` — enemy hovers at the engage threshold to detect oscillation.
> - `state_integrity_test` — forces MatchEnd → MatchStart transitions to validate resets.
> - `swarm_retreat_test` — 1v3 pressure that rewards disengage/hold choices.
> - `load_spike_test` — 50 rapid position updates to stress timing.
> - `regression_suite` — deterministic bundle (duel, swarm, retreat, load spike) with pass/fail summary and exit code.
>
> Example:
> ```bash
> dotnet run --project BotRunner -- --scenario duel
> ```
>
> Scenarios can also be configured in `BotRunner/Config/appsettings.json` (`Scenario` section) where you can change the seed, enemy count, and step durations without code changes.

### Loop scenario quick start

```bash
dotnet run --project BotRunner -- --scenario loop
```

### Seeded run (deterministic)

```bash
dotnet run --project BotRunner -- --scenario demo --seed 123
```

### What You Should See

If successful, the terminal will show a deterministic sequence similar to:

```
[Scenario] Starting demo sequence...
[Transport:Mock] Inject code=52 payloadType=Object[] sender=-1
[RPC] MatchStart -> match running
[Scenario] Injected SpawnAllowed for bot
[Bot] Spawn requested at <10, 0, 10>
[Transport:Mock] Send code=50 reliability=Unreliable payloadType=Byte[]
```

**Success looks like:**
1. The bot joins the match
2. Receives a spawn instruction
3. Transitions to roaming/engaging
4. Emits repeated `PositionUpdate` packets via the mock transport

This confirms that the FSM, state propagation, timing model, and RPC routing are all functioning in M1.

---

## How to Validate Phase 2

Run the demo with a fixed seed to exercise utility behavior selection and combat intent generation:

```bash
dotnet run --project BotRunner -- --scenario demo --seed 123
```

You should see INFO logs showing behavior choices (wander/chase/disengage/strafe) and DEBUG logs for combat intents while engaging. The run summary (`run-summary.json`) now reports the current behavior, behavior switch count, and combat intent counts.

---

## Run Summary Output
Each offline run emits `run-summary.json` (next to the executable) on shutdown. It captures:

- Scenario name and seed
- Time spent in each bot FSM state
- Position updates sent
- Network ticks received
- Current utility behavior name
- Behavior metrics (switch count, switches/minute, and time spent per behavior)
- Combat metrics (intent counts and shoot/no-shoot tallies)

Use it to compare deterministic harness runs or validate new scenario timings.

---

## Behavior Tuning

Humanization knobs live under `Bot.Config` in `BotRunner/Config/appsettings.json`:

- `ReactionDelayMs` — how long the bot waits before applying a new movement intent
- `JitterStrengthMeters` — random offset applied to movement targets for slight path variation

Wander is used while roaming, the bot chases the nearest enemy while engaging, and will disengage (back off) if an enemy is too close.

---

## Troubleshooting (Beginner)

### "Framework not found"

Ensure the .NET 10 runtime is installed and available in your PATH.

Verify with:
```bash
dotnet --version
```

### "Scenario skipped"

Confirm you used the correct command:
```bash
dotnet run --project BotRunner -- --scenario demo
```

The offline demo only runs on `MockTransportConnection`. If Photon transport is selected, the demo is intentionally skipped with a log message.

### "Build succeeded but nothing happens"

Ensure you are running the `BotRunner` project:
```bash
dotnet run --project BotRunner -- --scenario demo
```

Look for `[Scenario]` logs — if they are missing, the demo injection did not start.

### Logs are too noisy

Set `LOG_LEVEL` to filter output:
```bash
LOG_LEVEL=warn dotnet run --project BotRunner -- --scenario demo
```
Levels: `error`, `warn`, `info` (default), `debug`, `trace`.

### No PositionUpdate logs

Confirm the bot reached the `Roaming` state.

Spawning is gated by `MatchState.CanRespawnNow`. Missing spawn events will prevent movement and updates.

---

## Offline Demo Details (M1)

### Injected Events
- `MatchStart`
- `FullPlayerListUpdate`
- `SetNextSpawnPointForPlayer`
- Batched `PositionUpdate`

### Scenario Script
```
BotRunner/Scenarios/ScenarioRunner.cs
```
You can safely edit delays, positions, or player stubs here.

### Payload Stubs
- `PlayerStub` objects stand in for real `SyncObject` data
- Used only for the offline demo path

### Logging
See `BotRunner/Docs/README.Logging.md` for log levels and quiet modes.

---

## Architecture Overview (Advanced)

### High-level Flow

```
Transport
  → RpcRouter
    → WorldState / MatchState
      → BotBrain (FSM)
        → RpcSender
```

### Key Design Notes

- **Transport abstraction** cleanly separates mock vs Photon paths
- **FSM** is intentionally minimal (no combat/aiming logic)
- **Timing** uses two ticks:
  - Network tick (Photon-style `Service()` cadence)
  - Bot logic tick (behavior updates)
- **ActorId** is the single authoritative key across systems

This structure mirrors a real client stack while remaining testable offline.

---

## Integration Notes (M2 — Authorized / Private Servers Only)

The following are intentionally stubbed and must only be implemented in authorized environments:

### Photon Transport
```
Networking/Photon3TransportConnection.cs
```
Enabled via `PHOTON3` symbol and `TransportConnectionFactory`

### RPC Identifiers
Replace placeholder IDs in `RpcMapping.Default()` with authoritative values

### Join Payload
Implement real `CharacterInfo` / auth serialization in `RpcSender.SendJoinRoom`

### Server Timebase
Replace fallback ticks with authoritative server-synchronized time

> **This repository contains no credentials, tokens, or production endpoints.**

---

## License / Disclaimer

This project is provided for **educational and reference purposes only**.

- ✅ Use only in safe, authorized environments
- ✅ Keep bots clearly identifiable (e.g. `[BOT]` name prefixes)
- ✅ Respect server authority and rules
- ❌ Do not deploy in public or competitive environments

**No warranty is provided.**
