using System;
using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Roams around a center point by picking random targets inside a radius.
    /// </summary>
    public class WanderBehavior : IBotBehavior
    {
        private readonly float _roamRadiusMeters;
        private readonly float _arrivalThresholdMeters;
        private readonly Random _random;
        private Vector3 _roamCenter;
        private Vector3 _currentTarget;
        private bool _hasTarget;

        public WanderBehavior(Vector3 roamCenter, float roamRadiusMeters, float arrivalThresholdMeters, int? seed = null)
        {
            _roamCenter = roamCenter;
            _roamRadiusMeters = roamRadiusMeters;
            _arrivalThresholdMeters = arrivalThresholdMeters;
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            var currentPos = context.CurrentPosition;
            if (!_hasTarget || Vector3.Distance(currentPos, _currentTarget) < _arrivalThresholdMeters)
            {
                _currentTarget = PickNewTarget();
                _hasTarget = true;
            }

            return new MovementIntent(_currentTarget);
        }

        public void SetRoamCenter(Vector3 center)
        {
            _roamCenter = center;
        }

        private Vector3 PickNewTarget()
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var radius = _random.NextDouble() * _roamRadiusMeters;
            return _roamCenter + new Vector3(
                (float)(Math.Cos(angle) * radius),
                0f,
                (float)(Math.Sin(angle) * radius));
        }
    }
}
