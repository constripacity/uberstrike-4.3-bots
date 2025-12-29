namespace BotRunner.Networking.Payload
{
    /// <summary>
    /// Documentation-only schemas that describe the order and types of fields expected by each RPC.
    /// These are intended to be cross-checked with RemoteMethodInterface definitions inside the
    /// original client. Actual serialization is handled by <see cref="ByteConverter"/>.
    /// </summary>
    public static class PayloadSchemas
    {
        /*
         * GameRPC.Join (client -> server)
         * [string] playerName
         * [short]  characterId (gear loadout identifier)
         * TODO: include CMID, AccessLevel, and AuthToken when integrating with real services.
         *
         * GameRPC.FullPlayerListUpdate (server -> client)
         * [int] playerCount
         * Repeated per player:
         *   [int] cmid
         *   [string] name
         *   [byte] team
         *   [bool] isAlive
         *
         * FpsGameRPC.PositionUpdate (bidirectional)
         * [short,short,short] position * 100
         * [short,short,short] velocity * 100
         * [int] serverTime
         *
         * FpsGameRPC.PlayerHit (client -> server)
         * [int] targetCmid
         * [int] damage
         * [short,short,short] hit point * 100
         *
         * FpsGameRPC.SetPlayerSpawnPosition (client -> server)
         * [short,short,short] spawn position * 100
         */
    }
}
