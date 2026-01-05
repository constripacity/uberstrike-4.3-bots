using BotRunner.Bot.Behaviors;

namespace BotRunner.Bot.AI
{
    public interface IUtilityBehavior
    {
        string Name { get; }
        float Score(BehaviorContext ctx);
        MovementIntent GetIntent(BehaviorContext ctx);
    }
}
