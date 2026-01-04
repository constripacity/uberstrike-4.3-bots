using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;

namespace BotRunner.Scenarios
{
    public static class StateIntegrityTest
    {
        public static Task Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var seed = config.Seed != 0 ? config.Seed : 42;
                var rng = new Random(seed);
                var spawn = new Vector3(6, 0, 6);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 11, 999999 }, -1));
                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(Math.Max(1, config.EnemyCount), rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                await Task.Delay(durations.PositionUpdateMs);
                ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, Math.Max(1, config.EnemyCount), 8f, spawn + new Vector3(6, 0, 0));

                await Task.Delay(durations.MatchStartMs * 2);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchEnd"], Array.Empty<object>(), -1));

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 12, 999999 }, -1));
                await Task.Delay(durations.SpawnMs);
                var respawn = spawn + new Vector3(2, 0, 2);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, respawn, 1 }, -1));
                await Task.Delay(durations.PositionUpdateMs);
                ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, Math.Max(1, config.EnemyCount), 6f, respawn + new Vector3(4, 0, 4));
            });
        }
    }
}
