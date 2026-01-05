using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot.AI
{
    /// <summary>
    /// Simple target hysteresis manager.
    /// Records target lock durations and exposes selection logic with switch penalties.
    /// </summary>
    public class TargetHysteresis
    {
        private readonly RunMetrics? _metrics;
        private int _currentTargetId = -1;
        private DateTime _targetLockTime = DateTime.MinValue;
        private readonly TimeSpan _minLockDuration = TimeSpan.FromSeconds(1.5);
        private readonly TimeSpan _maxLockDuration = TimeSpan.FromSeconds(5.0);
        private readonly float _switchPenalty = 0.3f;
        
        private readonly Dictionary<int, TargetMemory> _targetMemories = new();
        
        public int CurrentTargetId => _currentTargetId;
        public TimeSpan CurrentLockDuration => DateTime.UtcNow - _targetLockTime;
        
        public TargetHysteresis(RunMetrics? metrics = null)
        {
            _metrics = metrics;
        }
        
        public int SelectTarget(List<PlayerState> visibleEnemies, Vector3 myPosition)
        {
            if (visibleEnemies == null || visibleEnemies.Count == 0)
            {
                if (_currentTargetId != -1)
                {
                    var durationMs = (DateTime.UtcNow - _targetLockTime).TotalMilliseconds;
                    _metrics?.RecordTargetLock(durationMs);
                    Logger.Debug($"[TargetHysteresis] No visible enemies -> clearing target {_currentTargetId} (lockMs={durationMs:0.0})");
                }
                _currentTargetId = -1;
                return -1;
            }
            
            // Score each potential target
            var scoredTargets = visibleEnemies
                .Select(e => new {
                    Enemy = e,
                    Score = CalculateTargetScore(e, myPosition)
                })
                .OrderByDescending(x => x.Score)
                .ToList();
            
            var bestTarget = scoredTargets.First();
            
            // Apply hysteresis: penalty for switching away from current target
            if (_currentTargetId != -1 && bestTarget.Enemy.ActorId != _currentTargetId)
            {
                // Check if we should force-switch (current target dead or lost)
                var currentTarget = visibleEnemies.FirstOrDefault(e => e.ActorId == _currentTargetId);
                var shouldForceSwitch = currentTarget == null || 
                                       CurrentLockDuration > _maxLockDuration;
                
                if (!shouldForceSwitch)
                {
                    // Apply switch penalty - new target must be significantly better
                    var currentScore = scoredTargets
                        .FirstOrDefault(x => x.Enemy.ActorId == _currentTargetId)?.Score ?? 0f;
                    
                    if (bestTarget.Score < currentScore + _switchPenalty)
                    {
                        // Stick with current target
                        Logger.Debug($"[TargetHysteresis] Sticking with current target {_currentTargetId} (currentScore={currentScore:0.00}, bestScore={bestTarget.Score:0.00})");
                        return _currentTargetId;
                    }
                }
            }
            
            // Switch to new target (or maintain current)
            if (_currentTargetId != bestTarget.Enemy.ActorId)
            {
                var previous = _currentTargetId;
                // record previous lock duration if any
                if (previous != -1)
                {
                    var durationMs = (DateTime.UtcNow - _targetLockTime).TotalMilliseconds;
                    _metrics?.RecordTargetLock(durationMs);
                    _metrics?.RecordTargetSwitch();
                    Logger.Debug($"[TargetHysteresis] Target switch: {previous} -> {bestTarget.Enemy.ActorId} (prevLockMs={durationMs:0.0})");
                }

                _currentTargetId = bestTarget.Enemy.ActorId;
                _targetLockTime = DateTime.UtcNow;
                
                Logger.Debug($"[TargetHysteresis] Switching target {previous} -> {_currentTargetId} (score={bestTarget.Score:0.00})");
                
                // Record target engagement
                if (!_targetMemories.ContainsKey(_currentTargetId))
                    _targetMemories[_currentTargetId] = new TargetMemory();
                
                _targetMemories[_currentTargetId].EngagementCount++;
                _targetMemories[_currentTargetId].LastEngaged = DateTime.UtcNow;
            }
            
            Logger.Debug($"[TargetHysteresis] CurrentTarget={_currentTargetId} lockDuration={CurrentLockDuration.TotalSeconds:0.00}s");
            return _currentTargetId;
        }
        
        private float CalculateTargetScore(PlayerState enemy, Vector3 myPosition)
        {
            float score = 0f;
            
            // Distance factor (closer = higher priority)
            var distance = Vector3.Distance(myPosition, enemy.Position);
            var distanceScore = Math.Clamp(1f - (distance / 40f), 0f, 1f);
            score += distanceScore * 0.6f;
            
            // Freshness factor (recently seen = higher priority)
            var ageSeconds = (float)(DateTime.UtcNow - enemy.LastSeenUtc).TotalSeconds;
            var freshness = Math.Clamp(1f - (ageSeconds / 5f), 0f, 1f);
            score += freshness * 0.3f;
            
            // Recent damage memory (small bonus)
            if (_targetMemories.TryGetValue(enemy.ActorId, out var memory))
            {
                var damageScore = Math.Min(memory.DamageDealtToMe / 50f, 0.1f);
                score += damageScore;
            }
            
            return score;
        }
        
        public void RecordDamage(int fromActorId, float damage)
        {
            if (!_targetMemories.ContainsKey(fromActorId))
                _targetMemories[fromActorId] = new TargetMemory();
            
            _targetMemories[fromActorId].DamageDealtToMe += damage;
        }
        
        public void Reset()
        {
            _currentTargetId = -1;
            _targetLockTime = DateTime.MinValue;
        }
        
        private class TargetMemory
        {
            public int EngagementCount { get; set; }
            public float DamageDealtToMe { get; set; }
            public DateTime LastEngaged { get; set; }
        }
    }
}
