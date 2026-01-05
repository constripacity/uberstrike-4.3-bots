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
    public class SwarmRetreatScenario : IScenario
    {
        public string Name => "swarm_retreat_test";

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
            _seed = seed != 0 ? seed : 313;
            _enemyCount = scenarioConfig.EnemyCount > 0 ? scenarioConfig.EnemyCount : 3;
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            EnsureReady();
            var rng = new Random(_seed);
            var spawn = new Vector3(4, 0, 4);

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.MatchStartMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 21, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(Math.Max(3, _enemyCount), rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(80, _durations.PositionUpdateMs / 2));
            for (var wave = 0; wave < 3; wave++)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
                yield return Inject(() =>
                {
                    var batch = new byte[1 + 3 * 11];
                    batch[0] = 3;
                    var idx = 1;
                    for (var enemy = 0; enemy < 3; enemy++)
                    {
                        var pos = spawn + new Vector3(1 + enemy * 0.5f + wave * 0.2f, 0, 1 + enemy * 0.5f);
                        var sv = ShortVector3.FromVector(pos);
                        batch[idx] = (byte)(enemy + 2);
                        BitConverter.GetBytes(60000 + wave * 100 + enemy * 20).CopyTo(batch, idx + 1);
                        BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                        BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                        BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                        idx += 11;
                    }
                    _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                });
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
