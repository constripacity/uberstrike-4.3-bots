using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;

namespace BotRunner.Scenarios
{
    public static class DeterministicSuiteRunner
    {
        public static async Task<ScenarioRunSummary?> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            var seed = config.Seed == 0 ? 12345 : config.Seed;
            var baseConfig = new ScenarioConfig
            {
                Durations = config.Durations,
                EnemyCount = config.EnemyCount,
                Seed = seed
            };

            var results = new List<ScenarioResult>();

            results.Add(await RunSafe("duel", () => DuelScenario.Run(mock, mapping, baseConfig, botActorId)));
            results.Add(await RunSafe("swarm", () => SwarmScenario.Run(mock, mapping, baseConfig, botActorId)));
            results.Add(await RunSafe("retreat", () => RetreatScenario.Run(mock, mapping, baseConfig, botActorId)));
            results.Add(await RunSafe("load_spike", () => LoadSpikeScenario.Run(mock, mapping, baseConfig, botActorId)));

            var success = results.TrueForAll(r => r.Success);
            BotRunner.Utils.Logger.Info("[Regression] Summary:");
            foreach (var r in results)
            {
                BotRunner.Utils.Logger.Info($"[Regression] {r.Name}: {(r.Success ? "PASS" : "FAIL")} ({r.Details})");
            }

            return new ScenarioRunSummary("regression_suite", success, results);
        }

        private static async Task<ScenarioResult> RunSafe(string name, Func<Task<ScenarioResult>> fn)
        {
            try
            {
                return await fn();
            }
            catch (Exception ex)
            {
                return new ScenarioResult(name, false, $"exception: {ex.Message}");
            }
        }
    }

    public static class DuelScenario
    {
        public static Task<ScenarioResult> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed);
                var spawn = new Vector3(10, 0, 10);
                var engageDistances = new[] { 8f, 14f, 20f };

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 11, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(1, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                var timestamp = 200000;
                foreach (var dist in engageDistances)
                {
                    await Task.Delay(Math.Max(75, durations.PositionUpdateMs / 2));
                    var pos = new Vector3(spawn.X + dist, spawn.Y, spawn.Z);
                    ScenarioHelpers.InjectPosition(mock, mapping, timestamp, pos);
                    timestamp += 33;
                }

                return new ScenarioResult("duel", true, "positions cycled");
            });
        }
    }

    public static class SwarmScenario
    {
        public static Task<ScenarioResult> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed ^ 0xFACE);
                var spawn = new Vector3(6, 0, 6);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 12, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(3, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                var basePos = new Vector3(12, 0, 12);
                for (var i = 0; i < 6; i++)
                {
                    await Task.Delay(Math.Max(60, durations.PositionUpdateMs / 3));
                    ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, 3, 10f, basePos);
                }

                return new ScenarioResult("swarm", true, "3-enemy waves injected");
            });
        }
    }

    public static class RetreatScenario
    {
        public static Task<ScenarioResult> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed ^ 0xBEEF);
                var spawn = new Vector3(4, 0, 4);
                var threatPos = new Vector3(5, 0, 5);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 13, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(1, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                // Threat appears very close to encourage disengage behavior.
                var timestamp = 300000;
                for (var i = 0; i < 5; i++)
                {
                    await Task.Delay(Math.Max(70, durations.PositionUpdateMs / 2));
                    var offset = i % 2 == 0 ? -0.5f : 0.5f;
                    var pos = new Vector3(threatPos.X + offset, threatPos.Y, threatPos.Z + offset);
                    ScenarioHelpers.InjectPosition(mock, mapping, timestamp, pos);
                    timestamp += 33;
                }

                return new ScenarioResult("retreat", true, "close-range threat oscillated");
            });
        }
    }

    public static class LoadSpikeScenario
    {
        public static Task<ScenarioResult> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();
                var rng = new Random(config.Seed ^ 0x1234);
                var spawn = new Vector3(8, 0, 8);

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 14, 999999 }, -1));

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(1, rng, botActorId, spawn);
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, spawn, 0 }, -1));

                // Burst of position updates to mimic load spike.
                for (var i = 0; i < 20; i++)
                {
                    await Task.Delay(Math.Max(25, durations.PositionUpdateMs / 5));
                    ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, 1, 15f, spawn + new Vector3(6, 0, 6));
                }

                return new ScenarioResult("load_spike", true, "burst updates delivered");
            });
        }
    }

    public static class ScenarioHelpers
    {
        internal static void InjectPosition(MockTransportConnection mock, RpcMapping mapping, int timestamp, Vector3 pos)
        {
            var sv = ShortVector3.FromVector(pos);
            var batch = new byte[12];
            batch[0] = 1;
            batch[1] = 2; // enemy id
            BitConverter.GetBytes(timestamp).CopyTo(batch, 2);
            BitConverter.GetBytes(sv.X).CopyTo(batch, 6);
            BitConverter.GetBytes(sv.Y).CopyTo(batch, 8);
            BitConverter.GetBytes(sv.Z).CopyTo(batch, 10);
            mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
        }
    }

    public record ScenarioResult(string Name, bool Success, string Details);

    public record ScenarioRunSummary(string SuiteName, bool Success, IReadOnlyList<ScenarioResult> Results);
}
