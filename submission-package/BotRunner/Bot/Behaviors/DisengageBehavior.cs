using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Backs away from the nearest enemy to regain space.
    /// </summary>
    public class DisengageBehavior : IBotBehavior
    {
        private readonly float _desiredSeparation;

        public DisengageBehavior(float desiredSeparation)
        {
            _desiredSeparation = desiredSeparation;
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            if (context.NearestEnemy == null)
            {
                return MovementIntent.None;
            }

            var direction = context.CurrentPosition - context.NearestEnemy.Position;
            if (direction.LengthSquared() < 0.0001f)
            {
                direction = new Vector3(0, 0, 1);
            }

            direction = Vector3.Normalize(direction);
            var target = context.CurrentPosition + direction * _desiredSeparation;
            return new MovementIntent(target);
        }
    }
}
