using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;

namespace BotRunner.Scenarios
{
    public static class LoadSpikeTest
    {
        public static Task Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed != 0 ? config.Seed : 99);
                var spawn = new Vector3(2, 0, 2);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 31, 999999 }, -1));
                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(config.EnemyCount, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                var batch = new byte[1 + config.EnemyCount * 11];
                for (var i = 0; i < config.EnemyCount; i++)
                {
                    var pos = spawn + new Vector3(3 + i, 0, 3 + i);
                    var sv = ShortVector3.FromVector(pos);
                    batch[0] = (byte)config.EnemyCount;
                    var idx = 1 + i * 11;
                    batch[idx] = (byte)(i + 2);
                    BitConverter.GetBytes(70000 + i * 5).CopyTo(batch, idx + 1);
                    BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                    BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                    BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                }

                // Burst 50 updates over ~100ms to stress timing.
                for (var tick = 0; tick < 50; tick++)
                {
                    mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                    await Task.Delay(2);
                }
            });
        }
    }
}
