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
        private readonly HoldPositionBehavior _holdBehavior;
        private readonly Random _intentRandom;
        private readonly TimeSpan _reactionDelay;
        private Vector3 _currentPosition = Vector3.Zero;
        private MovementIntent _lastIntent = MovementIntent.None;
        private bool _spawned;
        private DateTime _lastIntentAppliedUtc = DateTime.MinValue;
        private readonly UtilityAISelector _utility;
        private readonly CombatIntentGenerator _combatIntentGenerator;
        private DateTime _fsmStateEnteredUtc = DateTime.MinValue;
        private string _activeBehaviorName = string.Empty;
        private readonly bool _debugScoreLogs;
        private readonly ActionPipeline _actionPipeline;
        private readonly TargetHysteresis _targetHysteresis;
        private readonly int _movementSeed;
        private readonly BotMovement _botMovement;

        private BotFsmState _state = BotFsmState.Joining;
        private bool _joinSent;

        public BotBrain(WorldState worldState, MatchState matchState, RpcSender rpcSender, BotSettings botSettings, RoomSettings roomConfig, RunMetrics? metrics = null, int seed = 0)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcSender = rpcSender;
            _botConfig = botSettings.Config;
            _roomConfig = roomConfig;
            _metrics = metrics;
            _targetHysteresis = new TargetHysteresis(metrics);
            // Ensure all random instances are seeded for determinism
            var rootSeed = seed;
            // Movement RNG derived from scenario seed; only falls back to Environment.TickCount if no seed is provided.
            _movementSeed = rootSeed != 0 ? rootSeed ^ 0x5f3759df : Environment.TickCount;
            _actionPipeline = new ActionPipeline(metrics, new ActionPipelineSettings(), rootSeed);
            _wanderBehavior = new WanderBehavior(Vector3.Zero, _botConfig.RoamRadiusMeters, 1f, _movementSeed);
            _botMovement = new BotMovement(Vector3.Zero, _botConfig.RoamRadiusMeters, _botConfig.MaxWalkSpeed, 1f, _movementSeed);
            _disengageBehavior = new DisengageBehavior(Math.Max(1f, _botConfig.EngageDistanceMeters * 0.5f));
            var util = _botConfig.Utility;
            var panic = util.PanicDistanceMeters > 0 ? util.PanicDistanceMeters : _botConfig.EngageDistanceMeters * 0.33f;
            var preferredMin = util.PreferredMinMeters > 0 ? util.PreferredMinMeters : _botConfig.EngageDistanceMeters * 0.45f;
            var preferredMax = util.PreferredMaxMeters > 0 ? util.PreferredMaxMeters : _botConfig.EngageDistanceMeters * 0.73f;
            var strafeMax = util.StrafeMaxMeters > 0 ? util.StrafeMaxMeters : _botConfig.EngageDistanceMeters * 0.86f;
            var orbitMin = Math.Max(5f, preferredMin * 0.7f);
            var orbitMax = Math.Min(Math.Max(orbitMin + 2f, 15f), Math.Max(preferredMin + 6f, preferredMax));
            var orbitIdeal = Math.Clamp(preferredMin, orbitMin, orbitMax);
            _holdBehavior = new HoldPositionBehavior(preferredMin, preferredMax);
            _positionLimiter = new RateLimiter(TimeSpan.FromMilliseconds(50)); // ~20Hz position updates
            _intentRandom = new Random(rootSeed ^ 0x2);
            _reactionDelay = TimeSpan.FromMilliseconds(Math.Max(0, _botConfig.ReactionDelayMs));
            _utility = new UtilityAISelector(
                new IUtilityBehavior[]
                {
                    new UtilityWanderBehavior(_wanderBehavior),
                    new UtilityChaseBehavior(_chaseBehavior, preferredMax, _botConfig.EngageDistanceMeters),
                    new UtilityFlankBehavior(new FlankBehavior(metrics: _metrics, flankDistance: 6f, sideOffset: 4f), stateBias: 0.04f),
                    new UtilityDisengageBehavior(_disengageBehavior, panic),
                    new UtilityOrbitStrafeBehavior(new OrbitStrafeBehavior(orbitIdeal, orbitMin, orbitMax, flipMinSeconds: 2f, flipMaxSeconds: 4f, seed: rootSeed ^ 0x3), orbitMin, orbitMax, orbitIdeal),
                    new UtilityStrafeBehavior(new StrafeBehavior(2f, rootSeed ^ 0x4), preferredMin, strafeMax),
                    new UtilityHoldBehavior(_holdBehavior, preferredMin, preferredMax),
                    new UtilityCoverBehavior(new CoverBehavior())
                },
                stickinessBonus: util.StickinessBonus,
                minHold: TimeSpan.FromMilliseconds(util.MinHoldMilliseconds),
                overrideDelta: util.OverrideDelta,
                noiseSeed: rootSeed ^ 0x5,
                noiseAmplitude: util.NoiseAmplitude);
            _combatIntentGenerator = new CombatIntentGenerator(_worldState, rootSeed ^ 0x6, metrics);
            var envLog = Environment.GetEnvironmentVariable("LOG_LEVEL");
            _debugScoreLogs = string.Equals(envLog, "debug", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(envLog, "trace", StringComparison.OrdinalIgnoreCase);
            _metrics?.EnterState(_state.ToString());
        }

        public void Reset()
        {
            _currentPosition = Vector3.Zero;
            _lastIntent = MovementIntent.None;
            _spawned = false;
            _lastIntentAppliedUtc = DateTime.MinValue;
            _fsmStateEnteredUtc = DateTime.MinValue;
            _activeBehaviorName = string.Empty;
            _state = BotFsmState.Joining;
            _joinSent = false;
            _targetHysteresis.Reset();
            _combatIntentGenerator.Simulator.ResetState();
            _positionLimiter.Reset(SimulationTime.Instance.Now);
            _metrics?.EnterState(_state.ToString());
        }

        public void Tick()
        {
            if (_fsmStateEnteredUtc == DateTime.MinValue)
            {
                _fsmStateEnteredUtc = SimulationTime.Instance.Now;
            }
            BotRunner.Utils.Logger.Debug($"[BotBrain] Tick state={_state} spawned={_spawned} pos={_currentPosition}");
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

            // Update combat simulator
            _combatIntentGenerator.Simulator.Update();

            // Sync simulator health with world state
            if (self != null)
            {
                var simState = _combatIntentGenerator.Simulator.GetBotState();
                if (self.Health != simState.Health)
                {
                    Logger.Info($"[BotBrain] Health Sync: World={self.Health}, Sim={simState.Health}");
                }

                if (self.Health < simState.Health)
                {
                    // This is external damage
                    var damage = simState.Health - self.Health;
                    Logger.Info($"[BotBrain] Simulating {damage} incoming damage from world state");
                    _combatIntentGenerator.Simulator.ReceiveDamage(damage, -1);
                }
                else if (self.Health > simState.Health)
                {
                    // This could be healing or initial sync
                    simState.Health = self.Health;
                }
            }

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
                    if (_matchState.CanRespawnNow(SimulationTime.Instance.Now))
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
                    UpdateActions();
                    break;
                case BotFsmState.Engaging:
                    if (!HasEngageableEnemy(out _))
                    {
                        TransitionTo(BotFsmState.Roaming);
                    }
                    UpdateActions();
                    break;
                case BotFsmState.Dead:
                    if (_matchState.CanRespawnNow(SimulationTime.Instance.Now))
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
            // Ensure bot is in world state so coordination logic works
            _worldState.Upsert(_rpcSender.LocalActorId, "LocalBot", _botConfig.TeamId, true);
            _worldState.UpdatePosition(_rpcSender.LocalActorId, spawnPos);
            _wanderBehavior.SetRoamCenter(spawnPos);
            _positionLimiter.Reset(SimulationTime.Instance.Now);
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

private void UpdateActions()
        {
            if (!_spawned)
            {
                BotRunner.Utils.Logger.Info("[BotBrain] UpdateActions: forcing execution for diagnostic (was not spawned)");
                // NOTE: Diagnostic override - allow pipeline to run even if not spawned.
            }
            else
            {
                BotRunner.Utils.Logger.Info("[BotBrain] UpdateActions entering - spawned");
            }

            var now = SimulationTime.Instance.Now;
            var self = _worldState.Get(_rpcSender.LocalActorId);
            var visibleEnemies = _worldState.GetEnemies(_botConfig.TeamId, _botConfig.EnemyStaleTimeout).ToList();
            var visibleAllies = _worldState.GetAllies(_botConfig.TeamId, _rpcSender.LocalActorId).ToList();
            var targetId = _targetHysteresis.SelectTarget(visibleEnemies, _currentPosition);
            var target = visibleEnemies.FirstOrDefault(e => e.ActorId == targetId);

            // 1. Get behavior context
            var nearbyEnemies = visibleEnemies.Count(e => Vector3.Distance(e.Position, _currentPosition) < 30f);
            var nearbyAllies = visibleAllies.Count(a => Vector3.Distance(a.Position, _currentPosition) < 30f);
            var isOutnumbered = nearbyEnemies > nearbyAllies + 1; // Improved heuristic

            var context = new BehaviorContext(
                _currentPosition,
                self,
                target,
                target != null ? Vector3.Distance(target.Position, _currentPosition) : float.PositiveInfinity,
                now - _fsmStateEnteredUtc,
                _activeBehaviorName,
                now,
                isEngagingState: _state == BotFsmState.Engaging,
                healthRatio: _combatIntentGenerator.Simulator.GetBotState().HealthRatio,
                ammoRatio: _combatIntentGenerator.Simulator.GetBotState().GetCurrentWeapon()?.AmmoRatio ?? 1f,
                enemyCount: visibleEnemies.Count,
                nearbyEnemiesCount: nearbyEnemies,
                nearbyAlliesCount: nearbyAllies,
                isOutnumbered: isOutnumbered);
            
            // 2. Let utility AI select behavior
            var decision = _utility.Select(context);
            _metrics?.RecordBehaviorSpread(decision.Scores);
            _metrics?.RecordBehaviorDecision(decision.Behavior.Name, decision.Switched, decision.Reason);
            if (decision.Switched)
            {
                Logger.Info($"[Bot] Switching to {decision.Behavior.Name} for positional advantage (Reason: {decision.Reason})");
                _activeBehaviorName = decision.Behavior.Name;
            }

            if (_debugScoreLogs || decision.Switched || decision.Behavior.Name == "Flank")
            {
                var flankScore = decision.Scores.FirstOrDefault(s => s.Name == "Flank");
                if (flankScore.Name != null)
                {
                    Logger.Info($"[UtilityAI] FlankBehavior score: {flankScore.RawScore:F2} (context: has_ally={context.NearbyAlliesCount > 0}, enemy_dist={context.DistanceToEnemy:F1}m)");
                }
            }

            var movementIntent = decision.Behavior.GetIntent(context);
            
            // 3. Generate combat intent
            CombatIntent combatIntent = _combatIntentGenerator.Generate(context, target);

            BotRunner.Utils.Logger.Info($"[BotBrain] VisibleEnemies={visibleEnemies.Count} targetId={targetId} targetActor={(target==null?-1:target.ActorId)}");
            BotRunner.Utils.Logger.Info($"[BotBrain] MovementIntent: HasTarget={movementIntent.HasTarget} Target={movementIntent.TargetPosition}");
            BotRunner.Utils.Logger.Info($"[BotBrain] CombatIntent: ShouldShoot={(combatIntent?.ShouldShoot ?? false)} Accuracy={(combatIntent?.Accuracy ?? 0f):F2} Reason={combatIntent?.Reason}");
            
            // 4. UNIFY DECISIONS via action pipeline
            var actionFrame = _actionPipeline.GenerateFrame(
                context, 
                movementIntent, 
                combatIntent!);
            
            if (actionFrame != null)
            {
                BotRunner.Utils.Logger.Info("[ActionPipeline] Frame generated");
                // 5. Execute the coherent frame
                ExecuteActionFrame(actionFrame);
                
                // 6. Track metrics
                // This seems redundant as the pipeline already records it
                // _metrics?.RecordActionFrame(actionFrame); 
            }
            else
            {
                BotRunner.Utils.Logger.Debug("[ActionPipeline] GenerateFrame returned null");
            }
        }

        private void ExecuteActionFrame(ActionFrame frame)
        {
            // Movement execution
            if (frame.Movement.HasTarget)
            {
                var deltaSeconds = 1f / _roomConfig.BotLogicTickRateHz;
                _currentPosition = MoveTowards(_currentPosition, frame.Movement.TargetPosition, 
                                              _botConfig.MaxWalkSpeed, deltaSeconds);
                _worldState.UpdatePosition(_rpcSender.LocalActorId, _currentPosition);
            }
            
            if (_positionLimiter.TryConsume(SimulationTime.Instance.Now))
            {
                var serverTicks = _matchState.LastKnownServerTicks != 0
                    ? _matchState.LastKnownServerTicks
                    : (int)SimulationTime.Instance.CurrentTick & int.MaxValue;
                _rpcSender.SendPositionUpdate(_rpcSender.LocalActorId, _currentPosition, serverTicks);
                _metrics?.IncrementPositionUpdatesSent();
            }

            // Combat execution
            if (frame.Combat.ShouldShoot)
            {
                var result = _combatIntentGenerator.Simulator.ProcessShootIntent(frame.Combat, frame.Context.NearestEnemy, _currentPosition);
                var weapon = _combatIntentGenerator.Simulator.GetBotState().GetCurrentWeapon();
                if (result.IsHit)
                {
                    Logger.Info($"[Bot] HIT {result.TargetId} for {result.Damage} damage! (hp: {_combatIntentGenerator.Simulator.GetBotState().Health}, ammo: {weapon?.CurrentAmmo}/{weapon?.MaxAmmo})");
                    // In a real scenario, we'd send an RPC here. 
                    // For deterministic testing, the simulator handles it.
                }
                else
                {
                    Logger.Debug($"[Bot] MISS: {result.Reason} (ammo: {weapon?.CurrentAmmo}/{weapon?.MaxAmmo})");
                }
            }
            
            if (frame.Combat.ShouldReload)
            {
                Logger.Info($"[Bot] RELOADING weapon {frame.Combat.DesiredWeaponId}");
                _combatIntentGenerator.Simulator.ReloadWeapon();
            }

            if (frame.Combat.DesiredWeaponId != -1 && frame.Combat.DesiredWeaponId != _combatIntentGenerator.Simulator.CurrentWeaponId)
            {
                Logger.Info($"[Bot] SWITCHING to weapon {frame.Combat.DesiredWeaponId}");
                _combatIntentGenerator.Simulator.SwitchWeapon(frame.Combat.DesiredWeaponId);
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

        private static void LogScores(SelectionDecision decision, bool important)
        {
            var table = string.Join(", ", decision.Scores.Select(s => $"{s.Name}:{s.RawScore:0.00}->{s.AdjustedScore:0.00}{(s.IsCurrent ? " (current)" : string.Empty)}"));
            if (important)
            {
                BotRunner.Utils.Logger.Info($"[Utility] Scores: {table} | selected={decision.Behavior.Name} reason={decision.Reason}");
            }
            else
            {
                BotRunner.Utils.Logger.Debug($"[Utility] Scores: {table} | selected={decision.Behavior.Name} reason={decision.Reason}");
            }
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

            var now = SimulationTime.Instance.Now;
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
