using System.Collections.Generic;

namespace BotRunner.Networking
{
    /// <summary>
    /// Maps human-readable RPC names to Photon event codes. The values are intentionally configurable
    /// because different server builds or client versions may shift these identifiers.
    /// </summary>
    public class RpcMapping
    {
        public Dictionary<string, byte> RpcNameToId { get; } = new();
        public Dictionary<byte, string> RpcIdToName { get; } = new();

        public static RpcMapping Default()
        {
            var mapping = new RpcMapping();

            // TODO: fill with the actual identifiers from RemoteMethodInterface
            mapping.Add("GameRPC.Join", 1);
            mapping.Add("GameRPC.Leave", 2);
            mapping.Add("GameRPC.FullPlayerListUpdate", 3);
            mapping.Add("GameRPC.DeltaPlayerListUpdate", 4);
            mapping.Add("GameRPC.PlayerUpdate", 5);
            mapping.Add("GameRPC.Begin", 6);
            mapping.Add("GameRPC.End", 7);
            mapping.Add("FpsGameRPC.PositionUpdate", 50);
            mapping.Add("FpsGameRPC.PlayerHit", 51);
            mapping.Add("FpsGameRPC.MatchStart", 52);
            mapping.Add("FpsGameRPC.MatchEnd", 53);
            mapping.Add("FpsGameRPC.SetNextSpawnPointForPlayer", 54);
            mapping.Add("FpsGameRPC.SetPlayerSpawnPosition", 55);

            return mapping;
        }

        private void Add(string name, byte id)
        {
            RpcNameToId[name] = id;
            RpcIdToName[id] = name;
        }
    }
}
