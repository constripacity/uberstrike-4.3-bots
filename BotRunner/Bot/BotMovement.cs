using System;
using System.Numerics;

namespace BotRunner.Bot
{
    public class BotMovementConfig
    {
        public float RoamRadiusMeters { get; set; } = 40f;
        public float ArrivalThresholdMeters { get; set; } = 1f;
        public float SpeedMetersPerSec { get; set; } = 6.5f;
        public Vector3 RoamCenter { get; set; } = Vector3.Zero;
        public int? RandomSeed { get; set; }
    }

    /// <summary>
    /// Minimal roam-only movement helper. Picks random targets within a radius and walks toward them.
    /// </summary>
    public class BotMovement
    {
        private readonly BotMovementConfig _config;
        private readonly Random _random;
        private Vector3? _currentTarget;

        public BotMovement(BotMovementConfig config)
        {
            _config = config;
            _random = config.RandomSeed.HasValue ? new Random(config.RandomSeed.Value) : new Random();
        }

        public Vector3 GetNextPosition(Vector3 currentPos, float speedMetersPerSec, float deltaSeconds)
        {
            if (!_currentTarget.HasValue || Vector3.Distance(currentPos, _currentTarget.Value) < _config.ArrivalThresholdMeters)
            {
                _currentTarget = PickNewTarget();
            }

            var dir = _currentTarget.Value - currentPos;
            var dist = dir.Length();
            if (dist < float.Epsilon)
            {
                return currentPos;
            }

            dir /= dist; // normalize
            var step = Math.Min(dist, speedMetersPerSec * deltaSeconds);
            return currentPos + dir * step;
        }

        private Vector3 PickNewTarget()
        {
            // Uniformly sample direction and radius.
            var theta = _random.NextDouble() * Math.PI * 2;
            var radius = _random.NextDouble() * _config.RoamRadiusMeters;
            var offset = new Vector3(
                (float)(Math.Cos(theta) * radius),
                0f,
                (float)(Math.Sin(theta) * radius));
            return _config.RoamCenter + offset;
        }
    }
}
