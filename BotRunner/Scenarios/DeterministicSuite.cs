using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;

namespace BotRunner.Scenarios
{
    public static class DeterministicSuite
    {
        public static async Task Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            var seed = config.Seed == 0 ? 42 : config.Seed;
            var baseConfig = new ScenarioConfig
            {
                Durations = config.Durations,
                EnemyCount = config.EnemyCount,
                Seed = seed
            };

            await FlippingTest.Run(mock, mapping, baseConfig, botActorId);
            await SwarmRetreatTest.Run(mock, mapping, baseConfig, botActorId);
            await LoadSpikeTest.Run(mock, mapping, baseConfig, botActorId);
            await StateIntegrityTest.Run(mock, mapping, baseConfig, botActorId);
        }
    }
}
