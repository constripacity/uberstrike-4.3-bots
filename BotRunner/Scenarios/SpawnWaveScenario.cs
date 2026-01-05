using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public class SpawnWaveScenario : IScenario
    {
        public string Name => "spawn_wave";

        private MockTransportConnection? _transport;
        private int _wave = 0;
        private readonly List<int> _enemyIds = new();
        private int _botActorId = 1000;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId)
        {
            _transport = transport;
            _botActorId = botActorId;
            var rpc = RpcMapping.Default();

            // Single bot
            var bot = new PlayerStub(_botActorId, "[BOT] Defender", (byte)botConfig.TeamId, true, Vector3.Zero);
            _transport.Inject(new NetEvent(rpc.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 999999 }, -1));
            _transport.Inject(new NetEvent(rpc.RpcNameToId["GameRPC.FullPlayerListUpdate"], new[] { bot }, -1));
            _transport.Inject(new NetEvent(rpc.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { _botActorId, Vector3.Zero, 0 }, -1));
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            // 5 waves, spawn every 3 seconds
            for (_wave = 1; _wave <= 5; _wave++)
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromSeconds(3),
                    Action = () => SpawnWave(_wave)
                };

                // Combat phase 3 seconds (30 * 100ms)
                for (int i = 0; i < 30; i++)
                {
                    yield return new ScenarioStep
                    {
                        Delay = TimeSpan.FromMilliseconds(100),
                        Action = () => { /* advance time */ }
                    };
                }
            }

            // Evaluation
            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () =>
                {
                    Logger.Info("[Scenario] SpawnWave completed - evaluation step (check wave survival in logs)");
                }
            };
        }

        private void SpawnWave(int waveNumber)
        {
            if (_transport == null) return;
            var rpc = RpcMapping.Default();
            var enemyCount = waveNumber;
            var created = new List<PlayerStub>();
            for (int i = 0; i < enemyCount; i++)
            {
                var angle = (i * 2 * Math.PI) / Math.Max(1, enemyCount);
                var pos = new Vector3((float)(Math.Cos(angle) * 20), 0, (float)(Math.Sin(angle) * 20));
                var enemyId = 2000 + (waveNumber * 10) + i;
                _enemyIds.Add(enemyId);
                created.Add(new PlayerStub(enemyId, $"Wave{waveNumber}_Enemy{i}", 1, true, pos));
            }

            _transport.Inject(new NetEvent(rpc.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], created.ToArray(), -1));
            foreach (var e in created)
            {
                _transport.Inject(new NetEvent(rpc.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { e.ActorId, e.Position, 0 }, -1));
            }

            Logger.Info($"[Scenario] SpawnWave {waveNumber}: spawned {enemyCount} enemies");
        }
    }
}
