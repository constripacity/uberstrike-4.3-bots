using System.Numerics;

namespace BotRunner.Bot.Combat
{
    public readonly struct CombatIntent
    {
        public CombatIntent(bool shouldShoot, int burstDurationMs, Vector3 aimPoint)
        {
            ShouldShoot = shouldShoot;
            BurstDurationMs = burstDurationMs;
            AimPoint = aimPoint;
        }

        public bool ShouldShoot { get; }
        public int BurstDurationMs { get; }
        public Vector3 AimPoint { get; }
    }
}
