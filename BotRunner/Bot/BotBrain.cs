using System;
using System.Numerics;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;

namespace BotRunner.Bot
{
    /// <summary>
    /// Finite state machine for high-level bot lifecycle. Navigation and combat are intentionally omitted
    /// in this reference version; only state transitions and required RPCs are issued.
    /// </summary>
    public class BotBrain
    {
        private readonly WorldState _worldState;
        private readonly MatchState _matchState;
        private readonly RpcSender _rpcSender;
        private readonly BotConfig _config;
        private readonly RoomSettings _roomSettings;

        private BotState _state = BotState.Joining;
        private bool _joinSent;

        public BotBrain(WorldState worldState, MatchState matchState, RpcSender rpcSender, BotSettings botSettings, RoomSettings roomSettings)
        {
            _worldState = worldState;
            _matchState = matchState;
            _rpcSender = rpcSender;
            _config = botSettings.Config;
            _roomSettings = roomSettings;
        }

        public void Tick()
        {
            switch (_state)
            {
                case BotState.Joining:
                    EnterJoining();
                    break;
                case BotState.WaitingForMatch:
                    if (_matchState.MatchRunning)
                    {
                        TransitionTo(BotState.Spawning);
                    }
                    break;
                case BotState.Spawning:
                    if (_matchState.CanRespawnNow(DateTime.UtcNow))
                    {
                        RequestSpawn();
                        TransitionTo(BotState.Roaming);
                    }
                    break;
                case BotState.Roaming:
                    if (HasEngageableEnemy(out _))
                    {
                        TransitionTo(BotState.Engaging);
                    }
                    break;
                case BotState.Engaging:
                    if (!HasEngageableEnemy(out _))
                    {
                        TransitionTo(BotState.Roaming);
                    }
                    break;
                case BotState.Dead:
                    if (_matchState.CanRespawnNow(DateTime.UtcNow))
                    {
                        TransitionTo(BotState.Spawning);
                    }
                    break;
            }
        }

        private void EnterJoining()
        {
            if (_joinSent)
            {
                TransitionTo(BotState.WaitingForMatch);
                return;
            }

            Console.WriteLine("[Bot] Sending join request...");
            _rpcSender.SendJoinRoom();
            _joinSent = true;
            TransitionTo(BotState.WaitingForMatch);
        }

        private void RequestSpawn()
        {
            // TODO: choose spawn point based on NextSpawnPointIndex or map data.
            var spawnPos = Vector3.Zero;
            _rpcSender.SendSpawnRequest(_rpcSender.LocalActorId, spawnPos);
            Console.WriteLine($"[Bot] Spawn requested at {spawnPos}");
        }

        private bool HasEngageableEnemy(out PlayerState? enemy)
        {
            enemy = _worldState.FindNearestEnemy(_config.TeamId, Vector3.Zero, _config.EnemyStaleTimeout);
            if (enemy == null)
            {
                return false;
            }

            var dist = Vector3.Distance(enemy.Position, Vector3.Zero);
            return dist <= _config.EngageDistanceMeters;
        }

        private void TransitionTo(BotState next)
        {
            if (_state == next)
            {
                return;
            }

            Console.WriteLine($"[Bot] State {_state} -> {next}");
            _state = next;
        }

        private enum BotState
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
