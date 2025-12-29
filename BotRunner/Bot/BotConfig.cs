using System;

namespace BotRunner.Bot
{
    /// <summary>
    /// Tunable parameters that shape how human-like the bot feels. These values are intentionally
    /// conservative to avoid exceeding what the original client would do.
    /// </summary>
    public class BotConfig
    {
        public int ReactionDelayMs { get; set; } = 200;
        public float AimErrorDegrees { get; set; } = 3.5f;
        public int RoamRadius { get; set; } = 40;
        public float MaxWalkSpeed { get; set; } = 6.5f; // meters per second, aligned with default player speeds
        public int FireRateMs { get; set; } = 220; // Controlled by weapon cadence
        public int RespawnDelayMs { get; set; } = 2200;
    }
}
