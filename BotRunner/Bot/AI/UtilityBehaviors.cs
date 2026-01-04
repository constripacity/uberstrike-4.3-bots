using System;
using BotRunner.Bot.Behaviors;

namespace BotRunner.Bot.AI
{
    public class UtilityWanderBehavior : IUtilityBehavior
    {
        private readonly WanderBehavior _wander;
        private readonly float _baseScore;

        public UtilityWanderBehavior(WanderBehavior wander, float baseScore = 0.1f)
        {
            _wander = wander;
            _baseScore = baseScore;
        }

        public string Name => "Wander";

        public float Score(BehaviorContext ctx)
        {
            return _baseScore;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _wander.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy));
        }
    }

    public class UtilityChaseBehavior : IUtilityBehavior
    {
        private readonly ChaseNearestEnemyBehavior _chase;
        private readonly float _preferredRange;

        public UtilityChaseBehavior(ChaseNearestEnemyBehavior chase, float preferredRange)
        {
            _chase = chase;
            _preferredRange = preferredRange;
        }

        public string Name => "Chase";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            return Math.Max(0f, _preferredRange - ctx.DistanceToEnemy);
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _chase.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy));
        }
    }

    public class UtilityDisengageBehavior : IUtilityBehavior
    {
        private readonly DisengageBehavior _disengage;
        private readonly float _panicDistance;

        public UtilityDisengageBehavior(DisengageBehavior disengage, float panicDistance)
        {
            _disengage = disengage;
            _panicDistance = panicDistance;
        }

        public string Name => "Disengage";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            return ctx.DistanceToEnemy < _panicDistance ? _panicDistance - ctx.DistanceToEnemy : 0f;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _disengage.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy));
        }
    }

    public class UtilityStrafeBehavior : IUtilityBehavior
    {
        private readonly StrafeBehavior _strafe;
        private readonly float _engageRadius;

        public UtilityStrafeBehavior(StrafeBehavior strafe, float engageRadius)
        {
            _strafe = strafe;
            _engageRadius = engageRadius;
        }

        public string Name => "Strafe";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
            {
                return -1f;
            }

            return ctx.DistanceToEnemy <= _engageRadius ? 0.5f : -0.5f;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _strafe.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy));
        }
    }

    public class UtilityHoldBehavior : IUtilityBehavior
    {
        private readonly HoldPositionBehavior _hold;
        private readonly float _preferredMin;
        private readonly float _preferredMax;

        public UtilityHoldBehavior(HoldPositionBehavior hold, float preferredMin, float preferredMax)
        {
            _hold = hold;
            _preferredMin = preferredMin;
            _preferredMax = preferredMax;
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
                return 0.55f;
            }

            return -0.1f;
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _hold.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy));
        }
    }
}
