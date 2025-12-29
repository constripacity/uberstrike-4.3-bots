using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        public void Upsert(int actorId, string name, byte team, bool alive)
        {
            var state = _players.GetOrAdd(actorId, _ => new PlayerState(actorId, name, team, alive));
            state.Update(name, team, alive);
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

        public PlayerState? FindNearestEnemy(byte ourTeam, Vector3 currentPos, TimeSpan maxStale)
        {
            PlayerState? nearest = null;
            var bestDistSq = float.MaxValue;
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

                var distSq = Vector3.DistanceSquared(player.Position, currentPos);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    nearest = player;
                }
            }

            return nearest;
        }
    }
}
