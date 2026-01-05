using System;
using System.Collections.Generic;
using System.Linq;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Bot.Combat;

namespace BotRunner.Bot.Combat
{
    /// <summary>
    /// Simulates combat outcomes deterministically.
    /// Tracks virtual health, ammo, and damage without sending RPCs.
    /// </summary>
    public class CombatSimulator
    {
        public class BotCombatState
        {
            public int Health { get; set; } = 100;
            public int MaxHealth { get; set; } = 100;
            public Dictionary<int, WeaponAmmo> Weapons { get; } = new();
            public int CurrentWeaponId { get; set; } = 1;
            public DateTime LastDamageTime { get; set; } = DateTime.MinValue;
            
            public float HealthRatio => Health / (float)MaxHealth;
            
            public WeaponAmmo? GetCurrentWeapon() => 
                Weapons.TryGetValue(CurrentWeaponId, out var weapon) ? weapon : null;
        }
        
        public class WeaponAmmo
        {
            public int WeaponId { get; set; }
            public int CurrentAmmo { get; set; }
            public int MaxAmmo { get; set; }
            public float ReloadTimeSeconds { get; set; }
            public DateTime ReloadCompleteTime { get; set; } = DateTime.MinValue;
            
            public float AmmoRatio => MaxAmmo > 0 ? CurrentAmmo / (float)MaxAmmo : 1f;
            public bool IsReloading => SimulationTime.Instance.Now < ReloadCompleteTime;
        }
        
        private readonly BotCombatState _botState = new();
        private readonly Dictionary<int, EnemyCombatState> _enemyStates = new();
        private readonly Random _random;
        private readonly RunMetrics? _metrics;
        
        public int CurrentWeaponId => _botState.CurrentWeaponId;

        public CombatSimulator(int? seed = null, RunMetrics? metrics = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
            _metrics = metrics;
            
            // Initialize default weapons
            InitializeDefaultWeapons();
        }
        
        private void InitializeDefaultWeapons()
        {
            _botState.Weapons[1] = new WeaponAmmo { WeaponId = 1, CurrentAmmo = 30, MaxAmmo = 30, ReloadTimeSeconds = 2.0f };
            _botState.Weapons[2] = new WeaponAmmo { WeaponId = 2, CurrentAmmo = 10, MaxAmmo = 10, ReloadTimeSeconds = 3.5f };
            _botState.CurrentWeaponId = 1;
        }
        
        /// <summary>
        /// Process a shoot intent and determine if it would hit.
        /// </summary>
        public ShotResult ProcessShootIntent(CombatIntent intent, PlayerState? target, System.Numerics.Vector3 shooterPos)
        {
            if (!intent.ShouldShoot || target == null)
                return ShotResult.Miss("no_target");
            
            var weapon = _botState.GetCurrentWeapon();
            if (weapon == null || weapon.CurrentAmmo <= 0)
                return ShotResult.Miss("no_ammo");
            
            if (weapon.IsReloading)
                return ShotResult.Miss("reloading");
            
            // Consume ammo
            weapon.CurrentAmmo--;
            
            // Determine hit based on intent accuracy and random factor
            var hitRoll = _random.NextDouble();
            var hitChance = intent.Accuracy;
            
            if (hitRoll <= hitChance)
            {
                // Calculate damage (deterministic based on seed)
                var damage = CalculateDamage(intent, weapon.WeaponId);
                
                // Record enemy damage
                if (!_enemyStates.ContainsKey(target.ActorId))
                    _enemyStates[target.ActorId] = new EnemyCombatState();
                
                _enemyStates[target.ActorId].DamageReceived += damage;
                _enemyStates[target.ActorId].LastHitTime = SimulationTime.Instance.Now;
                
                // Update metrics
                _metrics?.RecordHit(damage, intent.LeadPredictionUsed);
                
                return ShotResult.Hit(damage, target.ActorId);
            }
            else
            {
                _metrics?.RecordMiss();
                return ShotResult.Miss("accuracy_fail");
            }
        }
        
        /// <summary>
        /// Process incoming damage to bot (from scenario events).
        /// </summary>
        public void ReceiveDamage(int damage, int fromEnemyId)
        {
            _botState.Health = Math.Max(0, _botState.Health - damage);
            _botState.LastDamageTime = SimulationTime.Instance.Now;
            
            // Track damage source for threat assessment
            if (!_enemyStates.ContainsKey(fromEnemyId))
                _enemyStates[fromEnemyId] = new EnemyCombatState();
            
            _enemyStates[fromEnemyId].DamageDealt += damage;
            
            _metrics?.RecordDamageTaken(damage);
        }
        
        /// <summary>
        /// Get combat context for behavior decisions.
        /// </summary>
        public CombatContext GetCombatContext()
        {
            var weapon = _botState.GetCurrentWeapon();
            return new CombatContext
            {
                HealthRatio = _botState.HealthRatio,
                AmmoRatio = weapon?.AmmoRatio ?? 1f,
                IsReloading = weapon?.IsReloading ?? false,
                TimeSinceLastDamage = SimulationTime.Instance.Now - _botState.LastDamageTime,
                TotalDamageTaken = _enemyStates.Values.Sum(e => e.DamageDealt),
                MostDangerousEnemyId = GetMostDangerousEnemy()
            };
        }
        
        private int CalculateDamage(CombatIntent intent, int weaponId)
        {
            // Base damage + random variation (deterministic based on seed)
            var baseDamage = weaponId == 1 ? 20 : 50; // Weapon 1: AR, Weapon 2: Sniper
            var variation = (float)(_random.NextDouble() * 0.3 - 0.15); // ±15%
            
            return (int)(baseDamage * (1 + variation));
        }
        
        private int? GetMostDangerousEnemy()
        {
            if (!_enemyStates.Any()) return null;
            
            return _enemyStates
                .OrderByDescending(e => e.Value.DamageDealt)
                .First().Key;
        }
        
        public void ReloadWeapon()
        {
            var weapon = _botState.GetCurrentWeapon();
            if (weapon == null || weapon.CurrentAmmo == weapon.MaxAmmo || weapon.IsReloading) return;
            
            weapon.ReloadCompleteTime = SimulationTime.Instance.Now.AddSeconds(weapon.ReloadTimeSeconds);
        }

        /// <summary>
        /// Update simulation state (e.g. check if reload finished).
        /// </summary>
        public void Update()
        {
            foreach (var weapon in _botState.Weapons.Values)
            {
                if (weapon.ReloadCompleteTime != DateTime.MinValue && SimulationTime.Instance.Now >= weapon.ReloadCompleteTime)
                {
                    weapon.CurrentAmmo = weapon.MaxAmmo;
                    weapon.ReloadCompleteTime = DateTime.MinValue;
                }
            }
        }
        
        public void SwitchWeapon(int weaponId)
        {
            if (_botState.Weapons.ContainsKey(weaponId))
                _botState.CurrentWeaponId = weaponId;
        }
        
        public void Reset()
        {
            _botState.Health = _botState.MaxHealth;
            foreach (var weapon in _botState.Weapons.Values)
                weapon.CurrentAmmo = weapon.MaxAmmo;
            
            _enemyStates.Clear();
        }

        public BotCombatState GetBotState() => _botState;
    }
    
    public class ShotResult
    {
        public bool IsHit { get; }
        public int Damage { get; }
        public int TargetId { get; }
        public string Reason { get; }
        
        private ShotResult(bool isHit, int damage, int targetId, string reason)
        {
            IsHit = isHit;
            Damage = damage;
            TargetId = targetId;
            Reason = reason;
        }
        
        public static ShotResult Hit(int damage, int targetId) => 
            new ShotResult(true, damage, targetId, "hit");
        
        public static ShotResult Miss(string reason) => 
            new ShotResult(false, 0, -1, reason);
    }
    
    public class CombatContext
    {
        public float HealthRatio { get; set; }
        public float AmmoRatio { get; set; }
        public bool IsReloading { get; set; }
        public TimeSpan TimeSinceLastDamage { get; set; }
        public int TotalDamageTaken { get; set; }
        public int? MostDangerousEnemyId { get; set; }
        
        public bool IsLowHealth => HealthRatio < 0.3f;
        public bool IsCriticalHealth => HealthRatio < 0.15f;
        public bool IsLowAmmo => AmmoRatio < 0.2f;
        public bool IsUnderFire => TimeSinceLastDamage.TotalSeconds < 3.0;
    }
    
    internal class EnemyCombatState
    {
        public int DamageDealt { get; set; }
        public int DamageReceived { get; set; }
        public DateTime LastHitTime { get; set; }
    }
}