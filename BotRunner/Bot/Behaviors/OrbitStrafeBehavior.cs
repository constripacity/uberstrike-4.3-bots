using System;
using System.Numerics;

namespace BotRunner.Bot.Behaviors
{
    /// <summary>
    /// Orbits around the nearest enemy while maintaining a preferred distance band.
    /// Introduces periodic direction flips to avoid tight circles.
    /// </summary>
    public class OrbitStrafeBehavior : IBotBehavior
    {
        private readonly float _idealDistance;
        private readonly float _minDistance;
        private readonly float _maxDistance;
        private readonly float _strideMeters;
        private readonly Random _random;
        private readonly float _flipMinSeconds;
        private readonly float _flipMaxSeconds;
        private int _direction = 1;
        private DateTime _nextFlipUtc = DateTime.MinValue;

        public OrbitStrafeBehavior(
            float idealDistance,
            float minDistance,
            float maxDistance,
            float strideMeters = 2f,
            float flipMinSeconds = 2f,
            float flipMaxSeconds = 4f,
            int? seed = null)
        {
            _idealDistance = Math.Max(0.5f, idealDistance);
            _minDistance = Math.Max(0.5f, minDistance);
            _maxDistance = Math.Max(_minDistance + 0.1f, maxDistance);
            _strideMeters = Math.Max(0.25f, strideMeters);
            _flipMinSeconds = Math.Max(0.25f, flipMinSeconds);
            _flipMaxSeconds = Math.Max(_flipMinSeconds, flipMaxSeconds);
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            if (context.NearestEnemy == null)
            {
                return MovementIntent.None;
            }

            var now = context.NowUtc;
            if (now >= _nextFlipUtc)
            {
                _direction *= -1;
                var next = _random.NextDouble() * (_flipMaxSeconds - _flipMinSeconds) + _flipMinSeconds;
                _nextFlipUtc = now + TimeSpan.FromSeconds(next);
            }

            var toSelf = context.CurrentPosition - context.NearestEnemy.Position;
            if (toSelf.LengthSquared() < 0.0001f)
            {
                toSelf = new Vector3(1f, 0f, 0f);
            }

            var radial = Vector3.Normalize(toSelf);
            var distance = toSelf.Length();
            var distanceError = Math.Clamp(_idealDistance - distance, -_strideMeters, _strideMeters);
            var rangeCorrection = radial * distanceError;

            var tangent = new Vector3(-radial.Z, 0f, radial.X) * _direction;
            var tangentialStep = tangent * _strideMeters;

            // Encourage returning to the preferred band if far out of range.
            if (distance < _minDistance)
            {
                rangeCorrection = radial * -Math.Min(_strideMeters, _minDistance - distance);
            }
            else if (distance > _maxDistance)
            {
                rangeCorrection = radial * Math.Min(_strideMeters, distance - _maxDistance);
            }

            var target = context.CurrentPosition + tangentialStep + rangeCorrection;
            return new MovementIntent(target);
        }
    }
}
