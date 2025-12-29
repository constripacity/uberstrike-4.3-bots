using System;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot
{
    /// <summary>
    /// FSM-only bot brain. Handles high-level join/spawn/engage transitions; navigation and combat
    /// are intentionally omitted for clarity in this reference.
    /// </summary>
    public class BotBrain
    {
        private readonly WorldState _worldState;
        private readonly MatchState _matchState;
        private readonly RpcSender _rpcSender;
        private readonly BotConfig _botConfig;
        private readonly RoomSettings _roomConfig;
        private readonly BotMovement _movement;
        private readonly RateLimiter _positionLimiter;
        private Vector3 _currentPosition = Vector3.Zero;

        private BotFsmState _state = BotFsmState.Joining;
        private bool _joinSent;

        public BotBrain(WorldState worldState, MatchState matchState, RpcSender rpcSender, BotSettings botSettings, RoomSettings roomConfig)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcSender = rpcSender;
            _botConfig = botSettings.Config;
            _roomConfig = roomConfig;
            _movement = new BotMovement(Vector3.Zero, _botConfig.RoamRadiusMeters, _botConfig.MaxWalkSpeed, 1f);
            _positionLimiter = new RateLimiter(TimeSpan.FromMilliseconds(50)); // ~20Hz position updates
        }

        public void Tick()
        {
            // Global death check at the start of each tick.
            var self = _worldState.Get(_rpcSender.LocalActorId);
            if (self != null && !self.IsAlive && _state != BotFsmState.Dead)
            {
                TransitionTo(BotFsmState.Dead);
            }
            else if (self != null && self.IsAlive && _state == BotFsmState.Dead && _matchState.MatchRunning)
            {
                TransitionTo(BotFsmState.Spawning);
            }

            // Keep a local position cache so we can emit PositionUpdate even before server echoes back.
            _currentPosition = self?.Position ?? _currentPosition;

            switch (_state)
            {
                case BotFsmState.Joining:
                    Join();
                    break;
                case BotFsmState.WaitingForMatch:
                    if (_matchState.MatchRunning)
                    {
                        TransitionTo(BotFsmState.Spawning);
                    }
                    break;
                case BotFsmState.Spawning:
                    if (_matchState.CanRespawnNow(DateTime.UtcNow))
                    {
                        RequestSpawn();
                        TransitionTo(BotFsmState.Roaming);
                    }
                    break;
                case BotFsmState.Roaming:
                    if (HasEngageableEnemy(out var enemy))
                    {
                        TransitionTo(BotFsmState.Engaging);
                    }
                    UpdateMovementAndPosition();
                    break;
                case BotFsmState.Engaging:
                    if (!HasEngageableEnemy(out var engagedEnemy))
                    {
                        TransitionTo(BotFsmState.Roaming);
                    }
                    UpdateMovementAndPosition();
                    break;
                case BotFsmState.Dead:
                    if (_matchState.CanRespawnNow(DateTime.UtcNow))
                    {
                        TransitionTo(BotFsmState.Spawning);
                    }
                    break;
            }
        }

        private void Join()
        {
            if (_joinSent)
            {
                return;
            }

            Console.WriteLine("[Bot] Sending join request...");
            _rpcSender.SendJoinRoom();
            _joinSent = true;
            TransitionTo(BotFsmState.WaitingForMatch);
        }

        private void RequestSpawn()
        {
            var spawnPos = Vector3.Zero; // TODO: use spawn points from server when available.
            _rpcSender.SendSpawnRequest(_rpcSender.LocalActorId, spawnPos);
            _currentPosition = spawnPos;
            _worldState.UpdatePosition(_rpcSender.LocalActorId, spawnPos);
            Console.WriteLine($"[Bot] Spawn requested at {spawnPos}");
        }

        private bool HasEngageableEnemy(out PlayerState? enemy)
        {
            var self = _worldState.Get(_rpcSender.LocalActorId);
            var selfPos = self?.Position ?? Vector3.Zero;
            enemy = _worldState.FindNearestEnemy(_botConfig.TeamId, selfPos, _botConfig.EnemyStaleTimeout);
            if (enemy == null)
            {
                return false;
            }

            var dist = Vector3.Distance(enemy.Position, selfPos);
            return dist <= _botConfig.EngageDistanceMeters;
        }

        private void UpdateMovementAndPosition()
        {
            // Use bot logic tick rate to derive delta time.
            var deltaSeconds = 1f / Math.Max(1, _roomConfig.BotLogicTickRateHz);
            _currentPosition = _movement.Step(_currentPosition, deltaSeconds);
            _worldState.UpdatePosition(_rpcSender.LocalActorId, _currentPosition);

            var now = DateTime.UtcNow;
            if (_positionLimiter.TryConsume(now))
            {
                var serverTicks = _matchState.LastKnownServerTicks != 0
                    ? _matchState.LastKnownServerTicks
                    : Environment.TickCount; // TODO: replace with server-synchronized ticks when available.
                _rpcSender.SendPositionUpdate(_rpcSender.LocalActorId, _currentPosition, serverTicks);
            }
        }

        private void TransitionTo(BotFsmState next)
        {
            if (_state == next)
            {
                return;
            }

            Console.WriteLine($"[Bot] State {_state} -> {next} (actorId={_rpcSender.LocalActorId})");
            _state = next;
        }

        private enum BotFsmState
        {
            Joining,
            WaitingForMatch,
            Spawning,
            Roaming,
            Engaging,
            Dead
        }
    }
}
