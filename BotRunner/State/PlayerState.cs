using System;
using System.Numerics;

namespace BotRunner.State
{
    /// <summary>
    /// Mutable snapshot of a player's state with internal locking to tolerate concurrent updates
    /// from networking and AI threads.
    /// </summary>
    public class PlayerState
    {
        private readonly object _lock = new();

        public int ActorId { get; }
        public string Name { get; private set; }
        public byte Team { get; private set; }
        public bool IsAlive { get; private set; }
        public Vector3 Position { get; private set; }
        public DateTime LastSeenUtc { get; private set; }
        public DateTime LastPositionUtc { get; private set; }

        public PlayerState(int actorId, string name, byte team, bool alive)
        {
            ActorId = actorId;
            Name = name;
            Team = team;
            IsAlive = alive;
            Position = Vector3.Zero;
            LastSeenUtc = DateTime.UtcNow;
            LastPositionUtc = DateTime.MinValue;
        }

        public void Update(string name, byte team, bool alive)
        {
            lock (_lock)
            {
                Name = name;
                Team = team;
                IsAlive = alive;
                LastSeenUtc = DateTime.UtcNow;
            }
        }

        public void UpdatePosition(Vector3 position)
        {
            lock (_lock)
            {
                Position = position;
                LastPositionUtc = DateTime.UtcNow;
                LastSeenUtc = LastPositionUtc;
            }
        }

        public override string ToString()
        {
            return $"PlayerState(actorId={ActorId}, name={Name}, team={Team}, alive={IsAlive}, pos={Position}, lastSeen={LastSeenUtc:O})";
        }
    }
}
