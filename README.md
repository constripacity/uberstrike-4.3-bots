# UberStrike 4.3 Headless Bot Reference

This repository is a reference / inspiration project for building headless UberStrike 4.3 bots that speak the same Photon RPC surface as the retail client.

It is intentionally scoped and transparent, designed to:

demonstrate client-side architecture,

enable offline experimentation,

and serve as a clean handoff to maintainers or contributors.

The project currently supports two modes:

M1 — Offline Demo (implemented)
Uses a mock transport and scripted scenario to simulate a real match loop.

M2 — Online / Photon Integration (intentionally stubbed)
Reserved for authorized, private server environments only.

First Run (Offline Demo – M1)
Prerequisites

Ensure the following are already installed:

Windows

.NET 10 runtime (no additional installs required)

The project is currently targeted to net10.0 to match the runtime used during development.

Clone & Build
```bash
git clone https://github.com/constripacity/uberstrike-4.3-bots.git
cd uberstrike-4.3-bots
dotnet build
```

Run the Offline Demo
```bash
dotnet run --project BotRunner -- --scenario demo
```

⚠️ Note the double dash before --scenario.
This is required so the argument is forwarded to the application.

What You Should See

If successful, the terminal will show a deterministic sequence similar to:
```
[Scenario] Starting demo sequence...
[Transport:Mock] Inject code=52 payloadType=Object[] sender=-1
[RPC] MatchStart -> match running
[Scenario] Injected SpawnAllowed for bot
[Bot] Spawn requested at <10, 0, 10>
[Transport:Mock] Send code=50 reliability=Unreliable payloadType=Byte[]
```

Success looks like:

The bot joins the match

Receives a spawn instruction

Transitions to roaming / engaging

Emits repeated PositionUpdate packets via the mock transport

This confirms that the FSM, state propagation, timing model, and RPC routing are all functioning in M1.

Troubleshooting (Beginner)
“Framework not found”

Ensure the .NET 10 runtime is installed and available in your PATH.

Verify with:
```bash
dotnet --version
```

“Scenario skipped”

Confirm you used:
```bash
-- --scenario demo
```

The offline demo only runs on MockTransportConnection.
If Photon transport is selected, the demo is intentionally skipped with a log message.

“Build succeeded but nothing happens”

Ensure you are running the BotRunner project:
```bash
dotnet run --project BotRunner -- --scenario demo
```

Look for [Scenario] logs — if they are missing, the demo injection did not start.

No PositionUpdate logs

Confirm the bot reached the Roaming state.

Spawning is gated by MatchState.CanRespawnNow.

Missing spawn events will prevent movement and updates.

Offline Demo Details (M1)

Injected events:

MatchStart

FullPlayerListUpdate

SetNextSpawnPointForPlayer

Batched PositionUpdate

Scenario script:
BotRunner/Scenarios/ScenarioRunner.cs
You can safely edit delays, positions, or player stubs here.

Payload stubs:

PlayerStub objects stand in for real SyncObject data

Used only for the offline demo path

Logging:

See BotRunner/Docs/README.Logging.md for log levels and quiet modes

Architecture Overview (Advanced)

High-level flow:

Transport
  → RpcRouter
    → WorldState / MatchState
      → BotBrain (FSM)
        → RpcSender

Key design notes:

Transport abstraction cleanly separates mock vs Photon paths

FSM is intentionally minimal (no combat / aiming logic)

Timing uses two ticks:

Network tick (Photon-style Service() cadence)

Bot logic tick (behavior updates)

ActorId is the single authoritative key across systems

This structure mirrors a real client stack while remaining testable offline.

Integration Notes (M2 — Authorized / Private Servers Only)

The following are intentionally stubbed and must only be implemented in authorized environments:

Photon transport:

Networking/Photon3TransportConnection.cs

Enabled via PHOTON3 symbol and TransportConnectionFactory

RPC identifiers:

Replace placeholder IDs in RpcMapping.Default() with authoritative values

Join payload:

Implement real CharacterInfo / auth serialization in RpcSender.SendJoinRoom

Server timebase:

Replace fallback ticks with authoritative server-synchronized time

This repository contains no credentials, tokens, or production endpoints.

License / Disclaimer

This project is provided for educational and reference purposes only.

Use only in safe, authorized environments

Keep bots clearly identifiable (e.g. [BOT] name prefixes)

Respect server authority and rules

Do not deploy in public or competitive environments

No warranty is provided.