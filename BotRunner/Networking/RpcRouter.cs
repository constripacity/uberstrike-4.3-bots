using System;
using System.Collections.Concurrent;
using System.Text;
using BotRunner.State;
using BotRunner.Networking.Payload;

namespace BotRunner.Networking
{
    /// <summary>
    /// Receives Photon events and routes them to lightweight handlers that update local state. In the
    /// real client these would be generated from RemoteMethodInterface; here we only implement the
    /// subset necessary for bot behavior.
    /// </summary>
    public class RpcRouter
    {
        private readonly WorldState _worldState;
        private readonly MatchState _matchState;
        private readonly ConcurrentQueue<Action> _pendingHandlers = new();

        public RpcRouter(WorldState worldState, MatchState matchState)
        {
            _worldState = worldState;
            _matchState = matchState;
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
            switch (rpcName)
            {
                case "GameRPC.FullPlayerListUpdate":
                    _pendingHandlers.Enqueue(() => HandleFullPlayerListUpdate(payload));
                    break;
                case "GameRPC.DeltaPlayerListUpdate":
                    _pendingHandlers.Enqueue(() => HandleDeltaPlayerListUpdate(payload));
                    break;
                case "FpsGameRPC.PositionUpdate":
                    _pendingHandlers.Enqueue(() => HandlePositionUpdate(payload));
                    break;
                case "FpsGameRPC.MatchStart":
                    _pendingHandlers.Enqueue(() => _matchState.MatchRunning = true);
                    break;
                case "FpsGameRPC.MatchEnd":
                    _pendingHandlers.Enqueue(() => _matchState.MatchRunning = false);
                    break;
                case "FpsGameRPC.SetNextSpawnPointForPlayer":
                    _pendingHandlers.Enqueue(() => _matchState.LastSpawnAllowedAt = DateTime.UtcNow);
                    break;
                default:
                    break;
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
            _worldState.UpdatePosition(actorId, position.ToVector3());
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
