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
    public class FlippingTestScenario : IScenario
    {
        public string Name => "flipping_test";

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
            yield return Inject(() => _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 5, 999999 }, -1)));

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.PlayerListMs) };
            yield return Inject(() =>
            {
                var players = ScenarioUtils.BuildPlayers(1, rng, _botActorId, spawn);
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
            });

            yield return new ScenarioStep { AdvanceTicks = ScenarioUtils.TicksFromMs(_durations.SpawnMs) };
            yield return Inject(() =>
                _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, spawn, 0 }, -1)));

            var flipOffsets = new[] { -0.4f, 0.4f, -0.2f, 0.25f, -0.1f, 0.35f, -0.3f, 0.3f };
            var timestamp = 50000;
            var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(80, _durations.PositionUpdateMs / 2));
            foreach (var offset in flipOffsets)
            {
                yield return new ScenarioStep { AdvanceTicks = intervalTicks };
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
                });
                timestamp += 33;
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
