using System;
using System.Numerics;
using BotRunner.Bot.Behaviors;

namespace BotRunner.Bot.Combat
{
    /// <summary>
    /// Single coherent decision frame - unifies movement and combat intents
    /// </summary>
    public class ActionFrame
    {
        public DateTime FrameTime { get; }
        public MovementIntent Movement { get; }
        public CombatIntent Combat { get; }
        public string PrimaryDecision { get; }  // "attack", "retreat", "reposition", "reload"
        public string Reason { get; }
        public float Confidence { get; }
        
        public ActionFrame(DateTime frameTime,
            MovementIntent movement,
            CombatIntent combat,
            string primaryDecision,
            string reason,
            float confidence)
        {
            FrameTime = frameTime;
            Movement = movement;
            Combat = combat;
            PrimaryDecision = primaryDecision;
            Reason = reason;
            Confidence = Math.Clamp(confidence, 0f, 1f);
        }
        
        public ActionFrame(
            MovementIntent movement,
            CombatIntent combat,
            string primaryDecision,
            string reason,
            float confidence) : this(Utils.SimulationTime.Instance.Now, movement, combat, primaryDecision, reason, confidence)
        {
        }
        
        public bool IsValid => Movement.HasTarget || Combat.ShouldShoot || Combat.ShouldReload;
        
        public override string ToString() => 
            $"[{FrameTime:HH:mm:ss.fff}] {PrimaryDecision}: {Reason} (conf: {Confidence:F2})";
    }
}
