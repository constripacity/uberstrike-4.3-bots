using System;
using System.Numerics;

namespace BotRunner.Bot.Combat
{
    public class CombatIntentGenerator
    {
        private readonly Random _random;
        private readonly float _closeRange = 10f;
        private readonly float _midRange = 25f;

        public CombatIntentGenerator(int seed)
        {
            _random = new Random(seed);
        }

        public CombatIntent Generate(Vector3 currentPos, Vector3 enemyPos, float distance)
        {
            var shouldShoot = ShouldShoot(distance);
            var burstMs = shouldShoot ? _random.Next(120, 400) : 0;
            var aim = AddJitter(enemyPos);
            return new CombatIntent(shouldShoot, burstMs, aim);
        }

        private bool ShouldShoot(float distance)
        {
            if (distance <= _closeRange)
            {
                return _random.NextDouble() > 0.15;
            }

            if (distance <= _midRange)
            {
                return _random.NextDouble() > 0.35;
            }

            return _random.NextDouble() > 0.65;
        }

        private Vector3 AddJitter(Vector3 aimPoint)
        {
            var offset = new Vector3(
                (float)(_random.NextDouble() * 0.5 - 0.25),
                (float)(_random.NextDouble() * 0.15 - 0.075),
                (float)(_random.NextDouble() * 0.5 - 0.25));
            return aimPoint + offset;
        }
    }
}
