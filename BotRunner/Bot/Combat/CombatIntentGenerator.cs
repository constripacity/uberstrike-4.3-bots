using System;
using System.Numerics;
using System.Linq;
using BotRunner.Bot.AI;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot.Combat
{
    public class CombatIntentGenerator
    {
        private readonly AimPredictor _aimPredictor;
        private readonly CombatSimulator _combatSimulator;
        private readonly Random _random;
        private readonly float _projectileSpeed;
        private readonly WorldState _worldState;
        private readonly RunMetrics? _metrics;
        
        public CombatSimulator Simulator => _combatSimulator;

        public CombatIntentGenerator(WorldState worldState, int? seed = null, RunMetrics? metrics = null)
        {
            _worldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            _projectileSpeed = 100f; // Example value
            _aimPredictor = new AimPredictor(projectileSpeed: _projectileSpeed);
            _combatSimulator = new CombatSimulator(seed, metrics);
            _random = new Random(seed ?? 1);
            _metrics = metrics;
        }
        
        public CombatIntent Generate(BehaviorContext context, PlayerState? target)
        {
            var combatContext = _combatSimulator.GetCombatContext();

            // Check focus fire opportunity (follow allies)
            if (context.Self != null)
            {
                var focusFireTargetId = _worldState.GetFocusFireTarget(context.Self.Team, context.Self.ActorId);
                if (focusFireTargetId.HasValue)
                {
                    if (focusFireTargetId.Value != target?.ActorId)
                    {
                        var focusTarget = _worldState.Get(focusFireTargetId.Value);
                        if (focusTarget != null && ShouldFocusFire(context, focusTarget, target))
                        {
                            Logger.Info($"[Combat] Focus fire opportunity on enemy {focusFireTargetId.Value} (Switching target for coordinated attack)");
                            target = focusTarget;
                        }
                    }
                    else
                    {
                        // Already targeting the focus fire target
                        Logger.Debug($"[Combat] Coordinated focus fire on enemy {target.ActorId}");
                    }
                }

                // Record focus-fire opportunity / execution
                _metrics?.RecordFocusFireOpportunity(focusFireTargetId.HasValue && focusFireTargetId.Value == target?.ActorId);
            }

            // Check if we should retreat instead of fighting
            if (ShouldRetreat(combatContext, context))
            {
                return new CombatIntent
                {
                    ShouldShoot = false,
                    ShouldReload = false,
                    DesiredWeaponId = -1,
                    Reason = $"retreat: health={combatContext.HealthRatio:F1}"
                };
            }

            if (target == null)
                return CombatIntent.None;

            // Avoid shooting through allies
            if (WouldHitAlly(context, target))
            {
                // Record friendly-fire avoidance
                _metrics?.RecordFriendlyFireAvoided();

                return new CombatIntent
                {
                    ShouldShoot = false,
                    ShouldReload = false,
                    DesiredWeaponId = -1,
                    Reason = "ally_in_line_of_fire"
                };
            }

            // Get weapon for current range
            var distance = Vector3.Distance(context.CurrentPosition, target.Position);
            var weaponId = SelectWeaponForRange(distance, combatContext);
            
            // Switch weapon if needed
            if (weaponId != -1 && weaponId != _combatSimulator.CurrentWeaponId)
            {
                _combatSimulator.SwitchWeapon(weaponId);
            }
            
            // Check reload
            var shouldReload = ShouldReload(combatContext, distance);
            if (shouldReload)
            {
                _combatSimulator.ReloadWeapon();
            }
            
            // Decide to shoot
            var shouldShoot = ShouldShootAtTarget(combatContext, target, distance);
            var aimPoint = Vector3.Zero;
            var accuracy = 0f;
            var leadPredictionUsed = false;
            
            var weaponProfile = WeaponSystem.GetProfile(_combatSimulator.CurrentWeaponId);

            if (shouldShoot && weaponProfile != null)
            {
                leadPredictionUsed = weaponProfile.ProjectileSpeed > 0.01f && target.Velocity.Length() >= 0.1f;
                aimPoint = _aimPredictor.CalculateAimPoint(
                    target.Position,
                    target.Velocity,
                    context.CurrentPosition,
                    weaponSpread: weaponProfile.Spread,
                    seed: (int)SimulationTime.Instance.CurrentTick
                );
                
                accuracy = _aimPredictor.CalculateHitProbability(
                    target.Velocity,
                    distance
                );
            }
            
            var intent = new CombatIntent
            {
                ShouldShoot = shouldShoot,
                AimPoint = aimPoint,
                Accuracy = accuracy,
                ShouldReload = shouldReload,
                DesiredWeaponId = weaponId,
                LeadPredictionUsed = leadPredictionUsed,
                Reason = BuildReasonString(combatContext, target, distance, accuracy)
            };

            // Record target engagement metrics
            if (intent.ShouldShoot && target != null)
            {
                _metrics?.RecordTargetEngagement(target.ActorId);
            }

            // Record nearest ally distance
            if (context.Self != null)
            {
                var nearestAlly = _worldState.GetAllies(context.Self.Team, context.Self.ActorId)
                    .OrderBy(a => Vector3.Distance(context.CurrentPosition, a.Position))
                    .FirstOrDefault();
                if (nearestAlly != null)
                {
                    var dist = Vector3.Distance(context.CurrentPosition, nearestAlly.Position);
                    _metrics?.RecordAllyDistance(dist);
                }
            }

            // Record weapon usage tick
            if (weaponProfile != null && target != null)
            {
                var isOptimal = Math.Abs(distance - weaponProfile.OptimalRange) < 5f; // Simplified optimal range check
                _metrics?.RecordWeaponUsage(_combatSimulator.CurrentWeaponId, weaponProfile.Name, false, isOptimal);
            }

            return intent;
        }

        private bool ShouldRetreat(CombatContext combatContext, BehaviorContext behaviorContext)
        {
            // Retreat if critically low health and enemies nearby
            if (combatContext.IsCriticalHealth && behaviorContext.NearbyEnemiesCount > 0)
                return true;
            
            // Retreat if outnumbered and low ammo
            if (combatContext.IsLowAmmo && behaviorContext.IsOutnumbered)
                return true;
            
            return false;
        }

        private bool ShouldShootAtTarget(CombatContext combatContext, PlayerState target, float distance)
        {
            if (combatContext.IsReloading || combatContext.AmmoRatio <= 0)
                return false;

            // Deterministic shooting logic
            var baseChance = 0.7f;
            
            // Adjust based on distance
            if (distance > 20f) baseChance *= 0.5f;
            if (distance < 5f) baseChance *= 1.2f;
            
            // Adjust based on target health
            var healthRatio = target.Health / (float)target.MaxHealth;
            if (healthRatio < 0.3f) baseChance *= 1.3f; // Finish low-health targets
            
            // Deterministic random check
            return _random.NextDouble() < baseChance;
        }

        private bool ShouldReload(CombatContext combatContext, float distanceToTarget)
        {
            if (combatContext.IsReloading || combatContext.AmmoRatio >= 1.0f)
                return false;

            // Reload if ammo is low
            if (combatContext.IsLowAmmo)
                return true;

            // Reload if no target is close and ammo is not full
            if (distanceToTarget > 30f && combatContext.AmmoRatio < 0.8f)
                return true;

            return false;
        }

        private int SelectWeaponForRange(float distance, CombatContext combatContext)
        {
            var bestWeaponId = -1;
            var bestScore = float.MinValue;

            foreach (var profile in WeaponSystem.GetAllProfiles())
            {
                // Basic scoring: proximity to optimal range
                var rangeScore = 1f - Math.Abs(distance - profile.OptimalRange) / Math.Max(1f, profile.MaxEffectiveRange);
                
                // Penalty for being out of effective range
                if (distance > profile.MaxEffectiveRange)
                    rangeScore -= 1.0f;

                if (rangeScore > bestScore)
                {
                    bestScore = rangeScore;
                    bestWeaponId = profile.Id;
                }
            }

            return bestWeaponId;
        }

        private string BuildReasonString(CombatContext combatContext, PlayerState target, float distance, float accuracy)
        {
            return $"hp:{combatContext.HealthRatio:F1}, ammo:{combatContext.AmmoRatio:F1}, dist:{distance:F1}m, acc:{accuracy:F2}";
        }

        private bool ShouldFocusFire(BehaviorContext context, PlayerState focusTarget, PlayerState? currentTarget)
        {
            var distanceToFocus = Vector3.Distance(context.CurrentPosition, focusTarget.Position);
            var distanceToCurrent = currentTarget != null ?
                Vector3.Distance(context.CurrentPosition, currentTarget.Position) : float.MaxValue;

            // EASIER to switch: 2.0x instead of 1.5x
            if (distanceToFocus < distanceToCurrent * 2.0f)
                return true;

            // ALWAYS switch if we don't have a current target
            if (currentTarget == null)
                return true;

            return false;
        }

        private bool WouldHitAlly(BehaviorContext context, PlayerState target)
        {
            if (context.Self == null) return false;

            var allies = _worldState.GetAllies(context.Self.Team, context.Self.ActorId);
            var shotDirection = Vector3.Normalize(target.Position - context.CurrentPosition);

            foreach (var ally in allies)
            {
                // don't consider the ally if it's the shooter or dead
                if (ally.ActorId == context.Self.ActorId || !ally.IsAlive)
                    continue;

                if (Vector3.Distance(context.CurrentPosition, ally.Position) < 5f)
                    continue; // Too close to be "in front"

                var toAlly = Vector3.Normalize(ally.Position - context.CurrentPosition);
                var dot = Vector3.Dot(shotDirection, toAlly);
                dot = Math.Max(-1f, Math.Min(1f, dot));
                var angleToAlly = Math.Acos(dot) * (180f / Math.PI);

                // If ally is within 10 degrees of shot line, risk of friendly fire
                if (angleToAlly < 10f)
                    return true;
            }

            return false;
        }
    }
}
