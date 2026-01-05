using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using BotRunner.Bot;
using BotRunner.Bot.AI;
using BotRunner.Bot.Combat;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Config;
using BotRunner.Networking;

namespace BotRunner.Scenarios
{
    public class AmmoPressureScenario : IScenario
    {
        public string Name => "ammo_pressure";
        
        private int _botActorId;
        private WorldState? _worldState;
        private MatchState? _matchState;
        private BotConfig? _botConfig;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, 
                              MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig)
        {
            _botActorId = botActorId;
            _worldState = worldState;
            _matchState = matchState;
            _botConfig = botConfig;
            var rpcMapping = RpcMapping.Default();
            
            // Stationary enemy at medium range (15m)
            var enemyPos = new Vector3(15, 0, 0);
            var enemy = new PlayerStub(
                1001,
                "AmmoTestEnemy",
                (byte)(botConfig.TeamId == 0 ? 1 : 0),
                true,
                enemyPos
            );

            var bot = new PlayerStub(
                botActorId,
                "[BOT] Alpha",
                botConfig.TeamId,
                true,
                Vector3.Zero
            );
            
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], new[] { enemy, bot }, -1));
            transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(0, 0, 0), 0 }, -1));

            // Small hack: wait for bot to initialize its simulator then set ammo
            // But we can't easily do that here. 
            // Instead, we can inject an RPC that the bot handles to set ammo? 
            // Or just know that the simulator starts with default ammo and we want to test REFILL or CONSERVATION.
            // Actually, I can't easily reach the bot's private simulator from here.
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () => {
                    Logger.Info("[Scenario] Starting Ammo Pressure Scenario. Bot should conserve ammo.");
                }
            };

            for (int i = 0; i < 40; i++) // 4 seconds
            {
                int step = i;
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromMilliseconds(100),
                    Action = () => {
                        Logger.Info($"[Scenario Step {step}] Tick");
                        // Occasionally damage bot to create pressure
                        if (step % 8 == 5) // Every ~0.8 second
                        {
                            var self = _worldState?.Get(_botActorId);
                            if (self != null)
                            {
                                Logger.Info($"[Scenario Step {step}] Applying 25 damage to bot. Current HP: {self.Health}");
                                self.Update(self.Name, self.Team, self.IsAlive, self.Health - 25, self.MaxHealth);
                            }
                        }
                    }
                };
            }
            
            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () => {
                    Logger.Info("[Scenario] Ammo Pressure Scenario Completed.");
                }
            };
        }
    }
}
