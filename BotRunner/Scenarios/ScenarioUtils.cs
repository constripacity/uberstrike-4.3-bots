using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;

namespace BotRunner.Scenarios
{
    internal static class ScenarioUtils
    {
        public static PlayerStub[] BuildPlayers(int enemyCount, Random rng, int botActorId, Vector3 botSpawn)
        {
            var players = new List<PlayerStub> { new(botActorId, "[BOT] Alpha", 0, true, botSpawn) };
            for (var i = 0; i < enemyCount; i++)
            {
                var offset = RandomOffset(rng, 10f, 24f);
                players.Add(new PlayerStub(i + 2, $"Enemy_{i + 1}", 1, true, botSpawn + offset));
            }
            return players.ToArray();
        }

        public static Vector3 RandomOffset(Random rng, float minRadius, float maxRadius)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var radius = minRadius + rng.NextDouble() * (maxRadius - minRadius);
            return new Vector3(
                (float)(Math.Cos(angle) * radius),
                0f,
                (float)(Math.Sin(angle) * radius));
        }

        public static void InjectEnemyBatch(MockTransportConnection mock, RpcMapping mapping, Random rng, int enemyCount, float radius, Vector3 center)
        {
            if (enemyCount <= 0)
            {
                BotRunner.Utils.Logger.Info("[Scenario] No enemies configured; skipping PositionUpdate batch");
                return;
            }

            var entries = enemyCount;
            var batch = new byte[1 + entries * 11];
            batch[0] = (byte)entries;
            var idx = 1;
            for (var i = 0; i < entries; i++)
            {
                var enemyId = (byte)(i + 2);
                var position = center + RandomOffset(rng, radius * 0.5f, radius);
                var sv = ShortVector3.FromVector(position);
                var timestamp = 10000 + rng.Next(0, 1000) + i * 100;
                BitConverter.GetBytes(timestamp).CopyTo(batch, idx + 1);
                BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                batch[idx] = enemyId;
                idx += 11;
            }

            mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
            BotRunner.Utils.Logger.Info($"[Scenario] Injected PositionUpdate batch entries={entries}");
        }

        public static async Task InjectDeterministicPath(MockTransportConnection mock, RpcMapping mapping, int enemyCount, int cadenceMs)
        {
            if (enemyCount <= 0)
            {
                return;
            }

            var path = new[]
            {
                new Vector3(12, 0, 12),
                new Vector3(16, 0, 12),
                new Vector3(16, 0, 16),
                new Vector3(12, 0, 16),
                new Vector3(10, 0, 14),
                new Vector3(14, 0, 10)
            };

            var timestamp = 20000;
            for (var step = 0; step < path.Length; step++)
            {
                await Task.Delay(cadenceMs);
                var batch = new byte[1 + enemyCount * 11];
                batch[0] = (byte)enemyCount;
                var idx = 1;
                for (var enemy = 0; enemy < enemyCount; enemy++)
                {
                    var pos = path[(step + enemy) % path.Length] + new Vector3(enemy * 0.5f, 0, enemy * 0.5f);
                    var sv = ShortVector3.FromVector(pos);
                    batch[idx] = (byte)(enemy + 2);
                    BitConverter.GetBytes(timestamp).CopyTo(batch, idx + 1);
                    BitConverter.GetBytes(sv.X).CopyTo(batch, idx + 5);
                    BitConverter.GetBytes(sv.Y).CopyTo(batch, idx + 7);
                    BitConverter.GetBytes(sv.Z).CopyTo(batch, idx + 9);
                    idx += 11;
                    timestamp += 33;
                }
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
                BotRunner.Utils.Logger.Info($"[Scenario] Injected deterministic duel path step {step + 1}/{path.Length}");
            }
        }
    }
}
