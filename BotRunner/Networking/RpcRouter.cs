using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly ConcurrentQueue<Action> _pendingHandlers = new();
        private readonly Dictionary<string, Action<byte[]>> _handlers;

        public RpcRouter(WorldState worldState, MatchState matchState)
        {
            _worldState = worldState;
            _matchState = matchState;
            _handlers = new Dictionary<string, Action<byte[]>>(StringComparer.Ordinal)
            {
                ["GameRPC.FullPlayerListUpdate"] = HandleFullPlayerListUpdate,
                ["GameRPC.DeltaPlayerListUpdate"] = HandleDeltaPlayerListUpdate,
                ["FpsGameRPC.PositionUpdate"] = HandlePositionUpdate,
                ["FpsGameRPC.MatchStart"] = payload => HandleMatchStart(),
                ["FpsGameRPC.MatchEnd"] = payload => HandleMatchEnd(),
                ["FpsGameRPC.SetNextSpawnPointForPlayer"] = payload => HandleSpawnAllowed()
            };
        }

        public void Register(PhotonConnection connection)
        {
            connection.RpcReceived += HandleRpc;
        }

        public void FlushIncoming()
        {
            while (_pendingHandlers.TryDequeue(out var handler))
            {
                handler();
            }
        }

        private void HandleRpc(string rpcName, byte[] payload)
        {
            Console.WriteLine($"[RPC] Received {rpcName} ({payload.Length} bytes)");
            if (_handlers.TryGetValue(rpcName, out var handler))
            {
                _pendingHandlers.Enqueue(() => handler(payload));
            }
        }

        private void HandleFullPlayerListUpdate(byte[] payload)
        {
            // Minimal parser that expects [int count][count * (int cmid + string name + byte team + bool alive)]
            var reader = new PayloadReader(payload);
            var count = reader.ReadInt();
            for (var i = 0; i < count; i++)
            {
                var cmid = reader.ReadInt();
                var name = reader.ReadString();
                var team = reader.ReadByte();
                var alive = reader.ReadBool();
                Console.WriteLine($"[RPC] FullPlayerListUpdate -> cmid={cmid}, name={name}, team={team}, alive={alive}");
                _worldState.UpsertPlayer(cmid, name, team, alive);
            }
        }

        private void HandleDeltaPlayerListUpdate(byte[] payload)
        {
            // For demonstration, reuse the same parsing. Real delta packets would encode adds/removes.
            HandleFullPlayerListUpdate(payload);
        }

        private void HandlePositionUpdate(byte[] payload)
        {
            // Position updates arrive as batches. For the sample we only parse one actor per packet.
            var reader = new PayloadReader(payload);
            var actorId = reader.ReadInt();
            var position = reader.ReadShortVector3();
            Console.WriteLine($"[RPC] PositionUpdate -> actor={actorId}, pos={position}");
            _worldState.UpdatePosition(actorId, position.ToVector3());
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

        /// <summary>
        /// Minimal byte reader for inbound payloads.
        /// </summary>
        private sealed class PayloadReader
        {
            private readonly byte[] _buffer;
            private int _offset;

            public PayloadReader(byte[] buffer)
            {
                _buffer = buffer;
            }

            public int ReadInt()
            {
                var value = BitConverter.ToInt32(_buffer, _offset);
                _offset += sizeof(int);
                return value;
            }

            public short ReadShort()
            {
                var value = BitConverter.ToInt16(_buffer, _offset);
                _offset += sizeof(short);
                return value;
            }

            public byte ReadByte()
            {
                var value = _buffer[_offset];
                _offset += 1;
                return value;
            }

            public bool ReadBool() => ReadByte() != 0;

            public string ReadString()
            {
                var length = ReadShort();
                var value = Encoding.UTF8.GetString(_buffer, _offset, length);
                _offset += length;
                return value;
            }

            public ShortVector3 ReadShortVector3()
            {
                var x = ReadShort();
                var y = ReadShort();
                var z = ReadShort();
                return new ShortVector3(x, y, z);
            }
        }
    }
}
