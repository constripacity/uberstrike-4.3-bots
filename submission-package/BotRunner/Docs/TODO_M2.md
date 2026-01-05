## M2 integration checklist (no protocol specifics)

- **Transport hookup:** Replace `Networking/MockTransportConnection.cs` with a real transport implementation. Entry point is `TransportConnectionFactory.Create(...)`.
- **RPC mapping:** Swap placeholder IDs in `Networking/RpcMapping.cs` with authoritative values before emitting or handling real traffic.
- **Join payload/auth:** Implement serialization in `Networking/RpcSender.SendJoinRoom` to include real character/auth data.
- **Server timebase:** Replace the fallback tick logic in `State/MatchState.UpdateServerTicks` and consumers (see `Bot/BotBrain.cs`) with server-synchronized timestamps.
- **State reconciliation:** For online play, reconcile `_currentPosition` in `Bot/BotBrain.cs` with server echoes instead of optimistic client authority.
- **Logging:** Keep `LOG_LEVEL` env override in mind; elevate critical failures to `Error` for remote monitoring.
