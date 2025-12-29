using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BotRunner.State;
using BotRunner.Networking.Payload;

namespace BotRunner.Networking
{
    /// <summary>
    /// Receives Photon events and routes them to lightweight handlers that update local state. In the
    /// real client these would be generated from RemoteMethodInterface; here we only implement the
    /// subset necessary for bot behavior.
    ///
    /// All handlers are keyed by RPC name (not numeric IDs) so RpcMapping can evolve independently.
    /// Logging is intentionally verbose to aid comparison with the retail client traffic.
    /// </summary>
    public class RpcRouter
    {
        private readonly WorldState _worldState;
        private readonly MatchState _matchState;
        private readonly RpcMapping _rpcMapping;
        private readonly ConcurrentQueue<Action> _pendingHandlers = new();
        private readonly Dictionary<byte, Action<object?>> _handlers;

        public RpcRouter(WorldState worldState, MatchState matchState, RpcMapping rpcMapping)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcMapping = rpcMapping;
            _handlers = new Dictionary<byte, Action<object?>>
            {
                { _rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], HandleFullPlayerListUpdate },
                { _rpcMapping.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], HandleDeltaPlayerListUpdate },
                { _rpcMapping.RpcNameToId["FpsGameRPC.PositionUpdate"], HandlePositionUpdate },
                { _rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], _ => HandleMatchStart() },
                { _rpcMapping.RpcNameToId["FpsGameRPC.MatchEnd"], _ => HandleMatchEnd() },
                { _rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], _ => HandleSpawnAllowed() }
            };
        }

        public void Register(ITransportConnection connection)
        {
            connection.EventReceived += HandleEvent;
        }

        public void FlushIncoming()
        {
            while (_pendingHandlers.TryDequeue(out var handler))
            {
                handler();
            }
        }

        private void HandleEvent(NetEvent netEvent)
        {
            var rpcName = _rpcMapping.RpcIdToName.TryGetValue(netEvent.EventCode, out var name)
                ? name
                : $"Unknown({netEvent.EventCode})";

            var payloadLength = (netEvent.Payload as byte[])?.Length ?? (netEvent.Payload as IList<byte>)?.Count ?? -1;
            Console.WriteLine($"[RPC] Received {rpcName} code={netEvent.EventCode} payloadType={netEvent.Payload?.GetType().Name ?? "null"} len={payloadLength} sender={netEvent.SenderActorId}");
            if (_handlers.TryGetValue(netEvent.EventCode, out var handler))
            {
                _pendingHandlers.Enqueue(() => handler(netEvent.Payload));
            }
        }

        private void HandleFullPlayerListUpdate(object? payload)
        {
            // TODO: Real payload is object[] containing List<SyncObject> and List<Vector3>.
            Console.WriteLine($"[RPC] FullPlayerListUpdate payloadType={payload?.GetType().Name ?? "null"} (TODO: parse SyncObject list)");
        }

        private void HandleDeltaPlayerListUpdate(object? payload)
        {
            // TODO: Real payload is List<SyncObject> delta entries.
            Console.WriteLine($"[RPC] DeltaPlayerListUpdate payloadType={payload?.GetType().Name ?? "null"} (TODO: parse delta SyncObjects)");
        }

        private void HandlePositionUpdate(object? payload)
        {
            // Expecting either byte[] or List<byte> containing batched position updates.
            if (payload is byte[] bytes)
            {
                LogPositionPayload(bytes);
            }
            else if (payload is List<byte> list)
            {
                LogPositionPayload(CollectionsMarshal.AsSpan(list));
            }
            else
            {
                Console.WriteLine($"[RPC] PositionUpdate unexpected payload type {payload?.GetType().Name ?? "null"}");
            }
        }

        private void HandleMatchStart()
        {
            Console.WriteLine("[RPC] MatchStart -> match running");
            _matchState.MatchRunning = true;
        }

        private void HandleMatchEnd()
        {
            Console.WriteLine("[RPC] MatchEnd -> match stopped");
            _matchState.MatchRunning = false;
        }

        private void HandleSpawnAllowed()
        {
            Console.WriteLine("[RPC] SetNextSpawnPointForPlayer -> spawn allowed timestamp updated");
            _matchState.LastSpawnAllowedAt = DateTime.UtcNow;
        }

        private void LogPositionPayload(ReadOnlySpan<byte> payload)
        {
            // Minimal logging for packed payloads: [actorId(int)] [pos short*3] [ticks(int)] ...
            if (payload.Length < 14)
            {
                Console.WriteLine($"[RPC] PositionUpdate payload too small ({payload.Length} bytes)");
                return;
            }

            var actorId = BitConverter.ToInt32(payload[..4]);
            var x = BitConverter.ToInt16(payload.Slice(4, 2));
            var y = BitConverter.ToInt16(payload.Slice(6, 2));
            var z = BitConverter.ToInt16(payload.Slice(8, 2));
            var ticks = BitConverter.ToInt32(payload.Slice(10, 4));
            var sv = new ShortVector3(x, y, z);
            Console.WriteLine($"[RPC] PositionUpdate -> actor={actorId}, pos={sv}, ticks={ticks} (batched payload len={payload.Length})");
        }
    }
}
