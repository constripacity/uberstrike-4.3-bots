using System;
using System.Numerics;
using BotRunner.Bot.AI;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot.Behaviors
{
    public class CoverBehavior : IBotBehavior
    {
        private readonly float _safeDistance;
        
        public CoverBehavior(float safeDistance = 15f)
        {
            _safeDistance = safeDistance;
        }

        public MovementIntent GetIntent(BotBehaviorContext context)
        {
            if (context.NearestEnemy == null)
                return MovementIntent.None;

            // Find nearest cover position (simplified: move to a position away from enemy or behind a virtual pillar)
            var coverPos = FindCoverPosition(context.CurrentPosition, context.NearestEnemy.Position);
            
            // Peek-shoot pattern: move to cover, pause, peek out, shoot, return
            var cycleTicks = 200;
            var currentTime = SimulationTime.Instance.CurrentTick % cycleTicks; 
            
            if (currentTime < 100)
                return new MovementIntent(coverPos); // Move to cover
            else if (currentTime < 130)
                return MovementIntent.None; // Wait in cover
            else if (currentTime < 170)
                // Peek out slightly to the side
                return new MovementIntent(coverPos + Vector3.UnitX * 3f); 
            else
                return new MovementIntent(coverPos); // Return to cover
        }

        private Vector3 FindCoverPosition(Vector3 currentPos, Vector3 enemyPos)
        {
            var awayFromEnemy = Vector3.Normalize(currentPos - enemyPos);
            // In a real game we'd search for static geometry. 
            // Here we simulate cover as a point 10m away from current position, further from enemy.
            return currentPos + awayFromEnemy * 10f;
        }
    }

    public class UtilityCoverBehavior : IUtilityBehavior
    {
        private readonly CoverBehavior _cover;
        
        public UtilityCoverBehavior(CoverBehavior cover)
        {
            _cover = cover;
        }

        public string Name => "Cover";

        public float Score(BehaviorContext ctx)
        {
            if (ctx.NearestEnemy == null)
                return -1f;

            float score = 0.1f;

            if (ctx.IsLowHealth)
                score += 0.7f; // High priority when hurt

            if (ctx.DistanceToEnemy < 10f)
                score += 0.3f; // Need cover in close combat

            if (ctx.IsOutnumbered)
                score += 0.2f;

            return Math.Clamp(score, 0f, 1f);
        }

        public MovementIntent GetIntent(BehaviorContext ctx)
        {
            return _cover.GetIntent(new BotBehaviorContext(ctx.CurrentPosition, ctx.Self, ctx.NearestEnemy, ctx.NowUtc));
        }
    }
}
