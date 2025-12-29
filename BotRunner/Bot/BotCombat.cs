using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot
{
    /// <summary>
    /// Handles aiming and firing cadence. Damage values are placeholders; the server remains
    /// authoritative, so these calls simply request validation.
    /// </summary>
    public class BotCombat
    {
        private readonly RpcSender _rpcSender;
        private readonly BotConfig _config;
        private readonly RateLimiter _fireLimiter;
        private readonly Random _random = new();

        public BotCombat(RpcSender rpcSender, BotConfig config)
        {
            _rpcSender = rpcSender;
            _config = config;
            _fireLimiter = new RateLimiter(TimeSpan.FromMilliseconds(config.FireRateMs));
        }

        public void TryFire(PlayerState target)
        {
            var reactionDelay = TimeSpan.FromMilliseconds(_config.ReactionDelayMs);
            _fireLimiter.SleepUntilNext();
            Task.Delay(reactionDelay).Wait();

            var aimOffset = (float)(_random.NextDouble() * _config.AimErrorDegrees);
            var damage = 10; // TODO: align with weapon stats
            var hitPoint = target.Position + new Vector3(0, aimOffset / 10f, 0);

            Console.WriteLine($"[Bot] Firing at {target.Name} with aim error {aimOffset:0.00} degrees");
            _rpcSender.SendPlayerHit(target.Cmid, damage, hitPoint);
        }
    }
}
