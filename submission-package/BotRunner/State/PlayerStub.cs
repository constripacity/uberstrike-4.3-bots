using System.Numerics;

namespace BotRunner.State
{
    public record PlayerStub(int ActorId, string Name, byte Team, bool Alive, Vector3 Position, int Health = 100, int MaxHealth = 100, Vector3? Velocity = null);
}