using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Holds current position to reduce movement.
    /// </summary>
    public class HoldPositionBehavior : IBotBehavior
    {
        private readonly float _preferredMin;
        private readonly float _preferredMax;

        public HoldPositionBehavior(float preferredMin, float preferredMax)
        {
            _preferredMin = preferredMin;
            _preferredMax = preferredMax;
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            return MovementIntent.None;
        }

        public bool IsInPreferredBand(float distance) => distance >= _preferredMin && distance <= _preferredMax;
    }
}
