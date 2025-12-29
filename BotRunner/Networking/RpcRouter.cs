using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using BotRunner.State;
using BotRunner.Networking.Payload;
using System.Numerics;

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
                { _rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], payload => HandleSpawnAllowed(payload) }
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
            // Expecting either byte[] or List<byte> containing position updates.
            if (payload is byte[] bytes)
            {
                LogPositionPayload(bytes);
            }
            else if (payload is List<byte> list)
            {
                LogPositionPayload(list.ToArray());
            }
            else
            {
                Console.WriteLine($"[RPC] PositionUpdate unexpected payload type {payload?.GetType().Name ?? "null"}");
            }
        }

        private void HandleMatchStart()
        {
            Console.WriteLine("[RPC] MatchStart -> match running");
            _matchState.OnMatchStart(0, 0);
        }

        private void HandleMatchEnd()
        {
            Console.WriteLine("[RPC] MatchEnd -> match stopped");
            _matchState.OnMatchEnd();
        }

        private void HandleSpawnAllowed(object? payload)
        {
            Console.WriteLine("[RPC] SetNextSpawnPointForPlayer -> spawn allowed timestamp updated");
            _matchState.OnSpawnInstruction(-1, 0);
        }

        private void LogPositionPayload(ReadOnlySpan<byte> payload)
        {
            if (!BitConverter.IsLittleEndian)
            {
                Console.WriteLine("[RPC] Warning: host is not little-endian; position parsing may be incorrect.");
            }

            // Client->server format (14 bytes): actorId(int32) + short3 + ticks(int32)
            if (payload.Length == 14)
            {
                var actorId = BitConverter.ToInt32(payload[..4]);
                var sv = new ShortVector3(
                    BitConverter.ToInt16(payload.Slice(4, 2)),
                    BitConverter.ToInt16(payload.Slice(6, 2)),
                    BitConverter.ToInt16(payload.Slice(8, 2)));
                var ticks = BitConverter.ToInt32(payload.Slice(10, 4));
                Console.WriteLine($"[RPC] PositionUpdate (client-style) actor={actorId}, pos={sv}, ticks={ticks}");
                return;
            }

            // Server->client batched format: [count (byte)] + count * (actorId(byte) + timestamp(int32) + short3)
            if (payload.Length >= 1)
            {
                var count = payload[0];
                var expectedLength = 1 + count * 11;
                if (payload.Length == expectedLength)
                {
                    Console.WriteLine($"[RPC] PositionUpdate (server batch) entries={count}");
                    var idx = 1;
                    for (var i = 0; i < count; i++)
                    {
                        if (idx + 11 > payload.Length)
                        {
                            Console.WriteLine($"[RPC] PositionUpdate entry {i} truncated");
                            break;
                        }

                        var actorIdByte = payload[idx];
                        var timestamp = BitConverter.ToInt32(payload.Slice(idx + 1, 4));
                        var sv = new ShortVector3(
                            BitConverter.ToInt16(payload.Slice(idx + 5, 2)),
                            BitConverter.ToInt16(payload.Slice(idx + 7, 2)),
                            BitConverter.ToInt16(payload.Slice(idx + 9, 2)));
                        if (i < 5)
                        {
                            Console.WriteLine($"[RPC]   entry {i}: actorId={actorIdByte}, pos={sv}, timestamp={timestamp}");
                        }
                        idx += 11;
                    }
                    return;
                }
            }

            Console.WriteLine($"[RPC] PositionUpdate unknown format len={payload.Length}");
        }
    }
}
