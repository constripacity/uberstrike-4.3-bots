using System;

namespace BotRunner.State
{
    /// <summary>
    /// Tracks match lifecycle and cooldowns that gate bot behavior.
    /// </summary>
    public class MatchState
    {
        public bool MatchRunning { get; set; }
        public DateTime LastSpawnAllowedAt { get; set; } = DateTime.MinValue;
        public DateTime LastDeathAt { get; set; } = DateTime.MinValue;
    }
}
