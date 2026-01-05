using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public static class DeterministicSuiteRunner
    {
        public static IEnumerable<IScenario> BuildRegressionSuite(ScenarioConfig config)
        {
            var seed = config.Seed == 0 ? 12345 : config.Seed;
            return new IScenario[]
            {
                new BadPayloadScenario(),
                new ReorderDropScenario(),
                new DuelScenario(seed),
                new SwarmScenario(seed ^ 0xFACE),
                new RetreatScenario(seed ^ 0xBEEF),
                new LoadSpikeScenario(seed ^ 0x1234)
            };
        }
    }

    internal class DuelScenario : IScenario
    {
        public string Name => "duel";
        private readonly int _seed;
        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _enemyCount;
        private Vector3 _spawn = new(10, 0, 10);

        public DuelScenario(int seed)
        {
            _seed = seed;
        }

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 1;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var engageDistances = new[] { 8f, 14f, 20f };

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 11, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, _spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, _spawn, 0 }, -1)));

            var timestamp = 200000;
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(75, _durations.PositionUpdateMs / 2));
            foreach (var dist in engageDistances)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() =>
                {
                    var pos = new Vector3(_spawn.X + dist, _spawn.Y, _spawn.Z);
                    ScenarioHelpers.InjectPosition(_transport!, _mapping!, timestamp, pos);
                });
                timestamp += 33;
            }
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

    internal class SwarmScenario : IScenario
    {
        public string Name => "swarm";
        private readonly int _seed;
        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private Vector3 _spawn = new(6, 0, 6);

        public SwarmScenario(int seed)
        {
            _seed = seed;
        }

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 12, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(3, rng, _botActorId, _spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, _spawn, 0 }, -1)));

            var basePos = new Vector3(12, 0, 12);
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(60, _durations.PositionUpdateMs / 3));
            for (var i = 0; i < 6; i++)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, 3, 10f, basePos));
            }
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

    internal class RetreatScenario : IScenario
    {
        public string Name => "retreat";
        private readonly int _seed;
        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private Vector3 _spawn = new(4, 0, 4);
        private Vector3 _threatPos = new(5, 0, 5);

        public RetreatScenario(int seed)
        {
            _seed = seed;
        }

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 13, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(1, rng, _botActorId, _spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, _spawn, 0 }, -1)));

            var timestamp = 300000;
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(70, _durations.PositionUpdateMs / 2));
            for (var i = 0; i < 5; i++)
            {
                var offset = i % 2 == 0 ? -0.5f : 0.5f;
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() =>
                {
                    var pos = new Vector3(_threatPos.X + offset, _threatPos.Y, _threatPos.Z + offset);
                    ScenarioHelpers.InjectPosition(_transport!, _mapping!, timestamp, pos);
                });
                timestamp += 33;
            }
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

    internal class LoadSpikeScenario : IScenario
    {
        public string Name => "load_spike";
        private readonly int _seed;
        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;

        public LoadSpikeScenario(int seed)
        {
            _seed = seed;
        }

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(8, 0, 8);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 14, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(1, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(25, _durations.PositionUpdateMs / 5));
            for (var i = 0; i < 20; i++)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() => ScenarioUtils.InjectEnemyBatch(_transport!, _mapping!, rng, 1, 15f, spawn + new Vector3(6, 0, 6)));
            }
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

    public static class ScenarioHelpers
    {
        internal static void InjectPosition(MockTransportConnection mock, RpcMapping mapping, int timestamp, Vector3 pos, int actorId = 2)
        {
            var sv = ShortVector3.FromVector(pos);
            var batch = new byte[12];
            batch[0] = 1;
            batch[1] = (byte)(actorId & 0xFF); // actor id (low byte)
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
