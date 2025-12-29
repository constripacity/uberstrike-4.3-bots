using System;
using System.Numerics;

namespace BotRunner.Bot
{
    /// <summary>
    /// Lightweight movement helper that creates human-like roaming and chasing paths. All movement
    /// remains within reasonable acceleration to avoid triggering speed checks.
    /// </summary>
    public class BotMovement
    {
        private readonly BotConfig _config;
        private readonly Random _random = new();

        public BotMovement(BotConfig config)
        {
            _config = config;
        }

        public (Vector3 position, Vector3 velocity) MoveTowards(Vector3 current, Vector3 target)
        {
            var direction = Vector3.Normalize(target - current);
            if (float.IsNaN(direction.X))
            {
                direction = Vector3.Zero;
            }

            var delta = direction * _config.MaxWalkSpeed * 0.05f; // 20Hz tick -> delta time = 0.05
            var next = current + delta;
            return (next, delta);
        }

        public (Vector3 position, Vector3 velocity) Chase(Vector3 current, Vector3 enemyPosition)
        {
            // Chase with a slight lead to feel responsive but still fair.
            var predicted = enemyPosition + new Vector3(0, 0, 0); // TODO: incorporate velocity if available
            return MoveTowards(current, predicted);
        }

        public Vector3 ChooseRoamTarget(Vector3 origin, int radius)
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = _random.NextDouble() * radius;
            var offset = new Vector3(
                (float)(Math.Cos(angle) * distance),
                0,
                (float)(Math.Sin(angle) * distance));
            return origin + offset;
        }
    }
}
