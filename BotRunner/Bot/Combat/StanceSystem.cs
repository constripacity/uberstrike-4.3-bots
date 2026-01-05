using System;

namespace BotRunner.Bot.Combat
{
    public enum BotStance
    {
        Standing,   // Default
        Crouching,  // Better accuracy, smaller hitbox
        Jumping,    // Evasive, harder to hit
        Prone       // Maximum accuracy, vulnerable
    }

    public class StanceDecision
    {
        public BotStance RecommendedStance { get; set; }
        public string Reason { get; set; } = "";
        public float Duration { get; set; } // How long to maintain stance
        
        public static StanceDecision Default => new() 
        { 
            RecommendedStance = BotStance.Standing,
            Reason = "default",
            Duration = 1.0f
        };
    }

    public static class StanceSystem
    {
        public static float GetAccuracyModifier(BotStance stance)
        {
            return stance switch
            {
                BotStance.Crouching => 1.2f,
                BotStance.Prone => 1.5f,
                BotStance.Jumping => 0.5f,
                _ => 1.0f
            };
        }

        public static float GetEvasionModifier(BotStance stance)
        {
            return stance switch
            {
                BotStance.Jumping => 1.3f,
                BotStance.Crouching => 1.1f,
                _ => 1.0f
            };
        }
    }
}
