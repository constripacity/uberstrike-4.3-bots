# UberStrike 4.3 Headless Bot Reference

This repository is a reference/inspiration project for building headless UberStrike 4.3 bots that speak the same Photon RPC surface as the retail client.

It is intentionally scoped and transparent, designed to:

- Demonstrate client-side architecture
- Enable offline experimentation
- Serve as a clean handoff to maintainers or contributors

## Operating Modes

The project currently supports two modes:

### M1 — Offline Demo (Implemented)
Uses a mock transport and scripted scenario to simulate a real match loop.

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

## Run Summary Output
Each offline run emits `run-summary.json` (next to the executable) on shutdown. It captures:

- Scenario name and seed
- Time spent in each bot FSM state
- Position updates sent
- Network ticks received

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
