using System;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Bot.Behaviors;

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
        private readonly RateLimiter _positionLimiter;
        private readonly RunMetrics? _metrics;
        private readonly WanderBehavior _wanderBehavior;
        private readonly ChaseNearestEnemyBehavior _chaseBehavior = new();
        private readonly DisengageBehavior _disengageBehavior;
        private readonly Random _intentRandom;
        private readonly TimeSpan _reactionDelay;
        private Vector3 _currentPosition = Vector3.Zero;
        private MovementIntent _lastIntent = MovementIntent.None;
        private bool _spawned;
        private DateTime _lastIntentAppliedUtc = DateTime.MinValue;

        private BotFsmState _state = BotFsmState.Joining;
        private bool _joinSent;

        public BotBrain(WorldState worldState, MatchState matchState, RpcSender rpcSender, BotSettings botSettings, RoomSettings roomConfig, RunMetrics? metrics = null, int? movementSeed = null)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcSender = rpcSender;
            _botConfig = botSettings.Config;
            _roomConfig = roomConfig;
            _metrics = metrics;
            _wanderBehavior = new WanderBehavior(Vector3.Zero, _botConfig.RoamRadiusMeters, 1f, movementSeed);
            _disengageBehavior = new DisengageBehavior(Math.Max(1f, _botConfig.EngageDistanceMeters * 0.5f));
            _positionLimiter = new RateLimiter(TimeSpan.FromMilliseconds(50)); // ~20Hz position updates
            _intentRandom = movementSeed.HasValue ? new Random(movementSeed.Value ^ 0x5f3759df) : new Random();
            _reactionDelay = TimeSpan.FromMilliseconds(Math.Max(0, _botConfig.ReactionDelayMs));
            _metrics?.EnterState(_state.ToString());
        }

        public void Tick()
        {
            // Global death check at the start of each tick.
            var self = _worldState.Get(_rpcSender.LocalActorId);
            if (self != null && !self.IsAlive && _state != BotFsmState.Dead)
            {
                TransitionTo(BotFsmState.Dead);
                _spawned = false;
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
                        var spawnPos = ResolveSpawnPosition();
                        RequestSpawn(spawnPos);
                        _spawned = true;
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

            BotRunner.Utils.Logger.Info("[Bot] Sending join request...");
            _rpcSender.SendJoinRoom();
            _joinSent = true;
            TransitionTo(BotFsmState.WaitingForMatch);
        }

        private void RequestSpawn(Vector3 spawnPos)
        {
            _rpcSender.SendSpawnRequest(_rpcSender.LocalActorId, spawnPos);
            _currentPosition = spawnPos;
            _worldState.UpdatePosition(_rpcSender.LocalActorId, spawnPos);
            _wanderBehavior.SetRoamCenter(spawnPos);
            _positionLimiter.Reset(DateTime.UtcNow);
            BotRunner.Utils.Logger.Info($"[Bot] Spawn requested at {spawnPos}");
        }

        private bool HasEngageableEnemy(out PlayerState? enemy)
        {
            var self = _worldState.Get(_rpcSender.LocalActorId);
            var selfPos = self?.Position ?? Vector3.Zero;
            enemy = _worldState.FindNearestEnemy(_botConfig.TeamId, selfPos, _botConfig.EnemyStaleTimeout, _rpcSender.LocalActorId);
            if (enemy == null)
            {
                return false;
            }

            var dist = Vector3.Distance(enemy.Position, selfPos);
            return dist <= _botConfig.EngageDistanceMeters;
        }

        private void UpdateMovementAndPosition()
        {
            if (!_spawned)
            {
                return;
            }

            // Use bot logic tick rate to derive delta time.
            var deltaSeconds = 1f / Math.Max(1, _roomConfig.BotLogicTickRateHz);
            var enemy = _worldState.FindNearestEnemy(_botConfig.TeamId, _currentPosition, _botConfig.EnemyStaleTimeout, _rpcSender.LocalActorId);
            var behavior = SelectBehavior(enemy);
            var intent = behavior.GetIntent(new BotBehaviorContext(_currentPosition, _worldState.Get(_rpcSender.LocalActorId), enemy));
            var appliedIntent = ApplyHumanization(intent, DateTime.UtcNow);

            if (appliedIntent.HasTarget)
            {
                _currentPosition = MoveTowards(_currentPosition, appliedIntent.TargetPosition, _botConfig.MaxWalkSpeed, deltaSeconds);
                _worldState.UpdatePosition(_rpcSender.LocalActorId, _currentPosition);
            }

            var now = DateTime.UtcNow;
            if (_positionLimiter.TryConsume(now))
            {
                var serverTicks = _matchState.LastKnownServerTicks != 0
                    ? _matchState.LastKnownServerTicks
                    : Environment.TickCount; // TODO: replace with server-synchronized ticks when available.
                _rpcSender.SendPositionUpdate(_rpcSender.LocalActorId, _currentPosition, serverTicks);
                _metrics?.IncrementPositionUpdatesSent();
            }
        }

        private Vector3 ResolveSpawnPosition()
        {
            if (_matchState.TryConsumeSpawnFor(_rpcSender.LocalActorId, out var serverPos))
            {
                return serverPos;
            }

            return _currentPosition;
        }

        private IBotBehavior SelectBehavior(PlayerState? nearestEnemy)
        {
            switch (_state)
            {
                case BotFsmState.Engaging when nearestEnemy != null:
                    var dist = Vector3.Distance(nearestEnemy.Position, _currentPosition);
                    var disengageThreshold = Math.Max(1f, _botConfig.EngageDistanceMeters * 0.5f);
                    return dist < disengageThreshold ? _disengageBehavior : _chaseBehavior;
                case BotFsmState.Roaming:
                default:
                    return _wanderBehavior;
            }
        }

        private MovementIntent ApplyHumanization(MovementIntent intent, DateTime utcNow)
        {
            if ((utcNow - _lastIntentAppliedUtc) < _reactionDelay)
            {
                return _lastIntent;
            }

            _lastIntentAppliedUtc = utcNow;
            if (!intent.HasTarget)
            {
                _lastIntent = MovementIntent.None;
                return _lastIntent;
            }

            var jitter = _botConfig.JitterStrengthMeters;
            if (jitter > 0f)
            {
                var angle = _intentRandom.NextDouble() * Math.PI * 2;
                var radius = _intentRandom.NextDouble() * jitter;
                var offset = new Vector3((float)(Math.Cos(angle) * radius), 0f, (float)(Math.Sin(angle) * radius));
                intent = new MovementIntent(intent.TargetPosition + offset);
            }

            _lastIntent = intent;
            return intent;
        }

        private static Vector3 MoveTowards(Vector3 current, Vector3 target, float speedMetersPerSec, float deltaSeconds)
        {
            var toTarget = target - current;
            var distance = toTarget.Length();
            if (distance < float.Epsilon)
            {
                return current;
            }

            var maxStep = speedMetersPerSec * deltaSeconds;
            var step = Math.Min(distance, maxStep);
            return current + toTarget / distance * step;
        }

        private void TransitionTo(BotFsmState next)
        {
            if (_state == next)
            {
                return;
            }

            BotRunner.Utils.Logger.Info($"[Bot] State {_state} -> {next} (actorId={_rpcSender.LocalActorId})");
            _state = next;
            _metrics?.EnterState(next.ToString());
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
