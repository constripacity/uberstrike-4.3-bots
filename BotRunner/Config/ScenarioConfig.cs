using System;

namespace BotRunner.Config
{
    /// <summary>
    /// Configuration for offline scenarios. Keeps the offline harness deterministic by allowing
    /// seeds and timing controls to be specified in appsettings.
    /// </summary>
    public class ScenarioConfig
    {
        public string ScenarioName { get; set; } = "demo";
        public int Seed { get; set; } = 0;
        public int EnemyCount { get; set; } = 1;
        public ScenarioDurations Durations { get; set; } = new();
    }

    public class ScenarioDurations
    {
        public int MatchStartMs { get; set; } = 100;
        public int PlayerListMs { get; set; } = 100;
        public int SpawnMs { get; set; } = 100;
        public int PositionUpdateMs { get; set; } = 200;
        public int RespawnLoopMs { get; set; } = 1500;
    }
}
