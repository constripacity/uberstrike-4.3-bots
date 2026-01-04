using System;
using System.Numerics;

namespace BotRunner.Bot.Actions
{
    public readonly struct BotAction
    {
        public BotAction(BotActionType type, Vector3? position = null, float confidence = 0f, DateTime? createdAtUtc = null)
        {
            Type = type;
            Position = position;
            Confidence = confidence;
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow;
        }

        public BotActionType Type { get; }
        public Vector3? Position { get; }
        public float Confidence { get; }
        public DateTime CreatedAtUtc { get; }
    }
}
