using System;
using System.Numerics;

namespace BotRunner.State
{
    /// <summary>
    /// Immutable snapshot of PlayerState for read-only consumption without locking.
    /// </summary>
    public readonly record struct PlayerSnapshot(
        int ActorId,
        string Name,
        byte Team,
        bool IsAlive,
        Vector3 Position,
        DateTime LastSeenUtc,
        DateTime LastPositionUtc);
}
