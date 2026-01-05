using System;
using System.Numerics;
using BotRunner.Bot.AI;

namespace BotRunner.Bot.Combat
{
    public class CombatIntentGenerator
    {
        private readonly Random _random;
        private readonly LineOfSightSimulator _los;
        private readonly WeaponRangeEvaluator _rangeEvaluator;
        private readonly float _aimLeadSeconds;
        private readonly TimeSpan _fireCooldown;
        private readonly int _clipSize;
        private readonly TimeSpan _reloadDuration;
        private int _currentAmmo;
        private DateTime _lastShotUtc = DateTime.MinValue;
        private DateTime _reloadUntilUtc = DateTime.MinValue;

        public CombatIntentGenerator(
            LineOfSightSimulator los,
            WeaponRangeEvaluator rangeEvaluator,
            int seed,
            float aimLeadSeconds,
            int fireRateMs,
            int clipSize,
            float reloadSeconds)
        {
            _los = los;
            _rangeEvaluator = rangeEvaluator;
            _random = new Random(seed);
            _aimLeadSeconds = aimLeadSeconds;
            _fireCooldown = TimeSpan.FromMilliseconds(Math.Max(10, fireRateMs));
            _clipSize = Math.Max(1, clipSize);
            _reloadDuration = TimeSpan.FromSeconds(Math.Max(0.5, reloadSeconds));
            _currentAmmo = _clipSize;
        }

        public CombatIntent Generate(BehaviorContext context)
        {
            if (context.NearestEnemy == null)
                return CombatIntent.None;

            var enemyPos = context.NearestEnemy.Position;
            var enemyVelocity = Vector3.Zero;
            var distance = context.DistanceToEnemy;
            var nowUtc = context.NowUtc;
            var currentPos = context.CurrentPosition;

            var facing = Vector3.Normalize(enemyPos - currentPos);
            if (float.IsNaN(facing.X))
            {
                facing = Vector3.UnitX;
            }

            if (_reloadUntilUtc != DateTime.MinValue && nowUtc >= _reloadUntilUtc)
            {
                _reloadUntilUtc = DateTime.MinValue;
                _currentAmmo = _clipSize;
            }

            if (_reloadUntilUtc > nowUtc)
            {
                var remaining = _reloadUntilUtc - nowUtc;
                return BuildIntent(enemyPos, enemyVelocity, TimeSpan.Zero, distance, true, "reloading");
            }

            if (_currentAmmo <= 0)
            {
                StartReload(nowUtc);
                return BuildIntent(enemyPos, enemyVelocity, TimeSpan.Zero, distance, true, "clip_empty");
            }

            var hasLineOfSight = _los.HasLineOfSight(currentPos, enemyPos, facing);
            var rangeDecision = _rangeEvaluator.Evaluate(distance);
            var shouldReload = false;
            var cooldownReady = nowUtc - _lastShotUtc >= _fireCooldown;
            // Combat-specific confidence comes from range evaluator + LOS factor
            var combatConfidence = rangeDecision.Confidence * (hasLineOfSight ? 1f : 0.5f);
            var shouldShoot = hasLineOfSight && rangeDecision.ShouldShoot && cooldownReady && !shouldReload;
            var aim = AimWithPrediction(enemyPos, enemyVelocity, TimeSpan.Zero); // No reaction latency here
            aim = _los.ApplyAimJitter(aim, distance, _random);
            var accuracy = combatConfidence;

            var reason = "idle";
            if (!hasLineOfSight) reason = "no_los";
            else if (!rangeDecision.ShouldShoot) reason = "out_of_range";
            else if (!cooldownReady) reason = "cooldown";
            else if (shouldShoot)
            {
                reason = "fire";
                _currentAmmo = Math.Max(0, _currentAmmo - 1);
                _lastShotUtc = nowUtc;
                if (_currentAmmo == 0)
                {
                    // Start reload timer but do not mark ShouldReload for this same frame.
                    StartReload(nowUtc);
                }
            }

            return new CombatIntent
            {
                ShouldShoot = shouldShoot,
                AimPoint = aim,
                Accuracy = accuracy,
                Confidence = combatConfidence,
                ShouldReload = shouldReload,
                DesiredWeaponId = rangeDecision.DesiredWeaponId,
                Reason = reason
            };
        }

        private Vector3 AimWithPrediction(Vector3 enemyPos, Vector3 enemyVelocity, TimeSpan reactionLatency)
        {
            var leadSeconds = _aimLeadSeconds + Math.Max(0f, (float)reactionLatency.TotalSeconds);
            return enemyPos + enemyVelocity * leadSeconds;
        }

        private void StartReload(DateTime nowUtc)
        {
            _reloadUntilUtc = nowUtc + _reloadDuration;
            _currentAmmo = 0;
        }

        private CombatIntent BuildIntent(
            Vector3 enemyPos,
            Vector3 enemyVelocity,
            TimeSpan reactionLatency,
            float distance,
            bool shouldReload,
            string reason)
        {
            var aim = AimWithPrediction(enemyPos, enemyVelocity, reactionLatency);
            aim = _los.ApplyAimJitter(aim, distance, _random);
            return new CombatIntent
            {
                ShouldShoot = false,
                AimPoint = aim,
                Accuracy = 0.35f,
                Confidence = 0.2f,
                ShouldReload = shouldReload,
                DesiredWeaponId = 0,
                Reason = reason
            };
        }
    }
}
