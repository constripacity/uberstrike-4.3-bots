using System;
using System.Numerics;
using BotRunner.State;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Simple flanking movement: moves to a point offset to the side of the enemy relative to the bot.
    /// </summary>
    public class FlankBehavior : IBotBehavior
    {
        private readonly float _flankDistance;
        private readonly float _sideOffset;
        private readonly Utils.RunMetrics? _metrics;

        public FlankBehavior(Utils.RunMetrics? metrics = null, float flankDistance = 6f, float sideOffset = 4f)
        {
            _metrics = metrics;
            _flankDistance = flankDistance;
            _sideOffset = sideOffset;
        }

        public MovementIntent GetIntent(BotBehaviorContext ctx)
        {
            var enemy = ctx.NearestEnemy;
            if (enemy == null)
                return MovementIntent.None;

            var toEnemy = Vector3.Normalize(enemy.Position - ctx.CurrentPosition);
            // pick a perpendicular vector for flanking
            var perp = new Vector3(-toEnemy.Z, 0f, toEnemy.X);
            // choose left or right based on enemy id to add determinism
            var side = (enemy.ActorId % 2 == 0) ? 1f : -1f;
            var flankOffset = perp * (_sideOffset * side) + toEnemy * -_flankDistance;

            var targetPos = enemy.Position + flankOffset;
            var distanceToTarget = Vector3.Distance(ctx.CurrentPosition, targetPos);

            // Record metrics
            if (_metrics != null)
            {
                // Calculate crossfire angle (just a simple approximation here since we don't have allies in ctx)
                // In a real implementation we'd need allies to calculate true crossfire.
                // For now, let's record the attempt.
                _metrics.RecordFlankAttempt(distanceToTarget < 2f, 0f, distanceToTarget);
            }

            // Pathfinding fallback / reached position
            if (distanceToTarget < 1f)
            {
                // Reached flank position, switch to attack/hold
                return MovementIntent.None;
            }

            return new MovementIntent(targetPos);
        }
    }
}
