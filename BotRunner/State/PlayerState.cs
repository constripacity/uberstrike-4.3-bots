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
        public int Cmid => ActorId;
        public string Name { get; private set; }
        public byte Team { get; private set; }
        public bool IsAlive { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public DateTime LastSeenUtc { get; private set; }
        public DateTime LastPositionUtc { get; private set; }
        // Optional extended state for multi-agent coordination
        public Vector3 FacingDirection { get; private set; } = new Vector3(0, 0, 1);
        public int? CurrentTargetId { get; private set; } = null;

        public PlayerState(int actorId, string name, byte team, bool alive, int health = 100, int maxHealth = 100)
        {
            ActorId = actorId;
            Name = name;
            Team = team;
            IsAlive = alive;
            Position = Vector3.Zero;
            Velocity = Vector3.Zero;
            Health = health;
            MaxHealth = maxHealth;
            LastSeenUtc = DateTime.UtcNow;
            LastPositionUtc = DateTime.MinValue;
        }

        public void Update(string name, byte team, bool alive, int health, int maxHealth)
        {
            lock (_lock)
            {
                Name = name;
                Team = team;
                IsAlive = alive;
                Health = health;
                MaxHealth = maxHealth;
                LastSeenUtc = DateTime.UtcNow;
            }
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

        // Optional helper to set facing and current target (used by coordination systems)
        public void UpdateTactical(Vector3 facingDirection, int? currentTargetId)
        {
            lock (_lock)
            {
                FacingDirection = facingDirection;
                CurrentTargetId = currentTargetId;
                LastSeenUtc = DateTime.UtcNow;
            }
        }

        public void UpdatePosition(Vector3 position)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (LastPositionUtc != DateTime.MinValue)
                {
                    var deltaSeconds = (float)(now - LastPositionUtc).TotalSeconds;
                    if (deltaSeconds > 0.001f)
                    {
                        Velocity = (position - Position) / deltaSeconds;
                    }
                }
                Position = position;
                LastPositionUtc = now;
                LastSeenUtc = LastPositionUtc;
            }
        }

        public override string ToString()
        {
            return $"PlayerState(actorId={ActorId}, name={Name}, team={Team}, alive={IsAlive}, pos={Position}, vel={Velocity}, health={Health}/{MaxHealth}, lastSeen={LastSeenUtc:O})";
        }

        public PlayerSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new PlayerSnapshot(ActorId, Name, Team, IsAlive, Position, LastSeenUtc, LastPositionUtc);
            }
        }
    }
}
