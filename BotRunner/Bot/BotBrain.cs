using System;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Bot
{
    /// <summary>
    /// Simple finite state machine that orchestrates connection, spawning, roaming, and combat.
    /// This intentionally mirrors the cadence of the retail client without exploiting server trust.
    /// </summary>
    public class BotBrain
    {
        private readonly WorldState _worldState;
        private readonly MatchState _matchState;
        private readonly RpcSender _rpcSender;
        private readonly BotSettings _botSettings;
        private readonly RoomSettings _roomSettings;
        private readonly BotMovement _movement;
        private readonly BotCombat _combat;
        private readonly RateLimiter _positionLimiter;

        private BotState _state = BotState.Connecting;
        private Vector3 _currentPosition = Vector3.Zero;
        private Vector3 _currentVelocity = Vector3.Zero;
        private DateTime _lastSpawnRequest = DateTime.MinValue;

        public BotBrain(WorldState worldState, MatchState matchState, RpcSender rpcSender, BotSettings botSettings, RoomSettings roomSettings)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcSender = rpcSender;
            _botSettings = botSettings;
            _roomSettings = roomSettings;
            _movement = new BotMovement(botSettings.Config);
            _combat = new BotCombat(rpcSender, botSettings.Config);
            _positionLimiter = new RateLimiter(TimeSpan.FromMilliseconds(50)); // 20Hz gameplay tick
        }

        public void Tick()
        {
            switch (_state)
            {
                case BotState.Connecting:
                    Console.WriteLine("[Bot] Transition -> Joining room");
                    _rpcSender.SendJoinRoom();
                    _state = BotState.Joining;
                    break;
                case BotState.Joining:
                    if (_matchState.MatchRunning)
                    {
                        _state = BotState.Spawning;
                    }
                    else
                    {
                        _state = BotState.WaitingForMatch;
                    }
                    break;
                case BotState.WaitingForMatch:
                    if (_matchState.MatchRunning)
                    {
                        _state = BotState.Spawning;
                    }
                    break;
                case BotState.Spawning:
                    TrySpawn();
                    break;
                case BotState.Roaming:
                    PerformRoam();
                    break;
                case BotState.EngagingEnemy:
                    EngageEnemy();
                    break;
                case BotState.Dead:
                    TryRespawn();
                    break;
            }
        }

        private void TrySpawn()
        {
            var sinceAllowed = DateTime.UtcNow - _matchState.LastSpawnAllowedAt;
            if (sinceAllowed < TimeSpan.FromMilliseconds(_botSettings.Config.RespawnDelayMs))
            {
                return;
            }

            if ((DateTime.UtcNow - _lastSpawnRequest).TotalMilliseconds < 500)
            {
                return;
            }

            _lastSpawnRequest = DateTime.UtcNow;
            Console.WriteLine("[Bot] Requesting spawn position...");
            _rpcSender.SendSpawnRequest(_currentPosition);
            _state = BotState.Roaming;
        }

        private void PerformRoam()
        {
            var nearestEnemy = _worldState.FindNearestEnemy(ourTeam: 0, _currentPosition);
            if (nearestEnemy != null)
            {
                _state = BotState.EngagingEnemy;
                return;
            }

            var target = _movement.ChooseRoamTarget(_currentPosition, _botSettings.Config.RoamRadius);
            (_currentPosition, _currentVelocity) = _movement.MoveTowards(_currentPosition, target);
            SendPositionUpdateIfNeeded();
        }

        private void EngageEnemy()
        {
            var enemy = _worldState.FindNearestEnemy(ourTeam: 0, _currentPosition);
            if (enemy == null)
            {
                _state = BotState.Roaming;
                return;
            }

            (_currentPosition, _currentVelocity) = _movement.Chase(_currentPosition, enemy.Position);
            SendPositionUpdateIfNeeded();
            _combat.TryFire(enemy);
        }

        private void TryRespawn()
        {
            var sinceDeath = DateTime.UtcNow - _matchState.LastDeathAt;
            if (sinceDeath.TotalMilliseconds < _botSettings.Config.RespawnDelayMs)
            {
                return;
            }

            _state = BotState.Spawning;
        }

        private void SendPositionUpdateIfNeeded()
        {
            _positionLimiter.SleepUntilNext();
            var serverTime = (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds;
            _rpcSender.SendPositionUpdate(_currentPosition, _currentVelocity, serverTime);
        }

        private enum BotState
        {
            Connecting,
            Joining,
            WaitingForMatch,
            Spawning,
            Roaming,
            EngagingEnemy,
            Dead
        }
    }
}
