using System;
using System.Collections.Generic;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;
using BotRunner.Config;

namespace BotRunner.Scenarios
{
    public interface IScenario
    {
        string Name { get; }
        void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId, ScenarioConfig scenarioConfig);
        IEnumerable<ScenarioStep> GetSteps();
    }

    public class ScenarioStep
    {
        public TimeSpan Delay { get; set; }
        public long AdvanceTicks { get; set; } = 0;
        public Action Action { get; set; } = () => { };
    }
}
