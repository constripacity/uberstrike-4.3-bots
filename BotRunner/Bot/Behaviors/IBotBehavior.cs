using System.Numerics;
using System;
using BotRunner.State;

namespace BotRunner.Bot.Behaviors
{
    public interface IBotBehavior
    {
        MovementIntent GetIntent(BotBehaviorContext context);
    }

    public readonly struct BotBehaviorContext
    {
        public BotBehaviorContext(Vector3 currentPosition, PlayerState? self, PlayerState? nearestEnemy, DateTime? nowUtc = null)
        {
            CurrentPosition = currentPosition;
            Self = self;
            NearestEnemy = nearestEnemy;
            NowUtc = nowUtc ?? DateTime.UtcNow;
        }

        public Vector3 CurrentPosition { get; }
        public PlayerState? Self { get; }
        public PlayerState? NearestEnemy { get; }
        public DateTime NowUtc { get; }
    }

    public readonly struct MovementIntent
    {
        public static MovementIntent None => new(false, Vector3.Zero);

        public MovementIntent(Vector3 targetPosition)
        {
            HasTarget = true;
            TargetPosition = targetPosition;
        }

        private MovementIntent(bool hasTarget, Vector3 targetPosition)
        {
            HasTarget = hasTarget;
            TargetPosition = targetPosition;
        }

        public bool HasTarget { get; }
        public Vector3 TargetPosition { get; }
    }
}
