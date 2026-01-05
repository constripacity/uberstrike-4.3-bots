using System;
using BotRunner.Bot.Behaviors;

namespace BotRunner.Bot.AI
{
    public class UtilityOrbitStrafeBehavior : IUtilityBehavior
    {
        private readonly OrbitStrafeBehavior _orbit;
        private readonly float _minRange;
        private readonly float _maxRange;
        private readonly float _idealRange;
        private readonly float _stateBias;
        private const float StayBonus = 0.06f;

        public UtilityOrbitStrafeBehavior(
            OrbitStrafeBehavior orbit,
            float minRange,
            float maxRange,
            float idealRange,
            float stateBias = 0.05f)
        {
            _orbit = orbit;
            _minRange = minRange;
            _maxRange = Math.Max(minRange + 0.5f, maxRange);
            _idealRange = idealRange;
            _stateBias = stateBias;
        }

        public string Name => "OrbitStrafe";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            if (ctx.DistanceToEnemy < _minRange * 0.75f || ctx.DistanceToEnemy > _maxRange * 1.2f)
            {
                return -0.2f;
            }

            var closeness = 1f - Math.Abs(ctx.DistanceToEnemy - _idealRange) / (_maxRange - _minRange);
            closeness = Math.Clamp(closeness, 0f, 1f);
            var score = 0.7f + closeness * 0.2f;
            if (string.Equals(ctx.LastBehaviorName, Name, StringComparison.Ordinal))
            {
                score += StayBonus;
            }
            if (ctx.IsEngagingState)
            {
                score += _stateBias;
            }

            return score;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _orbit.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    public class UtilityWanderBehavior : IUtilityBehavior
    {
        private readonly WanderBehavior _wander;
        private readonly float _baseScore;
        private readonly float _stateBias;

        public UtilityWanderBehavior(WanderBehavior wander, float baseScore = 0.1f, float stateBias = 0.05f)
        {
            _wander = wander;
            _baseScore = baseScore;
            _stateBias = stateBias;
        }

        public string Name => "Wander";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy != null)
            {
                return -0.5f;
            }
            var score = _baseScore;
            if (!ctx.IsEngagingState)
            {
                score += _stateBias;
            }
            return score;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _wander.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    public class UtilityChaseBehavior : IUtilityBehavior
    {
        private readonly ChaseNearestEnemyBehavior _chase;
        private readonly float _preferredMax;
        private readonly float _engageDistance;
        private readonly float _stateBias;

        public UtilityChaseBehavior(ChaseNearestEnemyBehavior chase, float preferredMax, float engageDistance, float stateBias = 0.05f)
        {
            _chase = chase;
            _preferredMax = preferredMax;
            _engageDistance = engageDistance;
            _stateBias = stateBias;
        }

        public string Name => "Chase";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f + (ctx.IsEngagingState ? 0f : _stateBias);
            }

            if (ctx.DistanceToEnemy > _engageDistance)
            {
                return -0.25f;
            }

            var t = (ctx.DistanceToEnemy - _preferredMax) / Math.Max(0.001f, (_engageDistance - _preferredMax));
            t = Math.Clamp(t, 0f, 1f);
            var score = 0.35f + t * 0.45f;
            if (!ctx.IsEngagingState)
            {
                score += _stateBias;
            }
            return score;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _chase.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    public class UtilityDisengageBehavior : IUtilityBehavior
    {
        private readonly DisengageBehavior _disengage;
        private readonly float _panicDistance;
        private readonly float _stateBias;

        public UtilityDisengageBehavior(DisengageBehavior disengage, float panicDistance, float stateBias = 0.05f)
        {
            _disengage = disengage;
            _panicDistance = panicDistance;
            _stateBias = stateBias;
        }

        public string Name => "Disengage";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            if (ctx.DistanceToEnemy >= _panicDistance)
            {
                return -0.5f;
            }

            var t = 1f - (ctx.DistanceToEnemy / Math.Max(0.001f, _panicDistance));
            t = Math.Clamp(t, 0f, 1f);
            var score = 0.55f + t * 0.45f;
            if (ctx.IsEngagingState)
            {
                score += _stateBias;
            }
            return score;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _disengage.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    public class UtilityStrafeBehavior : IUtilityBehavior
    {
        private readonly StrafeBehavior _strafe;
        private readonly float _preferredMin;
        private readonly float _strafeMax;
        private readonly float _stateBias;
        private const float StayBonus = 0.05f;

        public UtilityStrafeBehavior(StrafeBehavior strafe, float preferredMin, float strafeMax, float stateBias = 0.05f)
        {
            _strafe = strafe;
            _preferredMin = preferredMin;
            _strafeMax = strafeMax;
            _stateBias = stateBias;
        }

        public string Name => "Strafe";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            if (ctx.DistanceToEnemy < _preferredMin || ctx.DistanceToEnemy > _strafeMax)
            {
                return -0.25f;
            }

            var center = (_preferredMin + _strafeMax) * 0.5f;
            var closeness = 1f - Math.Abs(ctx.DistanceToEnemy - center) / (_strafeMax - _preferredMin);
            var score = 0.45f + closeness * 0.25f;
            if (string.Equals(ctx.LastBehaviorName, Name, StringComparison.Ordinal))
            {
                score += StayBonus;
            }
            if (ctx.IsEngagingState)
            {
                score += _stateBias;
            }
            return score;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _strafe.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    public class UtilityHoldBehavior : IUtilityBehavior
    {
        private readonly HoldPositionBehavior _hold;
        private readonly float _preferredMin;
        private readonly float _preferredMax;
        private readonly float _stateBias;
        private const float StayBonus = 0.08f;

        public UtilityHoldBehavior(HoldPositionBehavior hold, float preferredMin, float preferredMax, float stateBias = 0.05f)
        {
            _hold = hold;
            _preferredMin = preferredMin;
            _preferredMax = preferredMax;
            _stateBias = stateBias;
        }

        public string Name => "Hold";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -0.25f;
            }

            if (ctx.DistanceToEnemy >= _preferredMin && ctx.DistanceToEnemy <= _preferredMax)
            {
                var stay = string.Equals(ctx.LastBehaviorName, Name, StringComparison.Ordinal) ? StayBonus : 0f;
                return 0.6f + stay + (ctx.IsEngagingState ? _stateBias : 0f);
            }

            return -0.1f;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _hold.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }

    // NEW: Utility wrapper for FlankBehavior
    public class UtilityFlankBehavior : IUtilityBehavior
    {
        private readonly FlankBehavior _flank;
        private readonly float _stateBias;
        private const float StayBonus = 0.04f;

        public UtilityFlankBehavior(FlankBehavior flank, float stateBias = 0.05f)
        {
            _flank = flank;
            _stateBias = stateBias;
        }

        public string Name => "Flank";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
                return -1f;

            float score = 0f;

            // MORE WEIGHT to having allies
            if (ctx.NearbyAlliesCount > 0) 
                score += 0.4f;

            // MORE WEIGHT to medium range
            if (ctx.DistanceToEnemy > 8f && ctx.DistanceToEnemy < 25f)
                score += 0.3f;
            else if (ctx.DistanceToEnemy < 3f || ctx.DistanceToEnemy > 35f)
                score -= 0.3f;
            else
                score += 0.1f;

            // LESS penalty for low health (was -0.3f? instructions say -0.1f)
            if (ctx.IsLowHealth)
                score -= 0.1f;
            else
                score += 0.2f;

            if (string.Equals(ctx.LastBehaviorName, Name, StringComparison.Ordinal))
                score += StayBonus;
            
            if (ctx.IsEngagingState)
                score += _stateBias;

            return Math.Clamp(score, 0f, 1f);
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _flank.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }
}
