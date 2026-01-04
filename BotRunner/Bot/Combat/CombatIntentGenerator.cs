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

        public CombatIntentGenerator(LineOfSightSimulator los, WeaponRangeEvaluator rangeEvaluator, int seed, float aimLeadSeconds)
        {
            _los = los;
            _rangeEvaluator = rangeEvaluator;
            _random = new Random(seed);
            _aimLeadSeconds = aimLeadSeconds;
        }

        public CombatIntentDecision Generate(Vector3 currentPos, Vector3 enemyPos, float distance, TimeSpan reactionLatency, Vector3 enemyVelocity)
        {
            var facing = Vector3.UnitX; // placeholder forward vector
            var hasLineOfSight = _los.HasLineOfSight(currentPos, enemyPos, facing);
            var rangeDecision = _rangeEvaluator.Evaluate(distance);
            var shouldReload = false;
            var shouldShoot = hasLineOfSight && rangeDecision.ShouldShoot && !shouldReload;
            var aim = AimWithPrediction(enemyPos, enemyVelocity, reactionLatency);
            aim = _los.ApplyAimJitter(aim, distance, _random);
            var confidence = rangeDecision.Confidence * (hasLineOfSight ? 1f : 0.5f);
            var intent = new CombatIntent(shouldShoot, aim, confidence, shouldReload, rangeDecision.DesiredWeaponId);
            return new CombatIntentDecision(intent, hasLineOfSight, _rangeEvaluator.IsOptimal(distance));
        }

        private Vector3 AimWithPrediction(Vector3 enemyPos, Vector3 enemyVelocity, TimeSpan reactionLatency)
        {
            var leadSeconds = _aimLeadSeconds + Math.Max(0f, (float)reactionLatency.TotalSeconds);
            return enemyPos + enemyVelocity * leadSeconds;
        }
    }

    public readonly struct CombatIntentDecision
    {
        public CombatIntentDecision(CombatIntent intent, bool hasLineOfSight, bool inOptimalRange)
        {
            Intent = intent;
            HasLineOfSight = hasLineOfSight;
            InOptimalRange = inOptimalRange;
        }

        public CombatIntent Intent { get; }
        public bool HasLineOfSight { get; }
        public bool InOptimalRange { get; }
    }
}
