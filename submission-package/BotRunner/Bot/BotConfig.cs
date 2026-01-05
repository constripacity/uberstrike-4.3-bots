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
        public float MaxWalkSpeed { get; set; } = 6.5f; // meters per second, aligned with default player speeds
        public int FireRateMs { get; set; } = 220; // Controlled by weapon cadence
        public int RespawnDelayMs { get; set; } = 2200;
        public UtilityAiSettings Utility { get; set; } = new();
        public CombatIntentSettings Combat { get; set; } = new();
    }

    public class UtilityAiSettings
    {
        public float StickinessBonus { get; set; } = 0.10f;
        public int MinHoldMilliseconds { get; set; } = 450;
        public float OverrideDelta { get; set; } = 0.22f;
        public float NoiseAmplitude { get; set; } = 0.006f;
        public float PanicDistanceMeters { get; set; } = 10f;
        public float PreferredMinMeters { get; set; } = 14f;
        public float PreferredMaxMeters { get; set; } = 22f;
        public float StrafeMaxMeters { get; set; } = 26f;
    }

    public class CombatIntentSettings
    {
        public float CloseRangeMeters { get; set; } = 8f;
        public float MidRangeMeters { get; set; } = 20f;
        public float FarRangeMeters { get; set; } = 35f;
        public float MaxSightDistanceMeters { get; set; } = 45f;
        public float SightAngleDegrees { get; set; } = 80f;
        public float AimLeadSeconds { get; set; } = 0.15f;
        public int ClipSize { get; set; } = 12;
        public float ReloadSeconds { get; set; } = 1.6f;
    }
}
