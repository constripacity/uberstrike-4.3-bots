using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public class TeamDuelScenario : IScenario
    {
        public string Name => "team_duel";

        private MockTransportConnection? _transport;
        private readonly List<PlayerStub> _teamA = new();
        private readonly List<PlayerStub> _teamB = new();
        private int _botTeamId = 0;
        private WorldState? _worldState;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId)
        {
            _transport = transport;
            _worldState = worldState;
            _botTeamId = botConfig.TeamId;
            var rpcMapping = RpcMapping.Default();

            // Create 2 friendly bots
            _teamA.Add(new PlayerStub(1000, "[BOT] Alpha", (byte)_botTeamId, true, new Vector3(-10, 0, 0)));
            _teamA.Add(new PlayerStub(1001, "[BOT] Beta", (byte)_botTeamId, true, new Vector3(-15, 0, 5)));

            // Create 2 enemies
            var enemyTeam = (byte)(_botTeamId == 0 ? 1 : 0);
            _teamB.Add(new PlayerStub(2000, "Enemy1", enemyTeam, true, new Vector3(10, 0, 0)));
            _teamB.Add(new PlayerStub(2001, "Enemy2", enemyTeam, true, new Vector3(15, 0, -5)));

            // Inject match start and full player list
            _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 999999 }, -1));
            var allPlayers = new List<PlayerStub>();
            allPlayers.AddRange(_teamA);
            allPlayers.AddRange(_teamB);
            _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], allPlayers.ToArray(), -1));

            // Spawn bots (allow)
            foreach (var bot in _teamA)
            {
                _transport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { bot.ActorId, bot.Position, 0 }, -1));
            }

            Logger.Info("[Scenario] TeamDuel initialized");
        }

        public IEnumerable<ScenarioStep> GetSteps()
        {
            // Phase -1: allow initial events to flush
            yield return new ScenarioStep
            {
                Delay = TimeSpan.FromMilliseconds(100),
                Action = () => { }
            };

            // Phase 0: Setup tactical state after flush
            yield return new ScenarioStep
            {
                Delay = TimeSpan.FromMilliseconds(100),
                Action = () =>
                {
                    if (_worldState != null)
                    {
                        var allyAlpha = _worldState.Get(1000);
                        var allyBeta = _worldState.Get(1001);
                        if (allyAlpha != null && allyBeta != null)
                        {
                            allyAlpha.UpdateTactical(new Vector3(1, 0, 0), 2000);
                            allyBeta.UpdateTactical(new Vector3(1, 0, 0), 2000);
                            Logger.Info("[Scenario] Allies targeting Enemy 2000");
                        }
                        else
                        {
                            Logger.Warn("[Scenario] Allies not found in WorldState yet!");
                        }
                    }
                }
            };

            // Phase 1: let initial engagement run for 2 seconds (20 * 100ms)
            for (int i = 0; i < 20; i++)
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromMilliseconds(100),
                    Action = () => { /* advance time */ }
                };
            }

            // Phase 2: force one enemy to retreat (simulate target switching)
            yield return new ScenarioStep
            {
                Delay = TimeSpan.FromSeconds(1),
                Action = () =>
                {
                    try
                    {
                        var rpcMapping = RpcMapping.Default();
                        var retreatPos = new Vector3(50, 0, 0);
                        // Replace enemy1 position via a full player list update to ensure world state sees it
                        var all = new List<PlayerStub>();
                        all.AddRange(_teamA);
                        all.Add(new PlayerStub(2000, "Enemy1", (byte)(_teamA[0].Team == 0 ? 1 : 0), true, retreatPos));
                        all.AddRange(_teamB.FindAll(e => e.ActorId != 2000));
                        _transport?.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], all.ToArray(), -1));
                        Logger.Info("[Scenario] Forced Enemy 2000 to retreat");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn($"[Scenario] Force retreat failed: {ex.Message}");
                    }
                }
            };

            // Phase 3: continue for 3 seconds
            for (int i = 0; i < 30; i++)
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromMilliseconds(100),
                    Action = () => { /* advance time */ }
                };
            }

            // Evaluation
            yield return new ScenarioStep
            {
                Delay = TimeSpan.Zero,
                Action = () =>
                {
                    Logger.Info("[Scenario] Team Duel completed - evaluation step (survival/focus-fire should be checked in logs)");
                }
            };
        }
    }
}