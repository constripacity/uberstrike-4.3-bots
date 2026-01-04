using System;
using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Moves perpendicular to the enemy direction to create lateral motion.
    /// </summary>
    public class StrafeBehavior : IBotBehavior
    {
        private readonly float _distance;
        private readonly Random _random;
        private int _direction = 1;

        public StrafeBehavior(float distance, int? seed = null)
        {
            _distance = Math.Max(0.5f, distance);
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            if (context.NearestEnemy == null)
            {
                return MovementIntent.None;
            }

            // Occasionally flip direction to avoid circles.
            if (_random.NextDouble() < 0.1)
            {
                _direction *= -1;
            }

            var toEnemy = context.NearestEnemy.Position - context.CurrentPosition;
            if (toEnemy.LengthSquared() < 0.0001f)
            {
                return MovementIntent.None;
            }

            toEnemy = Vector3.Normalize(toEnemy);
            var lateral = new Vector3(-toEnemy.Z, 0f, toEnemy.X) * _direction;
            var target = context.CurrentPosition + lateral * _distance;
            return new MovementIntent(target);
        }
    }
}
