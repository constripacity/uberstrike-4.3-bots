using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Moves toward the nearest enemy to close distance.
    /// </summary>
    public class ChaseNearestEnemyBehavior : IBotBehavior
    {
        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            if (context.NearestEnemy == null)
            {
                return MovementIntent.None;
            }

            return new MovementIntent(context.NearestEnemy.Position);
        }
    }
}
