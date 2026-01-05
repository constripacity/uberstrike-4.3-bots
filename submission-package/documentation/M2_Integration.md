# M2 Integration Guide

This guide walks through replacing the offline `MockTransportConnection` with the real Photon transport stubs while keeping Phase 2 determinism intact.

## Prerequisites
- Authorized access to an UberStrike Photon endpoint (test or private server only).
- Photon AppId, region, and event code mappings.
- Familiarity with the RPC payload formats (CharacterInfo, SyncObject, PositionUpdate, PlayerHit).
- .NET SDK 7+ and the ability to run the regression suite locally.

## Step-by-Step Integration

### 1) Wire the transport
**Before (M1, offline):**
```csharp
var transport = TransportConnectionFactory.Create(settings); // returns MockTransportConnection for offline
rpcRouter.Register(transport);
await transport.ConnectAsync(cts.Token);
```

**After (M2, online):**
```csharp
var transport = new Photon3TransportConnection(settings.Server.Endpoint, settings.Server.Region, settings.PhotonAppId);
rpcRouter.Register(transport);
await transport.ConnectAsync(cts.Token);
```
Ensure `settings.Logging.Quiet` is false while validating the live connection so RPC routing logs are visible.

### 2) Provide real RPC codes
Update `RpcMapping.Default()` with the real Photon event codes from your server. Keep placeholder values for any RPCs you do **not** need; do not remove existing entries to avoid breaking offline scenarios.

### 3) Serialize payloads correctly
- **CharacterInfo / Player stubs:** mirror the live client’s layout (CMID, name, team, alive flag, position).
- **SyncObject updates:** preserve existing parsing; do not alter `SyncObject` handling.
- **PositionUpdate:** ensure the byte layout matches `ShortVector3` packing (see `Networking/Payload/ShortVector3.cs`).
- **PlayerHit:** the server remains authoritative; only send validated hit requests.

### 4) Safety checklist
- Use test servers only; never point at production without authorization.
- Keep the deterministic runner available by toggling the scenario name back to offline cases.
- Confirm seeds are supplied when running deterministic regression (`--seed 777`).

## Testing the integration safely
1. Start with a single offline scenario to confirm baseline (e.g., `flipping_regression`).
2. Switch to Photon transport and run a minimal live-room join (no combat) to confirm event wiring.
3. Enable one RPC at a time (MatchStart → FullPlayerListUpdate → PositionUpdate) and observe state changes.
4. Add hit events last; validate the server accepts payloads.
5. Run `scripts/validate-determinism.sh` and `scripts/benchmark.sh` after each change to ensure offline determinism is preserved.

## Troubleshooting
- **No events received:** verify Photon endpoint/region and that `RpcMapping` codes match server expectations.
- **Payload rejected:** check serialization order and types (especially arrays vs. primitives) against live protocol docs.
- **State desync:** ensure PositionUpdate timestamps/actor IDs are correct and that `WorldState` filters stale entries.
- **Transport exceptions:** confirm the Photon SDK dependencies are present and network egress is allowed.

### 5) Server Timebase Synchronization
- Use the server's authoritative clock for all simulation step calculations.
- Synchronize local `SimulationTime` with the server heartbeat to avoid drift.
- Buffer incoming state snapshots to account for network jitter while maintaining a consistent time offset.

## References
- `Networking/Photon3TransportConnection.cs` (stubbed, do not modify protocol parsing).
- `RpcMapping.cs` for event code tables.
- `Networking/Payload/ShortVector3.cs` for compact vector packing.
- `Docs/ScenariosAndDeterminism.md` for deterministic scenario authoring.
