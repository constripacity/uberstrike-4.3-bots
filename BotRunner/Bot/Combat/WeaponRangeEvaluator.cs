using System;

namespace BotRunner.Bot.Combat
{
    public class WeaponRangeEvaluator
    {
        private readonly float _close;
        private readonly float _mid;
        private readonly float _far;

        public WeaponRangeEvaluator(float closeRange, float midRange, float farRange)
        {
            _close = Math.Max(1f, closeRange);
            _mid = Math.Max(_close, midRange);
            _far = Math.Max(_mid, farRange);
        }

        public RangeDecision Evaluate(float distance)
        {
            if (distance <= _close)
            {
                return new RangeDecision(true, 0.85f, 1, true);
            }

            if (distance <= _mid)
            {
                return new RangeDecision(true, 0.7f, 2, true);
            }

            if (distance <= _far)
            {
                return new RangeDecision(true, 0.5f, 3, false);
            }

            return new RangeDecision(false, 0.15f, 3, false);
        }

        public bool IsOptimal(float distance)
        {
            return distance <= _mid;
        }
    }

    public readonly struct RangeDecision
    {
        public RangeDecision(bool shouldShoot, float confidence, int desiredWeaponId, bool highReward)
        {
            ShouldShoot = shouldShoot;
            Confidence = confidence;
            DesiredWeaponId = desiredWeaponId;
            HighReward = highReward;
        }

        public bool ShouldShoot { get; }
        public float Confidence { get; }
        public int DesiredWeaponId { get; }
        public bool HighReward { get; }
    }
}
