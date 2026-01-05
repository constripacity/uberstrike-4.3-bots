using System.Numerics;

namespace BotRunner.Bot.Combat
{
    /// <summary>
    /// Combat intent data-only structure
    /// </summary>
    public class CombatIntent
    {
        public bool ShouldShoot { get; set; }
        public Vector3 AimPoint { get; set; }
        public float Accuracy { get; set; }
        // Combat-specific confidence (used for shoot-window gating)
        public float Confidence { get; set; }
        public bool ShouldReload { get; set; }
        public int DesiredWeaponId { get; set; }
        public bool LeadPredictionUsed { get; set; }
        public string Reason { get; set; } = "";

        public static CombatIntent None => new CombatIntent
        {
            ShouldShoot = false,
            ShouldReload = false,
            DesiredWeaponId = -1,
            Confidence = 0f,
            Reason = "no_target"
        };
    }
}
