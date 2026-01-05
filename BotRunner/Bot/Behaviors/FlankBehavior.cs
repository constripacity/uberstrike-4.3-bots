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

        public FlankBehavior(float flankDistance = 6f, float sideOffset = 4f)
        {
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
            return new MovementIntent(targetPos);
        }
    }
}
