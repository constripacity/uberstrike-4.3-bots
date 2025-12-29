using System;

namespace BotRunner.State
{
    /// <summary>
    /// Minimal match lifecycle model with basic spawn cooldown tracking.
    /// </summary>
    public class MatchState
    {
        private readonly object _lock = new();

        public bool MatchRunning { get; private set; }
        public int? MatchEndServerTicks { get; private set; }
        public DateTime LastSpawnAllowedAtUtc { get; private set; } = DateTime.MinValue;
        public int NextSpawnPointIndex { get; private set; } = -1;
        public int RespawnCooldownSeconds { get; private set; } = 0;
        public int LastKnownServerTicks { get; private set; } = 0;
        public int MatchCount { get; private set; }

        public void OnMatchStart(int matchCount, int matchEndServerTicks)
        {
            lock (_lock)
            {
                MatchRunning = true;
                MatchEndServerTicks = matchEndServerTicks;
                MatchCount = matchCount;
            }
        }

        public void OnMatchEnd()
        {
            lock (_lock)
            {
                MatchRunning = false;
            }
        }

        public void OnSpawnInstruction(int spawnIndex, int cooldownSeconds)
        {
            lock (_lock)
            {
                NextSpawnPointIndex = spawnIndex;
                RespawnCooldownSeconds = cooldownSeconds;
                LastSpawnAllowedAtUtc = DateTime.UtcNow;
            }
        }

        public void UpdateServerTicks(int ticks)
        {
            lock (_lock)
            {
                if (ticks > LastKnownServerTicks)
                {
                    LastKnownServerTicks = ticks;
                }
            }
        }

        public bool CanRespawnNow(DateTime utcNow)
        {
            lock (_lock)
            {
                if (!MatchRunning)
                {
                    return false;
                }

                var readyAt = LastSpawnAllowedAtUtc.AddSeconds(RespawnCooldownSeconds);
                return utcNow >= readyAt;
            }
        }

        public override string ToString()
        {
            return $"MatchState(running={MatchRunning}, endTicks={MatchEndServerTicks}, nextSpawn={NextSpawnPointIndex}, cooldown={RespawnCooldownSeconds}s)";
        }
    }
}
