using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    internal class BadPayloadScenario : IScenario
    {
        public string Name => "bad_payload";

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
            _seed = seed != 0 ? seed : 5150;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(3, 0, 3);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 40, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(1, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var malformedSteps = new List<Action>
            {
                () => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], null, -1)),
                () => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], new byte[] { 0, 255, 1 }, -1)),
                () => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, "not_a_vector", -1 }, -1)),
                () => _transport!.Inject(new NetEvent(0, new object[] { "unexpected", null, 123 }, -1))
            };

            foreach (var step in malformedSteps)
            {
                yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(20) };
                yield return Inject(step);
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

    internal class ReorderDropScenario : IScenario
    {
        public string Name => "reorder_drop";

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
            _seed = seed != 0 ? seed : 6262;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(7, 0, 7);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 41, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(2, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(30, _durations.PositionUpdateMs / 3));
            var batches = new List<byte[]>();
            var timestamp = 80000;
            for (var i = 0; i < 10; i++)
            {
                var pos = spawn + ScenarioUtils.RandomOffset(rng, 3f, 6f);
                var sv = ShortVector3.FromVector(pos);
                var batch = new byte[12];
                batch[0] = 1;
                batch[1] = 2;
                BitConverter.GetBytes(timestamp).CopyTo(batch, 2);
                BitConverter.GetBytes(sv.X).CopyTo(batch, 6);
                BitConverter.GetBytes(sv.Y).CopyTo(batch, 8);
                BitConverter.GetBytes(sv.Z).CopyTo(batch, 10);
                batches.Add(batch);
                timestamp += 33;
            }

            var dropIndices = batches
                .Select((b, idx) => new { b, idx })
                .Where(x => (x.idx % 5) == 0)
                .Select(x => x.idx)
                .ToHashSet();

            foreach (var (batch, index) in batches.Select((b, idx) => (b, idx)).OrderByDescending(x => x.idx))
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                if (dropIndices.Contains(index))
                {
                    continue;
                }
                yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1)));
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

    internal class ManyActorsScenario : IScenario
    {
        public string Name => "many_actors";

        private MockTransportConnection? _transport;
        private RpcMapping? _mapping;
        private ScenarioDurations _durations = new();
        private int _botActorId;
        private int _seed;
        private int _actorCount;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _transport = transport;
            _mapping = RpcMapping.Default();
            _durations = scenarioConfig.Durations ?? new ScenarioDurations();
            _botActorId = botActorId;
            _seed = seed != 0 ? seed : 7331;
            _actorCount = Math.Max(10, scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 10);
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(12, 0, 12);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 50, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_actorCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(25, _durations.PositionUpdateMs / 4));
            var timestamp = 120000;
            for (var frame = 0; frame < 30; frame++)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() =>
                {
                    var batch = new byte[1 + _actorCount * 11];
                    batch[0] = (byte)_actorCount;
                    var idx = 1;
                    for (var actor = 0; actor < _actorCount; actor++)
                    {
                        var angle = (actor * 0.5f) + frame * 0.1f;
                        var radius = 6 + actor * 0.2f;
                        var pos = spawn + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
                        var sv = ShortVector3.FromVector(pos);
                        batch[idx] = (byte)(actor + 2);
                        BitConverter.GetBytes(timestamp + actor * 2).CopyTo(batch, idx + 1);
                        BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                        BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                        BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                        idx += 11;
                    }
                    _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
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
}
