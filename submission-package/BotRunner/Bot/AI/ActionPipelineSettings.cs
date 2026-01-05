using System;

namespace BotRunner.Bot.AI
{
    public class ActionPipelineSettings
    {
        // Commit durations (ms)
        public int MinShootCommitMs { get; set; } = 250;
        public int MinRepositionCommitMs { get; set; } = 250;

        // Thresholds
        public float ShootConfidenceThreshold { get; set; } = 0.65f;
        public float ShootDistanceMax { get; set; } = 20f;
    }
}
