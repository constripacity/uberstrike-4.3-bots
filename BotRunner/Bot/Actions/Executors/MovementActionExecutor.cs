using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Bot.Actions;

namespace BotRunner.Bot.Actions.Executors
{
    public class MovementActionExecutor
    {
        public Vector3 Execute(Vector3 currentPosition, IReadOnlyList<BotAction> actions, float speedMetersPerSec, float deltaSeconds)
        {
            var newPosition = currentPosition;
            foreach (var action in actions)
            {
                if (action.Type != BotActionType.Move || action.Position == null)
                {
                    continue;
                }

                newPosition = MoveTowards(newPosition, action.Position.Value, speedMetersPerSec, deltaSeconds);
            }

            return newPosition;
        }

        private static Vector3 MoveTowards(Vector3 current, Vector3 target, float speedMetersPerSec, float deltaSeconds)
        {
            var toTarget = target - current;
            var distance = toTarget.Length();
            if (distance < float.Epsilon)
            {
                return current;
            }

            var maxStep = speedMetersPerSec * deltaSeconds;
            var step = Math.Min(distance, maxStep);
            return current + toTarget / distance * step;
        }
    }
}
