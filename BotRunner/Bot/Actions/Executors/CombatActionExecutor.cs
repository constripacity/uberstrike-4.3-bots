using System.Collections.Generic;
using BotRunner.Bot.Actions;

namespace BotRunner.Bot.Actions.Executors
{
    public class CombatActionExecutor
    {
        public int Execute(IReadOnlyList<BotAction> actions)
        {
            var executed = 0;
            foreach (var action in actions)
            {
                if (action.Type == BotActionType.Aim || action.Type == BotActionType.Shoot || action.Type == BotActionType.Reload)
                {
                    BotRunner.Utils.Logger.Debug($"[Action] {action.Type} at {action.Position} (conf={action.Confidence:0.00})");
                    executed++;
                }
            }

            return executed;
        }
    }
}
