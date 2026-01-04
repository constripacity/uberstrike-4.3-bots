using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Holds current position to reduce movement.
    /// </summary>
    public class HoldPositionBehavior : IBotBehavior
    {
        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            return MovementIntent.None;
        }
    }
}
