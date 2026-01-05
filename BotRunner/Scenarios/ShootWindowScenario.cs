using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Bot;

namespace BotRunner.Scenarios
{
    /// <summary>
    /// Tests if bot maintains consistent shooting intervals.
    /// Enemy stands still at optimal range - bot should shoot at regular intervals.
    /// Success: Shoot intervals vary by < 50ms across runs with same seed.
    /// </summary>
    public class ShootWindowScenario : IScenario
    {
        public string Name => "shoot_window_test";
        
        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, 
                              MatchState matchState, BotConfig botConfig, int botActorId)
        {
            var rpcMapping = RpcMapping.Default();
            // Place enemy at perfect shooting distance (e.g., 10m)
            var enemyPos = new Vector3(10, 0, 0);
            var enemy = new PlayerStub(
                1001,
                "TestEnemy",
                (byte)(botConfig.TeamId == 0 ? 1 : 0),
                true,
                enemyPos
            );
            
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], new[] { enemy }, -1));
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(0, 0, 0), 0 }, -1));
        }
        
        public IEnumerable<ScenarioStep> GetSteps()
        {
            // Let bot shoot for 5 seconds
            for (int i = 0; i < 50; i++) // 50 * 100ms = 5 seconds
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromMilliseconds(100),
                    Action = () => { /* Just advance time */ }
                };
            }
            
            // Evaluation step
            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () => {}
            };
        }
    }
}
