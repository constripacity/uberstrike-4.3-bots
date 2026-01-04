using System;
using System.Linq;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Bot.Behaviors;
using BotRunner.Bot.AI;
using BotRunner.Bot.Combat;

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
        private readonly HoldPositionBehavior _holdBehavior = new();
        private readonly Random _intentRandom;
        private readonly TimeSpan _reactionDelay;
        private Vector3 _currentPosition = Vector3.Zero;
        private MovementIntent _lastIntent = MovementIntent.None;
        private bool _spawned;
        private DateTime _lastIntentAppliedUtc = DateTime.MinValue;
        private readonly UtilityAISelector _utility;
        private readonly CombatIntentGenerator _combatIntentGenerator;
        private DateTime _fsmStateEnteredUtc = DateTime.UtcNow;
        private string _activeBehaviorName = string.Empty;

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
            _utility = new UtilityAISelector(
                new IUtilityBehavior[]
                {
                    new UtilityWanderBehavior(_wanderBehavior),
                    new UtilityChaseBehavior(_chaseBehavior, _botConfig.EngageDistanceMeters),
                    new UtilityDisengageBehavior(_disengageBehavior, Math.Max(1f, _botConfig.EngageDistanceMeters * 0.5f)),
                    new UtilityStrafeBehavior(new StrafeBehavior(2f, movementSeed), _botConfig.EngageDistanceMeters),
                    new UtilityHoldBehavior(_holdBehavior, Math.Max(3f, _botConfig.EngageDistanceMeters * 0.35f), _botConfig.EngageDistanceMeters)
                },
                stickinessBonus: 0.5f,
                minHold: TimeSpan.FromMilliseconds(600),
                overrideDelta: 0.5f,
                noiseSeed: movementSeed.HasValue ? movementSeed.Value ^ 0x7f4a7c15 : null,
                noiseAmplitude: 0.02f);
            _combatIntentGenerator = new CombatIntentGenerator(movementSeed ?? Environment.TickCount);
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

            if (!_matchState.MatchRunning && (_state == BotFsmState.Spawning || _state == BotFsmState.Roaming || _state == BotFsmState.Engaging || _state == BotFsmState.Dead))
            {
                _spawned = false;
                _lastIntent = MovementIntent.None;
                _lastIntentAppliedUtc = DateTime.MinValue;
                TransitionTo(BotFsmState.WaitingForMatch);
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
                    if (!HasEngageableEnemy(out _))
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
            var now = DateTime.UtcNow;
            var self = _worldState.Get(_rpcSender.LocalActorId);
            var distanceToEnemy = enemy != null ? Vector3.Distance(enemy.Position, _currentPosition) : float.PositiveInfinity;
            var ctx = new BehaviorContext(
                _currentPosition,
                self,
                enemy,
                distanceToEnemy,
                now - _fsmStateEnteredUtc,
                _activeBehaviorName,
                now);
            var behavior = _utility.Select(ctx, out var scores);
            var intent = behavior.GetIntent(ctx);
            var appliedIntent = ApplyHumanization(intent, DateTime.UtcNow);
            if (!string.Equals(_activeBehaviorName, behavior.Name, StringComparison.Ordinal))
            {
                _activeBehaviorName = behavior.Name;
                BotRunner.Utils.Logger.Info($"[Bot] Behavior -> {_activeBehaviorName} (state={_state})");
                _metrics?.RecordBehaviorSwitch(_activeBehaviorName);
            }
            else
            {
                _metrics?.SetCurrentBehavior(_activeBehaviorName);
            }
            BotRunner.Utils.Logger.Debug("[Utility] Scores: " + string.Join(", ", scores.Select(s => $"{s.Name}:{s.RawScore:0.00}->{s.AdjustedScore:0.00}{(s.IsCurrent ? " (current)" : string.Empty)}")) + $" | selected={behavior.Name}");

            if (appliedIntent.HasTarget)
            {
                _currentPosition = MoveTowards(_currentPosition, appliedIntent.TargetPosition, _botConfig.MaxWalkSpeed, deltaSeconds);
                // M1: optimistic local position authority; M2 may reconcile with server echoes later.
                _worldState.UpdatePosition(_rpcSender.LocalActorId, _currentPosition);
            }

            if (_positionLimiter.TryConsume(now))
            {
                var serverTicks = _matchState.LastKnownServerTicks != 0
                    ? _matchState.LastKnownServerTicks
                    : Environment.TickCount & int.MaxValue; // TODO: replace with server-synchronized ticks when available.
                _rpcSender.SendPositionUpdate(_rpcSender.LocalActorId, _currentPosition, serverTicks);
                _metrics?.IncrementPositionUpdatesSent();
            }

            if (_state == BotFsmState.Engaging && enemy != null)
            {
                var combatIntent = _combatIntentGenerator.Generate(_currentPosition, enemy.Position, distanceToEnemy);
                _metrics?.RecordCombatIntent(combatIntent.ShouldShoot);
                BotRunner.Utils.Logger.Debug($"[Combat] Intent -> shoot={combatIntent.ShouldShoot}, burstMs={combatIntent.BurstDurationMs}, aim={combatIntent.AimPoint}");
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

            var now = DateTime.UtcNow;
            BotRunner.Utils.Logger.Info($"[Bot] State {_state} -> {next} (actorId={_rpcSender.LocalActorId})");
            _state = next;
            _fsmStateEnteredUtc = now;
            _metrics?.SetCurrentBehavior(_activeBehaviorName);
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
