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

        public static RpcMapping Default()
        {
            return new RpcMapping
            {
                RpcNameToId =
                {
                    // TODO: fill with the actual identifiers from RemoteMethodInterface
                    ["GameRPC.Join"] = 1,
                    ["GameRPC.Leave"] = 2,
                    ["GameRPC.FullPlayerListUpdate"] = 3,
                    ["GameRPC.DeltaPlayerListUpdate"] = 4,
                    ["GameRPC.PlayerUpdate"] = 5,
                    ["GameRPC.Begin"] = 6,
                    ["GameRPC.End"] = 7,
                    ["FpsGameRPC.PositionUpdate"] = 50,
                    ["FpsGameRPC.PlayerHit"] = 51,
                    ["FpsGameRPC.MatchStart"] = 52,
                    ["FpsGameRPC.MatchEnd"] = 53,
                    ["FpsGameRPC.SetNextSpawnPointForPlayer"] = 54,
                    ["FpsGameRPC.SetPlayerSpawnPosition"] = 55
                }
            };
        }
    }
}
