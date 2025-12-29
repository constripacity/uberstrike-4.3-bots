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
        private readonly ITransportConnection _connection;
        private readonly RpcMapping _mapping;
        private readonly string _botName;

        /// <summary>
        /// The actorId assigned by the server once joined. For now we seed it from config; when wiring to
        /// real Photon responses, set this from the join acknowledgement.
        /// </summary>
        public int LocalActorId { get; set; }

        public RpcSender(ITransportConnection connection, RpcMapping mapping, string botName)
        {
            _connection = connection;
            _mapping = mapping;
            _botName = botName;
        }

        public void SendJoinRoom()
        {
            // TODO: Serialize CharacterInfo (RemoteMethodInterface object) exactly as the Unity client does.
            // Until then, send a placeholder object[] with the bot name so the transport path is exercised.
            var args = new object[] { _botName, "TODO_CharacterInfo" };
            Console.WriteLine("[RPC] GameRPC.Join -> placeholder payload (requires CharacterInfo serialization)");
            _connection.SendEvent(_mapping.RpcNameToId["GameRPC.Join"], args, NetReliability.Reliable);
        }

        public void SendLeaveRoom(int actorId)
        {
            Console.WriteLine($"[RPC] GameRPC.Leave actorId={actorId}");
            _connection.SendEvent(_mapping.RpcNameToId["GameRPC.Leave"], new object[] { actorId }, NetReliability.Reliable);
        }

        public void SendSpawnRequest(int actorId, Vector3 spawnPosition)
        {
            var args = new object[] { actorId, spawnPosition };

            Console.WriteLine($"[RPC] FpsGameRPC.SetPlayerSpawnPosition actorId={actorId} pos={spawnPosition}");
            _connection.SendEvent(_mapping.RpcNameToId["FpsGameRPC.SetPlayerSpawnPosition"], args, NetReliability.Reliable);
        }

        public void SendPositionUpdate(int actorId, Vector3 position, int serverTicks)
        {
            // Packed binary payload used by the retail client: actorId + ShortVector3(position) + server time ticks.
            var payload = new byte[14];
            var offset = 0;
            Buffer.BlockCopy(BitConverter.GetBytes(actorId), 0, payload, offset, sizeof(int));
            offset += sizeof(int);
            var sv = ShortVector3.FromVector(position);
            Buffer.BlockCopy(BitConverter.GetBytes(sv.X), 0, payload, offset, sizeof(short));
            offset += sizeof(short);
            Buffer.BlockCopy(BitConverter.GetBytes(sv.Y), 0, payload, offset, sizeof(short));
            offset += sizeof(short);
            Buffer.BlockCopy(BitConverter.GetBytes(sv.Z), 0, payload, offset, sizeof(short));
            offset += sizeof(short);
            Buffer.BlockCopy(BitConverter.GetBytes(serverTicks), 0, payload, offset, sizeof(int));

            Console.WriteLine($"[RPC] FpsGameRPC.PositionUpdate actorId={actorId} pos={position} ticks={serverTicks}");
            _connection.SendEvent(_mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], payload, NetReliability.Unreliable);
        }

        public void SendPlayerHit(int attackerId, int targetId, short damage, byte bodyPart, int projectileId, byte angleByte, int weaponId, byte weaponClass, int damageEffectFlag, float damageEffectValue)
        {
            var args = new object[]
            {
                attackerId,
                targetId,
                damage,
                bodyPart,
                projectileId,
                angleByte,
                weaponId,
                weaponClass,
                damageEffectFlag,
                damageEffectValue
            };

            Console.WriteLine($"[RPC] FpsGameRPC.PlayerHit attacker={attackerId} target={targetId} dmg={damage} weapon={weaponId}");
            _connection.SendEvent(_mapping.RpcNameToId["FpsGameRPC.PlayerHit"], args, NetReliability.Reliable);
        }
    }
}
