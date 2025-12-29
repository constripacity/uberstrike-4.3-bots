using System.Numerics;

namespace BotRunner.State
{
    public record PlayerStub(int ActorId, string Name, byte Team, bool Alive, Vector3 Position);
}
