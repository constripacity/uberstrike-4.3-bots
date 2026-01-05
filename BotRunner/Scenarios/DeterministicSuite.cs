using System.Collections.Generic;
using BotRunner.Config;

namespace BotRunner.Scenarios
{
    public static class DeterministicSuite
    {
        public static IEnumerable<IScenario> BuildSuite(ScenarioConfig config)
        {
            var seed = config.Seed == 0 ? 42 : config.Seed;
            config.Seed = seed;

            return new IScenario[]
            {
                new FlippingTestScenario(),
                new SwarmRetreatScenario(),
                new LoadSpikeTestScenario(),
                new StateIntegrityScenario()
            };
        }
    }
}
