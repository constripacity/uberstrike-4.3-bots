using System;
using System.Collections.Generic;
using System.Numerics;
using BotRunner.Bot;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;
using BotRunner.Utils;

namespace BotRunner.Scenarios
{
    public class MovingTargetScenario : IScenario
    {
        public string Name => "moving_target";
        
        private MockTransportConnection _transport = null!;
        private RpcMapping _rpcMapping = null!;
        private PlayerStub _enemy = null!;

        public void Initialize(MockTransportConnection transport, int seed, WorldState worldState, MatchState matchState, BotConfig botConfig, int botActorId)
        {
            _transport = transport;
            _rpcMapping = RpcMapping.Default();
            
            // Create enemy that moves in predictable pattern
            _enemy = new PlayerStub(
                1001,
                "MovingTarget",
                (byte)(botConfig.TeamId == 0 ? 1 : 0),
                true,
                new Vector3(20, 0, 0),
                100,
                100,
                new Vector3(0, 0, 5) // Initial velocity
            );
            
            // ... standard initialization
            transport.Inject(new NetEvent(_rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
            transport.Inject(new NetEvent(_rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], new PlayerStub[] { _enemy }, -1));
            transport.Inject(new NetEvent(_rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(0, 0, 0), 0 }, -1));
        }
        
        public IEnumerable<ScenarioStep> GetSteps()
        {
            // Move enemy in predictable sine wave
            float time = 0f;
            for (int i = 0; i < 100; i++)
            {
                yield return new ScenarioStep
                {
                    Delay = TimeSpan.FromMilliseconds(100),
                    Action = () =>
                    {
                        time += 0.1f;
                        
                        var newPosition = new Vector3(
                            20 + MathF.Sin(time) * 10f, // Side-to-side movement
                            0,
                            time * 2f // Forward movement
                        );
                        
                        InjectPositionUpdate(_enemy.ActorId, newPosition, (int)SimulationTime.Instance.CurrentTick);
                    }
                };
            }
        }

        private void InjectPositionUpdate(int actorId, Vector3 position, int timestamp)
        {
            var sv = ShortVector3.FromVector(position);
            var batch = new byte[1 + 1 * 11]; // 1 entry in batch
            batch[0] = 1; // count
            
            var idx = 1;
            batch[idx] = (byte)actorId;
            BitConverter.GetBytes(timestamp).CopyTo(batch, idx + 1);
            BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
            BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
            BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);

            var ev = new NetEvent(
                _rpcMapping.RpcNameToId["FpsGameRPC.PositionUpdate"],
                batch,
                -1);

            _transport.Inject(ev);
        }
    }
}