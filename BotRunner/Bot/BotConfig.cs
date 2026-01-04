using System;

namespace BotRunner.Bot
{
    /// <summary>
    /// Tunable parameters that shape how human-like the bot feels. These values are intentionally
    /// conservative to avoid exceeding what the original client would do.
    /// </summary>
    public class BotConfig
    {
        public byte TeamId { get; set; } = 0;
        public TimeSpan EnemyStaleTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public float EngageDistanceMeters { get; set; } = 30f;
        public float RoamRadiusMeters { get; set; } = 40f;
        public int ReactionDelayMs { get; set; } = 200;
        public float JitterStrengthMeters { get; set; } = 0.25f;
        public float AimErrorDegrees { get; set; } = 3.5f;
        public int RoamRadius { get; set; } = 40;
        public float MaxWalkSpeed { get; set; } = 6.5f; // meters per second, aligned with default player speeds
        public int FireRateMs { get; set; } = 220; // Controlled by weapon cadence
        public int RespawnDelayMs { get; set; } = 2200;
    }
}
