using System;
using System.Numerics;

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
        public int? PendingSpawnActorId { get; private set; }
        public Vector3 PendingSpawnPosition { get; private set; }
        public bool HasPendingSpawnPosition { get; private set; }

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

        public void OnSpawnInstruction(int actorId, Vector3 position, int cooldownSeconds)
        {
            lock (_lock)
            {
                PendingSpawnActorId = actorId;
                PendingSpawnPosition = position;
                HasPendingSpawnPosition = true;
                NextSpawnPointIndex = -1;
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

        public bool TryConsumeSpawnFor(int actorId, out Vector3 position)
        {
            lock (_lock)
            {
                if (!HasPendingSpawnPosition || PendingSpawnActorId != actorId)
                {
                    position = Vector3.Zero;
                    return false;
                }

                position = PendingSpawnPosition;
                HasPendingSpawnPosition = false;
                return true;
            }
        }

        public override string ToString()
        {
            return $"MatchState(running={MatchRunning}, endTicks={MatchEndServerTicks}, nextSpawn={NextSpawnPointIndex}, cooldown={RespawnCooldownSeconds}s)";
        }
    }
}
