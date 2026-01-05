using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    internal class DemoScenario : IScenario
    {
        public string Name => "demo";

        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _enemyCount;
        private int _seed;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 1;
            _seed = seed != 0 ? seed : 101;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(10, 0, 10);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 0 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PositionUpdateMs) };
            yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, Math.Max(1, _enemyCount), 20f, new Vector3(20, 0, 20)));
        }

        private ScenarioStep Inject(Action action) => new ScenarioStep { Delay = TimeSpan.Zero, AdvanceTicks = 1, Action = action };

        private void EnsureReady()
        {
            if (_transport == null || _mapping == null)
            {
                throw new InvalidOperationException("Scenario not initialized");
            }
        }
    }

    internal class LoopScenario : IScenario
    {
        public string Name => "loop";

        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _enemyCount;
        private int _seed;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 1;
            _seed = seed != 0 ? seed : 303;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            foreach (var matchCount in new[] { 10, 11 })
            {
                foreach (var step in RunCycle(rng, matchCount))
                {
                    yield return step;
                }
            }
        }

        private IEnumerable<ScenarioStep> RunCycle(Random rng, int matchCount)
        {
            var spawn = new Vector3(6, 0, 6);
            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { matchCount, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var totalTicks = SimulationTime.Instance.ToTicks(TimeSpan.FromSeconds(10));
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(100, _durations.PositionUpdateMs));
            var elapsed = 0L;
            while (elapsed < totalTicks)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                elapsed += intervalTicks;
                yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, Math.Max(1, _enemyCount), 10f, new Vector3(12, 0, 12)));
            }

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PositionUpdateMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchEnd"], Array.Empty<object>(), -1)));
        }

        private ScenarioStep Inject(Action action) => new ScenarioStep { Delay = TimeSpan.Zero, AdvanceTicks = 1, Action = action };

        private void EnsureReady()
        {
            if (_transport == null || _mapping == null)
            {
                throw new InvalidOperationException("Scenario not initialized");
            }
        }
    }

    internal class RespawnLoopScenario : IScenario
    {
        public string Name => "respawn_loop";

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
            _seed = seed != 0 ? seed : 404;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 1;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(5, 0, 5);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 3, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var respawnTicks = ScenarioUtils.TicksFromMs(_durations.RespawnLoopMs);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return new ScenarioStep { AdvanceTicks = respawnTicks };
                yield return Inject(() =>
                {
                    var death = new[] { new PlayerStub(_botActorId, "[BOT] Respawn", 0, false, Vector3.Zero) };
                    _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], death, _botActorId));
                });

                yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
                yield return Inject(() =>
                {
                    var respawnPos = new Vector3(5 + cycle * 2, 0, 5 + cycle * 2);
                    _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, respawnPos, 1 }, -1));
                    var aliveAgain = new[] { new PlayerStub(_botActorId, "[BOT] Respawn", 0, true, respawnPos) };
                    _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], aliveAgain, _botActorId));
                });
            }

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PositionUpdateMs) };
            yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, _enemyCount, 12f, new Vector3(12, 0, 12)));
        }

        private ScenarioStep Inject(Action action) => new ScenarioStep { Delay = TimeSpan.Zero, AdvanceTicks = 1, Action = action };

        private void EnsureReady()
        {
            if (_transport == null || _mapping == null)
            {
                throw new InvalidOperationException("Scenario not initialized");
            }
        }
    }
}
