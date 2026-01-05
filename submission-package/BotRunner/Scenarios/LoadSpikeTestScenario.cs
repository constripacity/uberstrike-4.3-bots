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
    public class LoadSpikeTestScenario : IScenario
    {
        public string Name => "load_spike_test";

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
            _seed = seed != 0 ? seed : 99;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 1;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(2, 0, 2);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 31, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(_enemyCount, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var batch = new byte[1 + _enemyCount * 11];
            for (var i = 0; i < _enemyCount; i++)
            {
                var pos = spawn + new Vector3(3 + i, 0, 3 + i);
                var sv = ShortVector3.FromVector(pos);
                batch[0] = (byte)_enemyCount;
                var idx = 1 + i * 11;
                batch[idx] = (byte)(i + 2);
                BitConverter.GetBytes(70000 + i * 5).CopyTo(batch, idx + 1);
                BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
            }

            var burstTicks = ScenarioUtils.TicksFromMs(2);
            for (var tick = 0; tick < 50; tick++)
            {
                yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1)));
                yield return new ScenarioStep { AdvanceTicks = burstTicks };
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
