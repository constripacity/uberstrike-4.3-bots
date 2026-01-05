using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public class WeaponTestScenario : IScenario
    {
        public string Name => "weapon_test";

        private MockTransportConnection? _transport;
        private WorldState? _worldState;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId)
        {
            _transport = transport;
            _worldState = worldState;
            var rpcMapping = RpcMapping.Default();

            // Inject match start
            _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));

            // Create 1 enemy
            var players = new[] { new PlayerStub(2000, "TargetDummy", (byte)(botConfig.TeamId == 0 ? 1 : 0), true, new Vector3(30, 0, 0)) };
            _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

            // Spawn allowed
            _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, Vector3.Zero, 0 }, -1));

            Logger.Info("[Scenario] WeaponTest initialized");
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            // Wait for spawn
            yield return new ScenarioStep { Delay = TimeSpan.FromSeconds(1), Action = () => { } };

            var ranges = new float[] { 5f, 15f, 30f, 50f };

            foreach (var range in ranges)
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromSeconds(3),
                    Action = () =>
                    {
                        Logger.Info($"[Scenario] Moving target to range {range}m");
                        var rpcMapping = RpcMapping.Default();
                        var players = new[] { new PlayerStub(2000, "TargetDummy", 1, true, new Vector3(range, 0, 0)) };
                        _transport?.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                    }
                };
            }

            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () =>
                {
                    Logger.Info("[Scenario] Weapon test completed");
                }
            };
        }
    }
}
