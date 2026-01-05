using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Bot.Combat;
using BotRunner.Bot.Behaviors;
using BotRunner.Utils;
using BotRunner.Bot.AI;
using BotRunner.Bot;

namespace BotRunner.Bot.AI
{
    /// <summary>
    /// Unifies movement and combat decisions into coherent frames
    /// Prevents conflicts like "move left while shooting right"
    /// Enforces human-like decision timing (150-300ms frames)
    /// Adds a shoot-window hysteresis so "stop_and_shoot" can be held for a short duration.
    /// </summary>
    public class ActionPipeline
    {
        private readonly TimeSpan _minDecisionInterval = TimeSpan.FromMilliseconds(150);
        private readonly TimeSpan _maxDecisionInterval = TimeSpan.FromMilliseconds(300);
        
        // Use sim time from BehaviorContext.NowUtc for determinism
        private DateTime _lastDecisionSimTime = DateTime.MinValue;
        private ActionFrame? _lastFrame = null;
        private int _frameCount = 0;
        private readonly Random _random;
        private readonly RunMetrics? _metrics;
        private readonly ActionPipelineSettings _settings;
        
        // Decision history for consistency checking
        private readonly Queue<ActionFrame> _recentFrames = new(10);
        
        // Metrics for decision timing (sim-time based)
        private double _accumulatedDecisionIntervalMs = 0;
        private DateTime? _firstDecisionSimTime = null;

        // Action commit/hysteresis
        private DateTime _actionCommitUntilUtc = DateTime.MinValue;
        private string? _committedAction = null;
        
        public ActionPipeline(RunMetrics? metrics = null, ActionPipelineSettings? settings = null, int rngSeed = 0)
        {
            _metrics = metrics;
            _settings = settings ?? new ActionPipelineSettings();
            _random = new Random(rngSeed);
        }
        
        /// <summary>
        /// Generate a coherent action frame. Call this once per bot logic tick.
        /// Will rate-limit to enforce human-like decision timing.
        /// </summary>
        public ActionFrame? GenerateFrame(
            BehaviorContext behaviorContext,
            MovementIntent proposedMovement,
            CombatIntent proposedCombat)
        {
            // Use sim time provided by behaviorContext for determinism
            var now = behaviorContext.NowUtc;
            
            // 1. Rate limiting: enforce minimum time between decisions
            var timeSinceLast = now - _lastDecisionSimTime;
            if (timeSinceLast < _minDecisionInterval)
            {
                // Too soon - return last frame to maintain consistency
                return _lastFrame;
            }
            
            // 2. Decide if it's time for a new frame (randomized within window)
            var shouldDecide = timeSinceLast >= _maxDecisionInterval ||
                              (timeSinceLast >= _minDecisionInterval && 
                               _random.NextDouble() < 0.3); // 30% chance to decide early
            
            if (!shouldDecide && _lastFrame != null)
            {
                return _lastFrame;
            }
            
            // 3. Resolve potential conflicts between movement and combat
            var (finalMovement, finalCombat, primaryDecision, reason) = 
                ResolveConflicts(behaviorContext, proposedMovement, proposedCombat);
            
            // 4. Calculate decision confidence
            var confidence = CalculateConfidence(behaviorContext, finalMovement, finalCombat);

            // 4.5: Shoot-window hysteresis logic
            // If combat proposed shooting, but pipeline did not select attack, evaluate why and possibly override.
            if (proposedCombat?.ShouldShoot ?? false)
            {
                var blockedReason = "";
                var chosen = false;

                // Evaluate base reasons from CombatIntent.Reason (e.g., no_los, out_of_range, cooldown)
                var intentReason = proposedCombat.Reason ?? "";
                if (intentReason.Equals("no_los", StringComparison.OrdinalIgnoreCase))
                    blockedReason = "blocked_by_los";
                else if (intentReason.Equals("out_of_range", StringComparison.OrdinalIgnoreCase))
                    blockedReason = "too_far";
                else if (intentReason.Equals("cooldown", StringComparison.OrdinalIgnoreCase))
                    blockedReason = "cooldown";
                else
                    blockedReason = "";

                // Use combat-specific confidence for shoot-window gating (preferred over pipeline-level confidence)
                var combatConfidence = proposedCombat?.Confidence ?? proposedCombat!.Accuracy;
                // Check confidence + distance thresholds from settings
                if (combatConfidence >= _settings.ShootConfidenceThreshold && behaviorContext.DistanceToEnemy <= _settings.ShootDistanceMax)
                {
                    // Respect existing commit hold: if another action is committed and still active, block
                    if (!string.IsNullOrEmpty(_committedAction) && _actionCommitUntilUtc > now && _committedAction != "stop_and_shoot")
                    {
                        blockedReason = "action_commit_hold_active";
                    }
                    else
                    {
                        // Override to stop_and_shoot and commit for configured duration
                        finalMovement = MovementIntent.None;
                        primaryDecision = "stop_and_shoot";
                        reason = "shoot_window";
                        _committedAction = "stop_and_shoot";
                        _actionCommitUntilUtc = now + TimeSpan.FromMilliseconds(_settings.MinShootCommitMs);
                        chosen = true;
                        Logger.Debug($"[ActionPipeline] Shoot-window opened (combatConf={combatConfidence:0.00}, pipeConf={confidence:0.00}, dist={behaviorContext.DistanceToEnemy:0.00}) -> committing stop_and_shoot for {_settings.MinShootCommitMs}ms");
                    }
                }
                else
                {
                    // Not chosen because of combat confidence/distance
                    if (string.IsNullOrEmpty(blockedReason))
                    {
                        if (combatConfidence < _settings.ShootConfidenceThreshold) blockedReason = "combat_confidence_below_threshold";
                        else if (behaviorContext.DistanceToEnemy > _settings.ShootDistanceMax) blockedReason = "too_far";
                        else blockedReason = "unknown";
                    }
                }

                // Record opportunity metrics and log debug if blocked
                if (!chosen)
                {
                    _metrics?.RecordShootOpportunity(blockedReason, false);
                    Logger.Debug($"[ActionPipeline] Shoot blocked: {blockedReason} (intentReason={intentReason}, conf={confidence:0.00}, dist={behaviorContext.DistanceToEnemy:0.00})");
                }
                else
                {
                    _metrics?.RecordShootOpportunity("", true);
                }
            }

            // Enforce holding committed action until timeout
            if (!string.IsNullOrEmpty(_committedAction) && _actionCommitUntilUtc > now)
            {
                // keep primaryDecision as committed action (stop_and_shoot) if active
                if (_committedAction == "stop_and_shoot")
                {
                    primaryDecision = "stop_and_shoot";
                }
            }
            else
            {
                // Clear expired commit
                if (!string.IsNullOrEmpty(_committedAction))
                {
                    _committedAction = null;
                }
            }
            
            // 5. Create the new frame (use sim-time for determinism)
            var frame = new ActionFrame(
                behaviorContext.NowUtc,
                finalMovement,
                finalCombat,
                behaviorContext,
                primaryDecision,
                reason,
                confidence);
            
            // 6. Update state
            _lastFrame = frame ?? throw new InvalidOperationException("Generated null frame");

            // 7. Track for consistency analysis
            _recentFrames.Enqueue(_lastFrame);
            if (_recentFrames.Count > 10)
                _recentFrames.Dequeue();

            // 8. Update timing metrics (based on sim time)
            if (_firstDecisionSimTime == null)
                _firstDecisionSimTime = now;
            if (_lastDecisionSimTime != DateTime.MinValue)
            {
                var interval = (now - _lastDecisionSimTime).TotalMilliseconds;
                _accumulatedDecisionIntervalMs += interval;
            }
            _metrics?.RecordActionFrame(_lastFrame);
            _frameCount++;
            _lastDecisionSimTime = now;
            Logger.Debug($"[ActionPipeline] Frame #{_frameCount}: {_lastFrame}");

            return _lastFrame;
        }
        
        private (MovementIntent, CombatIntent, string, string) ResolveConflicts(
            BehaviorContext context,
            MovementIntent movement,
            CombatIntent combat)
        {
            // Default: use as-is
            string primaryDecision = "maintain";
            string reason = "no_conflict";
            
            // CONFLICT 1: Moving away while trying to shoot
            if (combat.ShouldShoot && movement.HasTarget)
            {
                var moveDirection = Vector3.Normalize(movement.TargetPosition - context.CurrentPosition);
                var aimDirection = Vector3.Normalize(combat.AimPoint - context.CurrentPosition);
                
                var dot = Vector3.Dot(moveDirection, aimDirection);
                
                if (dot < -0.7f) // Moving opposite of aim direction
                {
                    // PRIORITY: Shooting over moving
                    movement = MovementIntent.None;
                    primaryDecision = "stop_and_shoot";
                    reason = "movement_conflicted_with_aim";
                }
                else if (dot < 0.3f) // Moving perpendicular to aim
                {
                    // This is fine - strafing while shooting
                    primaryDecision = "strafe_shoot";
                    reason = "strafing_while_aiming";
                }
            }
            
            // CONFLICT 2: Reloading while moving into danger
            if (combat.ShouldReload && (context.EnemyCount ?? 0) > 0)
            {
                // Find cover or stop moving
                if (!movement.HasTarget)
                {
                    movement = MovementIntent.None;
                    primaryDecision = "reload_in_place";
                    reason = "reload_under_fire";
                }
                else
                {
                    primaryDecision = "reload_while_repositioning";
                    reason = "reload_with_movement";
                }
            }
            
            // Determine primary decision type if not set
            if (primaryDecision == "maintain")
            {
                if (combat.ShouldShoot) primaryDecision = "attack";
                else if (combat.ShouldReload) primaryDecision = "reload";
                else if (movement.HasTarget) primaryDecision = "reposition";
                else primaryDecision = "hold";
            }
            
            return (movement, combat, primaryDecision, reason);
        }
        
        private float CalculateConfidence(
            BehaviorContext context, 
            MovementIntent movement, 
            CombatIntent combat)
        {
            float confidence = 0.5f; // Base
            
            // Bonus for clear situations
            if (((context.HealthRatio ?? 1.0f) < 0.4f) && !movement.HasTarget)
                confidence += 0.2f; // Holding still when hurt is good
            
            if (combat.ShouldShoot && context.DistanceToEnemy < 10f)
                confidence += 0.15f; // Good shooting range
            
            if (!combat.ShouldShoot && context.DistanceToEnemy > 20f)
                confidence += 0.1f; // Not wasting ammo at long range
            
            // Penalty for contradictions
            if (combat.ShouldReload && (context.EnemyCount ?? 0) > 1)
                confidence -= 0.2f; // Dangerous reload timing
            
            // Bonus for consistency with recent frames
            confidence += CalculateConsistencyBonus();
            
            return Math.Clamp(confidence, 0.1f, 0.95f);
        }
        
        private float CalculateConsistencyBonus()
        {
            if (_recentFrames.Count < 3) return 0f;
            
            var frames = _recentFrames.ToArray();
            var lastDecision = frames[^1].PrimaryDecision;
            
            // Check if last 3 frames had same primary decision
            int sameCount = 0;
            for (int i = Math.Max(0, frames.Length - 4); i < frames.Length - 1; i++)
            {
                if (frames[i].PrimaryDecision == lastDecision)
                    sameCount++;
            }
            
            return sameCount >= 2 ? 0.1f : 0f; // Bonus for consistency
        }
        
        public void Reset()
        {
            _lastDecisionSimTime = DateTime.MinValue;
            _lastFrame = null;
            _frameCount = 0;
            _recentFrames.Clear();
            _committedAction = null;
            _actionCommitUntilUtc = DateTime.MinValue;
            _accumulatedDecisionIntervalMs = 0;
            _firstDecisionSimTime = null;
        }
        
        public ActionPipelineMetrics GetMetrics()
        {
            var avg = 0.0;
            if (_frameCount > 1 && _firstDecisionSimTime.HasValue)
            {
                var totalSimMs = (_lastDecisionSimTime - _firstDecisionSimTime.Value).TotalMilliseconds;
                avg = totalSimMs / _frameCount;
            }
            return new ActionPipelineMetrics
            {
                TotalFrames = _frameCount,
                AvgDecisionIntervalMs = avg,
                RecentConsistencyScore = CalculateRecentConsistency()
            };
        }
        
        private float CalculateRecentConsistency()
        {
            if (_recentFrames.Count < 2) return 1f;
            
            var frames = _recentFrames.ToArray();
            int changes = 0;
            
            for (int i = 1; i < frames.Length; i++)
            {
                if (frames[i].PrimaryDecision != frames[i-1].PrimaryDecision)
                    changes++;
            }
            
            return 1f - (changes / (float)(frames.Length - 1));
        }
    }
    
    public class ActionPipelineMetrics
    {
        public int TotalFrames { get; set; }
        public double AvgDecisionIntervalMs { get; set; }
        public float RecentConsistencyScore { get; set; }
    }
}
