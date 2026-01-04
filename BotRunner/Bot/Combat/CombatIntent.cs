using System.Numerics;

namespace BotRunner.Bot.Combat
{
    public readonly struct CombatIntent
    {
        public CombatIntent(bool shouldShoot, Vector3 aimPoint, float confidence, bool shouldReload, int desiredWeaponId)
        {
            ShouldShoot = shouldShoot;
            AimPoint = aimPoint;
            Confidence = confidence;
            ShouldReload = shouldReload;
            DesiredWeaponId = desiredWeaponId;
        }

        public bool ShouldShoot { get; }
        public Vector3 AimPoint { get; }
        public float Confidence { get; }
        public bool ShouldReload { get; }
        public int DesiredWeaponId { get; }
    }
}
