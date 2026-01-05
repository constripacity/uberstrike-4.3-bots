using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public class FlippingRegressionScenario : IScenario
    {
        public string Name => "flipping_regression";

        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _seed;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _seed = seed != 0 ? seed : 777;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var engageThreshold = 15f;
            var spawn = new Vector3(10, 0, 10);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 7, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(1, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Flipping regression: bot spawned and ready");
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var flipOffsets = new[] { -0.35f, 0.35f, -0.25f, 0.25f, -0.15f, 0.2f, -0.1f, 0.18f, -0.05f, 0.12f, -0.02f, 0.1f, -0.15f, 0.25f };
            var totalTicks = SimulationTime.Instance.ToTicks(TimeSpan.FromSeconds(5.5));
            var elapsedTicks = 0L;
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(75, _durations.PositionUpdateMs / 2));
            var timestamp = 100000;

            while (elapsedTicks < totalTicks)
            {
                foreach (var offset in flipOffsets)
                {
                    if (elapsedTicks >= totalTicks)
                    {
                        break;
                    }

                    yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                    elapsedTicks += intervalTicks;
                    yield return Inject(() =>
                    {
                        var pos = new Vector3(spawn.X + engageThreshold + offset, spawn.Y, spawn.Z);
                        var sv = ShortVector3.FromVector(pos);
                        var batch = new byte[12];
                        batch[0] = 1;
                        batch[1] = 2; // enemy id
                        BitConverter.GetBytes(timestamp).CopyTo(batch, 2);
                        BitConverter.GetBytes(sv.X).CopyTo(batch, 6);
                        BitConverter.GetBytes(sv.Y).CopyTo(batch, 8);
                        BitConverter.GetBytes(sv.Z).CopyTo(batch, 10);
                        _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                        var elapsedMs = SimulationTime.Instance.CurrentTick * SimulationTime.Instance.TickDurationMs;
                        BotRunner.Utils.Logger.Info($"[Scenario] Flip step at t+{elapsedMs:0}ms offset={offset:0.00}");
                    });
                    timestamp += 33;
                }
            }
        }

        private ScenarioStep Inject(Action action)
        {
            return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                AdvanceTicks = 1,
                Action = action
            };
        }

        private void EnsureReady()
        {
            if (_transport == null || _mapping == null)
            {
                throw new InvalidOperationException("Scenario not initialized");
            }
        }
    }
}
