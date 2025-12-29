using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using BotRunner.Config;
using BotRunner.Networking.Payload;
using BotRunner.State;

namespace BotRunner.Networking
{
    /// <summary>
    /// Convenience wrapper for emitting RPCs with the correct payload shape. In a production
    /// implementation these methods would serialize fields exactly like the Unity client
    /// using RemoteMethodReflection.
    /// </summary>
    public class RpcSender
    {
        private readonly PhotonConnection _connection;
        private readonly RpcMapping _mapping;
        private readonly string _botName;

        public RpcSender(PhotonConnection connection, RpcMapping mapping, string botName)
        {
            _connection = connection;
            _mapping = mapping;
            _botName = botName;
        }

        public void SendJoinRoom()
        {
            var payload = new List<byte>();
            payload.AddRange(ByteConverter.GetBytes(_botName));
            payload.AddRange(ByteConverter.GetBytes((short)0)); // TODO: character id / gear selection

            Console.WriteLine("[RPC] GameRPC.Join -> sending minimal CharacterInfo");
            _connection.Send("GameRPC.Join", payload.ToArray());
        }

        public void SendLeaveRoom()
        {
            Console.WriteLine("[RPC] GameRPC.Leave");
            _connection.Send("GameRPC.Leave", Array.Empty<byte>());
        }

        public void SendSpawnRequest(Vector3 spawnPosition)
        {
            var payload = new List<byte>();
            payload.AddRange(ByteConverter.GetBytes(ShortVector3.FromVector(spawnPosition)));

            Console.WriteLine($"[RPC] FpsGameRPC.SetPlayerSpawnPosition at {spawnPosition}");
            _connection.Send("FpsGameRPC.SetPlayerSpawnPosition", payload.ToArray());
        }

        public void SendPositionUpdate(Vector3 position, Vector3 velocity, int serverTime)
        {
            var payload = new List<byte>();
            payload.AddRange(ByteConverter.GetBytes(ShortVector3.FromVector(position)));
            payload.AddRange(ByteConverter.GetBytes(ShortVector3.FromVector(velocity)));
            payload.AddRange(BitConverter.GetBytes(serverTime));

            Console.WriteLine($"[RPC] FpsGameRPC.PositionUpdate pos={position} vel={velocity} time={serverTime}");
            _connection.Send("FpsGameRPC.PositionUpdate", payload.ToArray());
        }

        public void SendPlayerHit(int targetCmid, int damage, Vector3 hitPoint)
        {
            var payload = new List<byte>();
            payload.AddRange(BitConverter.GetBytes(targetCmid));
            payload.AddRange(BitConverter.GetBytes(damage));
            payload.AddRange(ByteConverter.GetBytes(ShortVector3.FromVector(hitPoint)));

            Console.WriteLine($"[RPC] FpsGameRPC.PlayerHit target={targetCmid} dmg={damage}");
            _connection.Send("FpsGameRPC.PlayerHit", payload.ToArray());
        }
    }
}
