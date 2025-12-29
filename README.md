# UberStrike 4.3 Headless Bot Reference

This repository demonstrates how AI-driven players could connect to an UberStrike 4.3 server using the same Photon RPC surfaces as the retail client. The intent is to provide a clean, transparent reference for legitimate server-side adoption—not to exploit or bypass any game protections.

## Goals
- Mirror the official client’s networking behavior while running headless (no Unity dependency).
- Keep the bot “brain” separate from the transport layer so server developers can adopt or adapt the logic.
- Provide conservative movement and firing patterns that stay within human-like limits and defer all authority to the server.

## Project layout
```
BotRunner/
 ├─ Program.cs                    // Entry point and lifecycle control
 ├─ Config/
 │   ├─ AppSettings.cs            // Strongly-typed settings
 │   └─ appsettings.json          // Server, room, and bot configuration
 ├─ Networking/
 │   ├─ ITransportConnection.cs   // Transport abstraction (Photon or mock)
 │   ├─ MockTransportConnection.cs// Offline/mock transport
 │   ├─ PhotonConnection.cs       // Photon peer wrapper
 │   ├─ Photon3TransportConnection.cs // Skeleton for real Photon peer
 │   ├─ NetEvent.cs               // Event envelope
 │   ├─ NetReliability.cs         // Delivery enum
 │   ├─ TransportConnectionFactory.cs // Selects Photon vs mock
 │   ├─ RpcRouter.cs              // Inbound RPC dispatch
 │   ├─ RpcMapping.cs             // RPC name → numeric ID (TODO fill from client)
 │   ├─ RpcSender.cs              // Helper for outbound RPCs
 │   └─ Payload/
 │       ├─ ShortVector3.cs       // Vector compression helper
 │       ├─ ByteConverter.cs      // Little-endian primitive helpers
 │       └─ PayloadSchemas.cs     // Documented field order/types per RPC
 ├─ Scenarios/
 │   └─ ScenarioRunner.cs         // Offline demo/validation sequences
 ├─ State/
 │   ├─ WorldState.cs             // actorId → PlayerState
 │   ├─ PlayerState.cs            // Position, team, health, alive flags
 │   ├─ PlayerSnapshot.cs         // Immutable snapshot for rendering/logic
 │   ├─ PlayerStub.cs             // Lightweight network payload model
 │   └─ MatchState.cs             // Match running flags and spawn timing
 ├─ Bot/
 │   ├─ BotBrain.cs               // Simple FSM controlling lifecycle
 │   ├─ BotMovement.cs            // Roam and chase logic
 │   ├─ BotCombat.cs              // Aim error, reaction, firing cadence
 │   └─ BotConfig.cs              // Difficulty & tuning parameters
 ├─ Utils/
 │   └─ RateLimiter.cs            // Cadence control
```

## Running the sample
1. Populate `BotRunner/Config/appsettings.json` with a valid Photon AppId, CMID, and access level from a legitimate login flow. Adjust `NetworkTickRateHz` (Photon pump) and `BotLogicTickRateHz` (behavior tick) to match your server’s expectations.
2. Build and run the console app with your preferred .NET SDK. The bot will:
   - connect to the Photon endpoint,
   - join the configured room,
   - wait for the match to start,
   - spawn when allowed,
   - roam and engage nearby enemies while sending position updates,
   - leave cleanly on shutdown.

## Offline Demo (M1)
Run:
```
dotnet run -- --scenario demo
```

This starts a fully offline simulation using `MockTransportConnection`. Injected events simulate:
- MatchStart
- Player list sync (bot + enemy)
- Spawn permission
- Enemy movement updates

Expected behavior:
- Bot joins
- Bot spawns
- Bot transitions to roaming/engaging
- Bot emits PositionUpdate packets via mock transport

This prevents humans (and future AIs) from mislabeling the repo as “not playable.”

## Disclaimer
This is a **reference / inspiration project** intended to help revive UberStrike responsibly. Bots should always remain identifiable (e.g., names prefixed with `[BOT]`), respect server authority, and avoid any behavior that could be mistaken for a cheat.

## TODO
- Replace placeholder RPC identifiers in `RpcMapping` with the exact values from `RemoteMethodInterface`.
- Implement true Photon serialization and hook into `PhotonPeer.Service()` instead of the stub transport.
- Extend RPC parsing to handle full batches and gear loadouts.
- Allow server-hosted logic to replace the headless client while keeping the same behavior.
