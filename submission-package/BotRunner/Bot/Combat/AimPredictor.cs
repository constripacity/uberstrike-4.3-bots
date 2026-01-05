using System;
using System.Numerics;

namespace BotRunner.Bot.Combat
{
    /// <summary>
    /// Predicts where to aim based on target movement.
    /// Deterministic - uses only simulation time, no wall clock.
    /// </summary>
    public class AimPredictor
    {
        private readonly float _projectileSpeed; // 0 for hitscan
        
        public AimPredictor(float projectileSpeed = 0f)
        {
            _projectileSpeed = projectileSpeed;
        }
        
        public Vector3 CalculateAimPoint(
            Vector3 targetPosition,
            Vector3 targetVelocity,
            Vector3 shooterPosition,
            float weaponSpread = 0.05f,
            int? seed = null)
        {
            var random = new Random(seed ?? 1);
            
            if (_projectileSpeed <= 0.01f || targetVelocity.Length() < 0.1f)
            {
                // Hitscan or stationary target - aim directly
                return AddSpread(targetPosition, weaponSpread, random);
            }
            
            // Calculate time for projectile to reach target
            var distance = Vector3.Distance(shooterPosition, targetPosition);
            var timeToTarget = distance / _projectileSpeed;
            
            // Predict future position (simple linear prediction)
            var predictedPos = targetPosition + targetVelocity * timeToTarget;
            
            // Add weapon spread (deterministic based on seed)
            return AddSpread(predictedPos, weaponSpread, random);
        }
        
        private Vector3 AddSpread(Vector3 position, float spread, Random random)
        {
            if (spread <= 0f) return position;
            
            var spreadOffset = new Vector3(
                (float)(random.NextDouble() - 0.5) * spread,
                (float)(random.NextDouble() - 0.5) * spread,
                (float)(random.NextDouble() - 0.5) * spread
            );
            
            return position + spreadOffset;
        }
        
        /// <summary>
        /// Calculate hit probability (0-1) based on target movement and distance.
        /// Deterministic given same inputs.
        /// </summary>
        public float CalculateHitProbability(
            Vector3 targetVelocity,
            float distance,
            float baseAccuracy = 0.8f,
            float maxRange = 50f)
        {
            var speedFactor = Math.Clamp(targetVelocity.Length() / 10f, 0f, 1f);
            var rangeFactor = Math.Clamp(distance / maxRange, 0f, 1f);
            
            // Accuracy decreases with target speed and distance
            var accuracy = baseAccuracy * 
                         (1f - speedFactor * 0.4f) * 
                         (1f - rangeFactor * 0.3f);
            
            return Math.Clamp(accuracy, 0.1f, 0.95f);
        }
    }
}
