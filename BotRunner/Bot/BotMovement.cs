using System;
using System.Numerics;

namespace BotRunner.Bot
{
    /// <summary>
    /// Minimal wander movement: picks random targets around a center and walks toward them at a steady speed.
    /// </summary>
    public class BotMovement
    {
        private readonly Vector3 _roamCenter;
        private readonly float _roamRadiusMeters;
        private readonly float _speedMetersPerSec;
        private readonly float _arrivalThresholdMeters;
        private readonly Random _random;
        private Vector3 _currentTarget;
        private bool _hasTarget;

        public BotMovement(Vector3 roamCenter, float roamRadiusMeters, float speedMetersPerSec, float arrivalThresholdMeters, int? randomSeed = null)
        {
            _roamCenter = roamCenter;
            _roamRadiusMeters = roamRadiusMeters;
            _speedMetersPerSec = speedMetersPerSec;
            _arrivalThresholdMeters = arrivalThresholdMeters;
            _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        public Vector3 Step(Vector3 currentPos, float deltaSeconds)
        {
            if (!_hasTarget || Vector3.Distance(currentPos, _currentTarget) < _arrivalThresholdMeters)
            {
                _currentTarget = PickNewTarget();
                _hasTarget = true;
            }

            var toTarget = _currentTarget - currentPos;
            var dist = toTarget.Length();
            if (dist < float.Epsilon)
            {
                return currentPos;
            }

            var maxStep = _speedMetersPerSec * deltaSeconds;
            var step = Math.Min(dist, maxStep);
            var dir = toTarget / dist;
            return currentPos + dir * step;
        }

        public override string ToString()
        {
            return $"BotMovement(center={_roamCenter}, radius={_roamRadiusMeters}, speed={_speedMetersPerSec}, arrival={_arrivalThresholdMeters})";
        }

        private Vector3 PickNewTarget()
        {
            var angle = _random.NextDouble() * Math.PI * 2;
            var radius = _random.NextDouble() * _roamRadiusMeters;
            var offset = new Vector3(
                (float)(Math.Cos(angle) * radius),
                0f,
                (float)(Math.Sin(angle) * radius));
            return _roamCenter + offset;
        }
    }
}
