using System;
using System.Numerics;
using BotRunner.State;

namespace BotRunner.Bot.AI
{
    public readonly struct BehaviorContext
    {
        public BehaviorContext(
            Vector3 currentPosition,
            PlayerState? self,
            PlayerState? nearestEnemy,
            float distanceToEnemy,
            TimeSpan timeInFsmState,
            string lastBehaviorName,
            DateTime nowUtc,
            bool isEngagingState,
            float? healthRatio = null,
            float? ammoRatio = null,
            int? enemyCount = null)
        {
            CurrentPosition = currentPosition;
            Self = self;
            NearestEnemy = nearestEnemy;
            DistanceToEnemy = distanceToEnemy;
            TimeInFsmState = timeInFsmState;
            LastBehaviorName = lastBehaviorName;
            NowUtc = nowUtc;
            IsEngagingState = isEngagingState;
            HealthRatio = healthRatio;
            AmmoRatio = ammoRatio;
            EnemyCount = enemyCount;
        }

        public Vector3 CurrentPosition { get; }
        public PlayerState? Self { get; }
        public PlayerState? NearestEnemy { get; }
        public float DistanceToEnemy { get; }
        public TimeSpan TimeInFsmState { get; }
        public string LastBehaviorName { get; }
        public DateTime NowUtc { get; }
        public bool IsEngagingState { get; }
        public float? HealthRatio { get; }
        public float? AmmoRatio { get; }
        public int? EnemyCount { get; }
    }
}
