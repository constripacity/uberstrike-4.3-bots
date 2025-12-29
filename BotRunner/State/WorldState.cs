using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace BotRunner.State
{
    /// <summary>
    /// Thread-safe snapshot of all known players. Updated through RPC handlers and consumed by the bot brain.
    /// </summary>
    public class WorldState
    {
        private readonly ConcurrentDictionary<int, PlayerState> _players = new();

        public IReadOnlyCollection<PlayerState> Players => _players.Values;

        public PlayerState? GetPlayer(int cmid)
        {
            _players.TryGetValue(cmid, out var state);
            return state;
        }

        public void UpsertPlayer(int cmid, string name, byte team, bool alive)
        {
            var player = _players.GetOrAdd(cmid, _ => new PlayerState(cmid, name, team, alive));
            player.Update(name, team, alive);
        }

        public void UpdatePosition(int cmid, System.Numerics.Vector3 position)
        {
            if (_players.TryGetValue(cmid, out var player))
            {
                player.UpdatePosition(position);
            }
        }

        public PlayerState? FindNearestEnemy(byte ourTeam, System.Numerics.Vector3 currentPosition)
        {
            return _players.Values
                .Where(p => p.Team != ourTeam && p.IsAlive)
                .OrderBy(p => System.Numerics.Vector3.DistanceSquared(p.Position, currentPosition))
                .FirstOrDefault();
        }
    }
}
