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
    public class StateIntegrityScenario : IScenario
    {
        public string Name => "state_integrity_test";

        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _seed;
        private int _enemyCount;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _seed = seed != 0 ? seed : 999;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 2;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(6, 0, 6);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 9, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var updateTicks = ScenarioUtils.TicksFromMs(_durations.PositionUpdateMs);
            yield return new ScenarioStep { AdvanceTicks = updateTicks };
            yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, _enemyCount, 20f, new Vector3(10, 0, 10)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs * 2) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchEnd"], Array.Empty<object>(), -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 10, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
            {
                var respawnPos = new Vector3(6, 0, 6);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, respawnPos, 0 }, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = updateTicks };
            yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, _enemyCount, 12f, new Vector3(12, 0, 12)));
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
