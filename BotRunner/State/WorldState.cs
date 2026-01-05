using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace BotRunner.State
{
    /// <summary>
    /// Thread-safe registry of players keyed by actorId with helpers for stale filtering.
    /// </summary>
    public class WorldState
    {
        private readonly ConcurrentDictionary<int, PlayerState> _players = new();

        public PlayerState? Get(int actorId)
        {
            _players.TryGetValue(actorId, out var state);
            return state;
        }

        public void Upsert(int actorId, string name, byte team, bool alive, int health = 100, int maxHealth = 100)
        {
            var state = _players.GetOrAdd(actorId, _ => new PlayerState(actorId, name, team, alive, health, maxHealth));
            state.Update(name, team, alive, health, maxHealth);
        }

        public void UpdatePosition(int actorId, Vector3 position)
        {
            if (_players.TryGetValue(actorId, out var state))
            {
                state.UpdatePosition(position);
            }
        }

        public IEnumerable<PlayerState> GetEnemies(byte ourTeam, TimeSpan maxStale)
        {
            var now = DateTime.UtcNow;
            foreach (var kvp in _players)
            {
                var player = kvp.Value;
                if (player.Team == ourTeam || !player.IsAlive)
                {
                    continue;
                }

                if (now - player.LastSeenUtc > maxStale)
                {
                    continue;
                }

                yield return player;
            }
        }

        public PlayerState? FindNearestEnemy(byte ourTeam, Vector3 currentPos, TimeSpan maxStale, int selfActorId = -1)
        {
            PlayerState? nearest = null;
            var bestDistSq = float.MaxValue;
            var now = DateTime.UtcNow;

            foreach (var kvp in _players)
            {
                var player = kvp.Value;
                if (player.ActorId == selfActorId)
                {
                    continue;
                }
                if (player.Team == ourTeam || !player.IsAlive)
                {
                    continue;
                }

                if (now - player.LastSeenUtc > maxStale)
                {
                    continue;
                }

                var distSq = Vector3.DistanceSquared(player.Position, currentPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = player;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Get all visible enemies for a specific viewer (by actorId)
        /// </summary>
        public List<PlayerState> GetVisibleEnemiesFor(int viewerActorId, float maxDistance = 50f, float fovDegrees = 90f)
        {
            var viewer = Get(viewerActorId);
            if (viewer == null) return new List<PlayerState>();

            // Use a small stale window for "visible" evaluation
            var enemies = GetEnemies(viewer.Team, TimeSpan.FromSeconds(5)).ToList();
            return enemies
                .Where(e => IsInFieldOfView(viewer.Position, viewer.FacingDirection, e.Position, fovDegrees))
                .Where(e => Vector3.Distance(viewer.Position, e.Position) <= maxDistance)
                .ToList();
        }

        /// <summary>
        /// Get allies (including self optionally excluded)
        /// </summary>
        public List<PlayerState> GetAllies(byte teamId, int excludeActorId = -1)
        {
            return _players.Values
                .Where(p => p.IsAlive && p.Team == teamId && p.ActorId != excludeActorId)
                .ToList();
        }

        /// <summary>
        /// Get enemy most targeted by allies (focus fire detection)
        /// </summary>
        public int? GetFocusFireTarget(byte teamId, int selfActorId)
        {
            var allies = GetAllies(teamId, selfActorId);
            if (!allies.Any()) return null;

            // Count how many allies are targeting each enemy
            var targetCounts = new Dictionary<int, int>();
            foreach (var ally in allies)
            {
                if (ally.CurrentTargetId.HasValue)
                {
                    var enemy = Get(ally.CurrentTargetId.Value);
                    if (enemy != null && enemy.Team != teamId)
                    {
                        targetCounts.TryGetValue(ally.CurrentTargetId.Value, out var count);
                        targetCounts[ally.CurrentTargetId.Value] = count + 1;
                    }
                }
            }

            if (!targetCounts.Any()) return null;
            return targetCounts.OrderByDescending(kv => kv.Value).First().Key;
        }

        private bool IsInFieldOfView(Vector3 viewerPos, Vector3 viewerFacing, Vector3 targetPos, float fovDegrees)
        {
            var toTarget = Vector3.Normalize(targetPos - viewerPos);
            var dot = Vector3.Dot(viewerFacing, toTarget);
            // clamp dot to valid acos domain (manual clamp to avoid MathF.Clamp compatibility issues)
            if (dot < -1f) dot = -1f;
            else if (dot > 1f) dot = 1f;
            var angle = Math.Acos(dot) * (180f / Math.PI);
            return angle <= fovDegrees / 2f;
        }
    }
}
