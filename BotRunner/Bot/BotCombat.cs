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
            var damage = (short)10; // TODO: align with weapon stats
            var bodyPart = (byte)0; // TODO: choose based on aim
            var projectileId = 0; // TODO: weapon projectile id
            var angleByte = (byte)_random.Next(0, 255);
            var weaponId = 0; // TODO: current weapon id
            var weaponClass = (byte)0; // TODO: weapon class enum value
            var effectFlag = 0;
            var effectValue = 0f;

            BotRunner.Utils.Logger.Info($"[Bot] Firing at {target.Name} with aim error {aimOffset:0.00} degrees");
            _rpcSender.SendPlayerHit(_rpcSender.LocalActorId, target.Cmid, damage, bodyPart, projectileId, angleByte, weaponId, weaponClass, effectFlag, effectValue);
        }
    }
}
