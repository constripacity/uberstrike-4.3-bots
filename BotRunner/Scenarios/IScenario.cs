using System;
using System.Collections.Generic;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Bot;

namespace BotRunner.Scenarios
{
    public interface IScenario
    {
        string Name { get; }
        void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId);
        IEnumerable<ScenarioStep> GetSteps();
    }

    public class ScenarioStep
    {
        public TimeSpan Delay { get; set; }
        public Action Action { get; set; } = () => { };
    }
}
