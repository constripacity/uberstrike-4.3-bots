using System;
using System.Numerics;
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
        
        public CombatSimulator Simulator => _combatSimulator;

        public CombatIntentGenerator(int? seed = null, RunMetrics? metrics = null)
        {
            _projectileSpeed = 100f; // Example value
            _aimPredictor = new AimPredictor(projectileSpeed: _projectileSpeed); 
            _combatSimulator = new CombatSimulator(seed, metrics);
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }
        
        public CombatIntent Generate(BehaviorContext context, PlayerState? target)
        {
            var combatContext = _combatSimulator.GetCombatContext();
            
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
            
            if (shouldShoot)
            {
                leadPredictionUsed = _projectileSpeed > 0.01f && target.Velocity.Length() >= 0.1f;
                aimPoint = _aimPredictor.CalculateAimPoint(
                    target.Position,
                    target.Velocity,
                    context.CurrentPosition,
                    weaponSpread: 0.1f,
                    seed: (int)SimulationTime.Instance.CurrentTick
                );
                
                accuracy = _aimPredictor.CalculateHitProbability(
                    target.Velocity,
                    distance
                );
            }
            
            return new CombatIntent
            {
                ShouldShoot = shouldShoot,
                AimPoint = aimPoint,
                Accuracy = accuracy,
                ShouldReload = shouldReload,
                DesiredWeaponId = weaponId,
                LeadPredictionUsed = leadPredictionUsed,
                Reason = BuildReasonString(combatContext, target, distance, accuracy)
            };
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
            if (distance < 15f) return 1; // Close range
            return 2; // Long range
        }

        private string BuildReasonString(CombatContext combatContext, PlayerState target, float distance, float accuracy)
        {
            return $"hp:{combatContext.HealthRatio:F1}, ammo:{combatContext.AmmoRatio:F1}, dist:{distance:F1}m, acc:{accuracy:F2}";
        }
    }
}