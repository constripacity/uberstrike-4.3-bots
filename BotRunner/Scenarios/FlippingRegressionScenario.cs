using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;

namespace BotRunner.Scenarios
{
    /// <summary>
    /// Exercises the utility selector hysteresis by moving an enemy just across the engage threshold.
    /// </summary>
    public static class FlippingRegressionScenario
    {
        public static Task Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var seed = config.Seed != 0 ? config.Seed : 777;
                var rng = new Random(seed);
                var engageThreshold = 15f;
                var spawn = new Vector3(10, 0, 10);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 7, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(1, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Flipping regression: bot spawned and ready");

                // Oscillate an enemy across the engage boundary for 5+ seconds.
                var flipOffsets = new[] { -0.35f, 0.35f, -0.25f, 0.25f, -0.15f, 0.2f, -0.1f, 0.18f, -0.05f, 0.12f, -0.02f, 0.1f, -0.15f, 0.25f };
                var startUtc = DateTime.UtcNow;
                var timestamp = 100000;
                while (DateTime.UtcNow - startUtc < TimeSpan.FromSeconds(5.5))
                {
                    foreach (var offset in flipOffsets)
                    {
                        if (DateTime.UtcNow - startUtc >= TimeSpan.FromSeconds(5.5))
                        {
                            break;
                        }

                        await Task.Delay(Math.Max(75, durations.PositionUpdateMs / 2));
                        var pos = new Vector3(spawn.X + engageThreshold + offset, spawn.Y, spawn.Z);
                        var sv = ShortVector3.FromVector(pos);
                        var batch = new byte[12];
                        batch[0] = 1;
                        batch[1] = 2; // enemy id
                        BitConverter.GetBytes(timestamp).CopyTo(batch, 2);
                        BitConverter.GetBytes(sv.X).CopyTo(batch, 6);
                        BitConverter.GetBytes(sv.Y).CopyTo(batch, 8);
                        BitConverter.GetBytes(sv.Z).CopyTo(batch, 10);
                        mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                        BotRunner.Utils.Logger.Info($"[Scenario] Flip step at t+{(DateTime.UtcNow - startUtc).TotalMilliseconds:0}ms offset={offset:0.00}");
                        timestamp += 33;
                    }
                }
            });
        }
    }
}
