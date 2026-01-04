using System;
using System.Numerics;

namespace BotRunner.Bot.Combat
{
    /// <summary>
    /// Placeholder line-of-sight evaluator that approximates vision using distance and angle checks.
    /// </summary>
    public class LineOfSightSimulator
    {
        private readonly float _maxDistance;
        private readonly float _cosThreshold;

        public LineOfSightSimulator(float maxDistance, float coneAngleDegrees)
        {
            _maxDistance = Math.Max(1f, maxDistance);
            _cosThreshold = (float)Math.Cos(coneAngleDegrees * Math.PI / 180f);
        }

        public bool HasLineOfSight(Vector3 origin, Vector3 target, Vector3 forward)
        {
            var toTarget = target - origin;
            if (toTarget.LengthSquared() < float.Epsilon)
            {
                return true;
            }

            if (toTarget.Length() > _maxDistance)
            {
                return false;
            }

            var dir = Vector3.Normalize(toTarget);
            var facing = Vector3.Normalize(forward);
            return Vector3.Dot(dir, facing) >= _cosThreshold;
        }

        public Vector3 ApplyAimJitter(Vector3 aimPoint, float distance, Random rng)
        {
            var scale = Math.Clamp(distance / _maxDistance, 0f, 1f);
            var jitter = new Vector3(
                (float)(rng.NextDouble() * 0.4 - 0.2) * scale,
                (float)(rng.NextDouble() * 0.15 - 0.075) * scale,
                (float)(rng.NextDouble() * 0.4 - 0.2) * scale);
            return aimPoint + jitter;
        }
    }
}
