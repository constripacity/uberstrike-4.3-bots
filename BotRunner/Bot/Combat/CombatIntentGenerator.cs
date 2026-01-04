using System;
using System.Numerics;

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

        public CombatIntentDecision Generate(Vector3 currentPos, Vector3 enemyPos, float distance, TimeSpan reactionLatency, Vector3 enemyVelocity, DateTime nowUtc)
        {
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
            return BuildDecision(enemyPos, enemyVelocity, reactionLatency, distance, false, true, "reloading", remaining);
        }

        if (_currentAmmo <= 0)
        {
            StartReload(nowUtc);
            return BuildDecision(enemyPos, enemyVelocity, reactionLatency, distance, false, true, "clip_empty", _reloadDuration);
        }

            var hasLineOfSight = _los.HasLineOfSight(currentPos, enemyPos, facing);
            var rangeDecision = _rangeEvaluator.Evaluate(distance);
            var shouldReload = false;
            var cooldownReady = nowUtc - _lastShotUtc >= _fireCooldown;
            var shouldShoot = hasLineOfSight && rangeDecision.ShouldShoot && cooldownReady && !shouldReload;
            var aim = AimWithPrediction(enemyPos, enemyVelocity, reactionLatency);
            aim = _los.ApplyAimJitter(aim, distance, _random);
            var confidence = rangeDecision.Confidence * (hasLineOfSight ? 1f : 0.5f);

            var reason = "idle";
            if (!hasLineOfSight)
            {
                reason = "no_los";
            }
            else if (!rangeDecision.ShouldShoot)
            {
                reason = "out_of_range";
            }
            else if (!cooldownReady)
            {
                reason = "cooldown";
            }
            else if (shouldShoot)
            {
                reason = "fire";
                _currentAmmo = Math.Max(0, _currentAmmo - 1);
                _lastShotUtc = nowUtc;
                if (_currentAmmo == 0)
                {
                    StartReload(nowUtc);
                    shouldReload = true;
                }
            }

            var intent = new CombatIntent(shouldShoot, aim, confidence, shouldReload, rangeDecision.DesiredWeaponId);
            return new CombatIntentDecision(intent, hasLineOfSight, _rangeEvaluator.IsOptimal(distance), reason);
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

        private CombatIntentDecision BuildDecision(
            Vector3 enemyPos,
            Vector3 enemyVelocity,
            TimeSpan reactionLatency,
            float distance,
            bool hasLineOfSight,
            bool shouldReload,
            string reason,
            TimeSpan remainingReload)
        {
            var aim = AimWithPrediction(enemyPos, enemyVelocity, reactionLatency);
            aim = _los.ApplyAimJitter(aim, distance, _random);
            var intent = new CombatIntent(false, aim, 0.35f, shouldReload, desiredWeaponId: 0);
            return new CombatIntentDecision(intent, hasLineOfSight, _rangeEvaluator.IsOptimal(distance), reason + (shouldReload ? $" ({remainingReload.TotalMilliseconds:0}ms)" : string.Empty));
        }
    }

    public readonly struct CombatIntentDecision
    {
        public CombatIntentDecision(CombatIntent intent, bool hasLineOfSight, bool inOptimalRange, string reason)
        {
            Intent = intent;
            HasLineOfSight = hasLineOfSight;
            InOptimalRange = inOptimalRange;
            Reason = reason;
        }

        public CombatIntent Intent { get; }
        public bool HasLineOfSight { get; }
        public bool InOptimalRange { get; }
        public string Reason { get; }
    }
}
