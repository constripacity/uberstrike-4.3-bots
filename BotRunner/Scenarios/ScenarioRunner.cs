using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Bot;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public static class ScenarioRunner
    {
        public static Task<ScenarioRunSummary?> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId, BotBrain botBrain, WorldState worldState, MatchState matchState, BotConfig botConfig, RpcRouter router)
        {
            var scenarioName = (config.ScenarioName ?? "demo").ToLowerInvariant();
            BotRunner.Utils.Logger.Info($"[Scenario] Starting {scenarioName} with seed={config.Seed} enemyCount={config.EnemyCount}");

            if (scenarioName == "shoot_window_test")
            {
                var scenario = new ShootWindowScenario();
                scenario.Initialize(mock, config.Seed, worldState, matchState, botConfig, botActorId);
                return RunDeterministicScenario(scenario, botBrain, mock, router);
            }

            if (scenarioName == "ammo_pressure")
            {
                var scenario = new AmmoPressureScenario();
                scenario.Initialize(mock, config.Seed, worldState, matchState, botConfig, botActorId);
                return RunDeterministicScenario(scenario, botBrain, mock, router);
            }

            // Helper to run a Task-based scenario and return a one-entry summary.
            static async Task<ScenarioRunSummary?> RunAndSummarize(string name, Func<Task> fn)
            {
                try
                {
                    await fn();
                    var result = new ScenarioResult(name, true, "completed");
                    return new ScenarioRunSummary(name, true, new[] { result });
                }
                catch (Exception ex)
                {
                    var result = new ScenarioResult(name, false, $"exception: {ex.Message}");
                    return new ScenarioRunSummary(name, false, new[] { result });
                }
            }

            return scenarioName switch
            {
                "duel" => RunAndSummarize("duel", () => RunDuel(mock, mapping, config, botActorId)),
                "respawn_loop" => RunAndSummarize("respawn_loop", () => RunRespawnLoop(mock, mapping, config, botActorId)),
                "loop" => RunAndSummarize("loop", () => RunLoop(mock, mapping, config, botActorId)),
                "flipping_test" => RunAndSummarize("flipping_test", () => FlippingTest.Run(mock, mapping, config, botActorId)),
                "flipping_regression" => RunAndSummarize("flipping_regression", () => FlippingRegressionScenario.Run(mock, mapping, config, botActorId)),
                "state_integrity_test" => RunAndSummarize("state_integrity_test", () => StateIntegrityTest.Run(mock, mapping, config, botActorId)),
                "swarm_retreat_test" => RunAndSummarize("swarm_retreat_test", () => SwarmRetreatTest.Run(mock, mapping, config, botActorId)),
                "load_spike_test" => RunAndSummarize("load_spike_test", () => LoadSpikeTest.Run(mock, mapping, config, botActorId)),
                "regression_suite" => DeterministicSuiteRunner.Run(mock, mapping, config, botActorId),
                "deterministic_suite" => RunAndSummarize("deterministic_suite", () => DeterministicSuite.Run(mock, mapping, config, botActorId)),
                _ => RunAndSummarize("demo", () => RunDemo(mock, mapping, config, botActorId))
            };
        }

        private static Task<ScenarioRunSummary?> RunDeterministicScenario(IScenario scenario, BotBrain botBrain, MockTransportConnection transport, RpcRouter router)
        {
            var simTime = SimulationTime.Instance;
            simTime.Reset();

            var steps = scenario.GetSteps();
            // The bot tick rate is configured in appsettings, but for scenarios we can assume a fixed rate.
            // The prompt implies a 60Hz rate for SimulationTime.
            var botTickIntervalMs = 16.667f; 

            foreach (var step in steps)
            {
                step.Action();
                var delayTicks = (long)(step.Delay.TotalMilliseconds / botTickIntervalMs);
                for (var i = 0; i < delayTicks; i++)
                {
                    transport.Service();
                    router.FlushIncoming();
                    botBrain.Tick();
                    simTime.Advance();
                }
            }

            var result = new ScenarioResult(scenario.Name, true, "completed");
            var summary = new ScenarioRunSummary(scenario.Name, true, new[] { result });
            return Task.FromResult<ScenarioRunSummary?>(summary);
        }
        
        private static Task RunDemo(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart");

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(10, 0, 10));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                BotRunner.Utils.Logger.Info($"[Scenario] Injected FullPlayerListUpdate (bot + {config.EnemyCount} enemies)");

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(10, 0, 10), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected SpawnAllowed for bot");

                await Task.Delay(durations.PositionUpdateMs);
                ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, config.EnemyCount, 20f, new Vector3(20, 0, 20));
            });
        }

        private static Task RunLoop(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();
                await RunSingleLoopCycle(mock, mapping, config, botActorId, rng, durations, matchCount: 10);

                // Optional second cycle to exercise lifecycle.
                await RunSingleLoopCycle(mock, mapping, config, botActorId, rng, durations, matchCount: 11);
            });
        }

        private static async Task RunSingleLoopCycle(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId, Random rng, ScenarioDurations durations, int matchCount)
        {
            await Task.Delay(durations.MatchStartMs);
            mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { matchCount, 999999 }, -1));
            BotRunner.Utils.Logger.Info($"[Scenario] Injected MatchStart (loop cycle {matchCount})");

            await Task.Delay(durations.PlayerListMs);
            var players = ScenarioUtils.BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(6, 0, 6));
            mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

            await Task.Delay(durations.SpawnMs);
            mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(6, 0, 6), 0 }, -1));

            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(Math.Max(100, durations.PositionUpdateMs));
                ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, Math.Max(1, config.EnemyCount), 10f, new Vector3(12, 0, 12));
            }

            await Task.Delay(durations.PositionUpdateMs);
            mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchEnd"], Array.Empty<object>(), -1));
            BotRunner.Utils.Logger.Info($"[Scenario] Injected MatchEnd (loop cycle {matchCount})");
        }

        private static Task RunDuel(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart (duel)");

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(config.EnemyCount, new Random(config.Seed), botActorId, new Vector3(8, 0, 8));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected FullPlayerListUpdate (duel)");

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(8, 0, 8), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected SpawnAllowed for bot");

                await ScenarioUtils.InjectDeterministicPath(mock, mapping, config.EnemyCount, durations.PositionUpdateMs);
            });
        }

        private static Task RunRespawnLoop(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 3, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart (respawn loop)");

                await Task.Delay(durations.PlayerListMs);
                var players = ScenarioUtils.BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(5, 0, 5));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(5, 0, 5), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected initial spawn for bot");

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    await Task.Delay(durations.RespawnLoopMs);
                    var death = new[] { new PlayerStub(botActorId, "[BOT] Respawn", 0, false, Vector3.Zero) };
                    mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], death, botActorId));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: marked bot as dead");

                    await Task.Delay(durations.SpawnMs);
                    var respawnPos = new Vector3(5 + cycle * 2, 0, 5 + cycle * 2);
                    mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, respawnPos, 1 }, -1));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: respawn allowed at {respawnPos}");
                    var aliveAgain = new[] { new PlayerStub(botActorId, "[BOT] Respawn", 0, true, respawnPos) };
                    mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], aliveAgain, botActorId));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: marked bot alive at {respawnPos}");
                }

                await Task.Delay(durations.PositionUpdateMs);
                ScenarioUtils.InjectEnemyBatch(mock, mapping, rng, config.EnemyCount, 12f, new Vector3(12, 0, 12));
            });
        }
    }
}