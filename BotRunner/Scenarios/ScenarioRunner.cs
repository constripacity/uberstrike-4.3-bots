using System;
using System.Collections.Generic;
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
        public static async Task<ScenarioRunSummary?> Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId, BotBrain botBrain, WorldState worldState, MatchState matchState, BotConfig botConfig, RpcRouter router)
        {
            _ = mapping;
            var scenarioName = (config.ScenarioName ?? "demo").ToLowerInvariant();
            BotRunner.Utils.Logger.Info($"[Scenario] Starting {scenarioName} with seed={config.Seed} enemyCount={config.EnemyCount}");
            worldState.Reset();
            matchState.Reset();
            botBrain.Reset();

            if (scenarioName == "regression_suite")
            {
                var regressionScenarios = DeterministicSuiteRunner.BuildRegressionSuite(config);
                return await RunScenarioSuite(regressionScenarios, mock, botBrain, router, worldState, matchState, botConfig, botActorId, config);
            }

            if (scenarioName == "deterministic_suite")
            {
                var deterministicSuite = DeterministicSuite.BuildSuite(config);
                return await RunScenarioSuite(deterministicSuite, mock, botBrain, router, worldState, matchState, botConfig, botActorId, config);
            }

            var scenario = CreateScenario(scenarioName, config);
            if (scenario == null)
            {
                BotRunner.Utils.Logger.Warn($"[Scenario] Unknown scenario '{scenarioName}', falling back to demo.");
                scenario = new DemoScenario();
            }

            scenario.Initialize(mock, config.Seed, worldState, matchState, botConfig, botActorId, config);
            return await RunDeterministicScenario(scenario, botBrain, mock, router);
        }

        private static IScenario? CreateScenario(string scenarioName, ScenarioConfig config)
        {
            return scenarioName switch
            {
                "shoot_window_test" => new ShootWindowScenario(),
                "ammo_pressure" => new AmmoPressureScenario(),
                "team_duel" => new TeamDuelScenario(),
                "spawn_wave" => new SpawnWaveScenario(),
                "weapon_test" => new WeaponTestScenario(),
                "moving_target" => new MovingTargetScenario(),
                "flipping_test" => new FlippingTestScenario(),
                "flipping_regression" => new FlippingRegressionScenario(),
                "state_integrity_test" => new StateIntegrityScenario(),
                "swarm_retreat_test" => new SwarmRetreatScenario(),
                "load_spike_test" => new LoadSpikeTestScenario(),
                "bad_payload" => new BadPayloadScenario(),
                "reorder_drop" => new ReorderDropScenario(),
                "many_actors" => new ManyActorsScenario(),
                "duel" => new DuelScenario(config.Seed),
                "swarm" => new SwarmScenario(config.Seed),
                "retreat" => new RetreatScenario(config.Seed),
                "load_spike" => new LoadSpikeScenario(config.Seed),
                "loop" => new LoopScenario(),
                "respawn_loop" => new RespawnLoopScenario(),
                "demo" => new DemoScenario(),
                _ => null
            };
        }

        private static async Task<ScenarioRunSummary?> RunScenarioSuite(IEnumerable<IScenario> scenarios, MockTransportConnection transport, BotBrain botBrain, RpcRouter router, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            var results = new List<ScenarioResult>();
            foreach (var scenario in scenarios)
            {
                try
                {
                    worldState.Reset();
                    matchState.Reset();
                    botBrain.Reset();
                    scenario.Initialize(transport, scenarioConfig.Seed, worldState, matchState, botConfig, botActorId, scenarioConfig);
                    await RunDeterministicScenario(scenario, botBrain, transport, router);
                    results.Add(new ScenarioResult(scenario.Name, true, "completed"));
                }
                catch (Exception ex)
                {
                    results.Add(new ScenarioResult(scenario.Name, false, $"exception: {ex.Message}"));
                }
            }

            var success = results.TrueForAll(r => r.Success);
            return new ScenarioRunSummary(scenarioConfig.ScenarioName ?? "suite", success, results);
        }

        private static Task<ScenarioRunSummary?> RunDeterministicScenario(IScenario scenario, BotBrain botBrain, MockTransportConnection transport, RpcRouter router)
        {
            var simTime = SimulationTime.Instance;
            simTime.Reset();
            foreach (var step in scenario.GetSteps())
            {
                step.Action();
                var ticks = step.AdvanceTicks > 0 ? step.AdvanceTicks : SimulationTime.Instance.ToTicks(step.Delay);
                AdvanceSimulation(transport, router, botBrain, ticks);
            }

            var result = new ScenarioResult(scenario.Name, true, "completed");
            var summary = new ScenarioRunSummary(scenario.Name, true, new[] { result });
            return Task.FromResult<ScenarioRunSummary?>(summary);
        }

        private static void AdvanceSimulation(MockTransportConnection transport, RpcRouter router, BotBrain botBrain, long ticks)
        {
            var simTime = SimulationTime.Instance;
            var steps = Math.Max(0, ticks);
            for (var i = 0; i < steps; i++)
            {
                transport.Service();
                router.FlushIncoming();
                botBrain.Tick();
                simTime.Advance();
            }
        }
    }
}
