using System;
using System.Numerics;

namespace BotRunner.State
{
    /// <summary>
    /// Mirrors the per-player state tracked by the client for scoreboard and bot decision making.
    /// </summary>
    public class PlayerState
    {
        public int Cmid { get; }
        public string Name { get; private set; }
        public byte Team { get; private set; }
        public bool IsAlive { get; private set; }
        public Vector3 Position { get; private set; }
        public DateTime LastSeen { get; private set; }

        public PlayerState(int cmid, string name, byte team, bool isAlive)
        {
            Cmid = cmid;
            Name = name;
            Team = team;
            IsAlive = isAlive;
            Position = Vector3.Zero;
            LastSeen = DateTime.UtcNow;
        }

        public void Update(string name, byte team, bool isAlive)
        {
            Name = name;
            Team = team;
            IsAlive = isAlive;
            LastSeen = DateTime.UtcNow;
        }

        public void UpdatePosition(Vector3 position)
        {
            Position = position;
            LastSeen = DateTime.UtcNow;
        }
    }
}
