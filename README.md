# UberStrike 4.3 Headless Bot Reference

This repository is a reference/inspiration project for building headless UberStrike 4.3 bots that speak the same Photon RPC surface as the retail client. It has two modes: **M1** (offline demo using a mock transport) and **M2** (future, authorized integration with a real server/Photon transport).

## Quickstart (Beginner, 5 minutes)
- **Prerequisites:** .NET SDK 8.0.x
- **Clone & restore:**
  ```bash
  git clone https://example.com/uberstrike-4.3-bots.git
  cd uberstrike-4.3-bots/BotRunner
  dotnet restore
  ```
- **Run the offline demo:**
  ```bash
  dotnet run -- --scenario demo
  ```
- **Expected log snippets:**
  ```
  [Scenario] Starting demo sequence...
  [Transport:Mock] Inject code=52 payloadType=Object[] sender=-1
  [RPC] MatchStart -> match running
  [Scenario] Injected SpawnAllowed for bot
  [Bot] Spawn requested at <10, 0, 10>
  [Transport:Mock] Send code=50 reliability=Unreliable payloadType=Byte[]
  ```
- **Success looks like:** The bot joins, spawns at (10,0,10), transitions to roaming, and emits `PositionUpdate` packets via the mock transport.

## Troubleshooting (Beginner)
- **Scenario flag not picked up:** Ensure you pass `-- --scenario demo` (note the double-dash). The argument is parsed in `Program.cs` with `GetScenario`.
- **Mock vs Photon transport:** The demo requires `MockTransportConnection`; if you configure Photon settings, the demo is skipped with a log warning.
- **RPC mapping missing keys:** `RpcMapping.Default()` holds placeholder IDs. If you change mappings and see “Unknown(...)” RPC logs, restore the defaults or supply matching IDs.
- **No PositionUpdate logs:** Confirm the bot reached the Roaming state. Spawns are gated by `MatchState.CanRespawnNow`; missing spawn events will prevent movement/updates.
- **Still stuck?** Check that `ScenarioRunner` injections are firing (look for `[Scenario]` logs) and that `transport.Service()` is being called in the main loop.

## Offline Demo details (M1)
- Injected events: `MatchStart`, `FullPlayerListUpdate`, `SetNextSpawnPointForPlayer` (spawn allowed), and a batched `PositionUpdate`.
- Scenario script: `BotRunner/Scenarios/ScenarioRunner.cs` — edit delays, positions, or stubs here.
- Payload stubs: `PlayerStub` objects stand in for real SyncObjects; they are used only for the offline demo path.

## Architecture (Advanced)
- Flow: **Transport** → **RpcRouter** → **State (WorldState/MatchState)** → **BotBrain** → **RpcSender**.
- Timing model: The main loop runs a network tick (Photon-style `Service()` cadence) and a bot logic tick (behavior updates). Both are driven from `Program.cs` using configurable Hz values.

## Integration notes (M2 – authorized/private servers only)
- Plug in a real Photon transport in `Networking/Photon3TransportConnection.cs` (and `PhotonConnection.cs`) and select it in `TransportConnectionFactory`.
- Replace placeholder RPC IDs in `Networking/RpcMapping.Default()` with the authoritative `RemoteMethodInterface` values (keep name↔ID maps in sync).
- Implement real join payload serialization (CharacterInfo/auth) in `RpcSender.SendJoinRoom` to match the retail client.
- Provide a server-synchronized timebase for `MatchState.UpdateServerTicks` and outbound timestamps in `RpcSender.SendPositionUpdate`.
- This repo contains **no credentials** or production server access; you must supply authorized values and endpoints.

## License / Disclaimer
Use only in safe, authorized environments. Keep bots clearly identifiable (e.g., `[BOT]` prefixes), respect server authority, and avoid any behavior that could be mistaken for a cheat. This project is provided for educational/reference purposes without warranty.
