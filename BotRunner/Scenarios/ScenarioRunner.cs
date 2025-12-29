using System;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;

namespace BotRunner.Scenarios
{
    public static class ScenarioRunner
    {
        public static void RunDemoScenario(MockTransportConnection mock, RpcMapping mapping)
        {
            _ = Task.Run(async () =>
            {
                Console.WriteLine("[Scenario] Starting demo sequence...");

                await Task.Delay(100);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
                Console.WriteLine("[Scenario] Injected MatchStart");

                await Task.Delay(100);
                var players = new[]
                {
                    new PlayerStub(5, "[BOT] Alpha", 0, true, Vector3.Zero),
                    new PlayerStub(2, "Enemy", 1, true, new Vector3(20, 0, 20))
                };
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                Console.WriteLine("[Scenario] Injected FullPlayerListUpdate (bot + enemy)");

                await Task.Delay(100);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { 5, new Vector3(10, 0, 10), 0 }, -1));
                Console.WriteLine("[Scenario] Injected SpawnAllowed for bot");

                await Task.Delay(200);
                // Build server batch position update for enemy.
                var enemyPos = ShortVector3.FromVector(new Vector3(20, 0, 20));
                var batch = new byte[12];
                batch[0] = 1; // count
                batch[1] = 2; // actorId byte
                BitConverter.GetBytes(12345).CopyTo(batch, 2); // timestamp
                BitConverter.GetBytes(enemyPos.X).CopyTo(batch, 6);
                BitConverter.GetBytes(enemyPos.Y).CopyTo(batch, 8);
                BitConverter.GetBytes(enemyPos.Z).CopyTo(batch, 10);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                Console.WriteLine("[Scenario] Injected PositionUpdate batch for enemy");
            });
        }
    }
}
