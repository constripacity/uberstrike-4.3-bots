using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;

namespace BotRunner.Scenarios
{
    public static class SwarmRetreatTest
    {
        public static Task Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed != 0 ? config.Seed : 313);
                var spawn = new Vector3(4, 0, 4);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 21, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(Math.Max(3, config.EnemyCount), rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                // Enemies swarm close to force disengage/hold behavior.
                for (var wave = 0; wave < 3; wave++)
                {
                    await Task.Delay(Math.Max(80, durations.PositionUpdateMs / 2));
                    var batch = new byte[1 + 3 * 11];
                    batch[0] = 3;
                    var idx = 1;
                    for (var enemy = 0; enemy < 3; enemy++)
                    {
                        var pos = spawn + new Vector3(1 + enemy * 0.5f + wave * 0.2f, 0, 1 + enemy * 0.5f);
                        var sv = ShortVector3.FromVector(pos);
                        batch[idx] = (byte)(enemy + 2);
                        BitConverter.GetBytes(60000 + wave * 100 + enemy * 20).CopyTo(batch, idx + 1);
                        BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                        BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                        BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                        idx += 11;
                    }
                    mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                }
            });
        }
    }
}
