using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.Networking.Payload;
using BotRunner.State;

namespace BotRunner.Scenarios
{
    public static class ScenarioRunner
    {
        public static void Run(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            var scenario = (config.ScenarioName ?? "demo").ToLowerInvariant();
            BotRunner.Utils.Logger.Info($"[Scenario] Starting {scenario} with seed={config.Seed} enemyCount={config.EnemyCount}");
            _ = scenario switch
            {
                "duel" => RunDuel(mock, mapping, config, botActorId),
                "respawn_loop" => RunRespawnLoop(mock, mapping, config, botActorId),
                _ => RunDemo(mock, mapping, config, botActorId)
            };
        }

        private static Task RunDemo(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 1, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart");

                await Task.Delay(durations.PlayerListMs);
                var players = BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(10, 0, 10));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                BotRunner.Utils.Logger.Info($"[Scenario] Injected FullPlayerListUpdate (bot + {config.EnemyCount} enemies)");

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(10, 0, 10), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected SpawnAllowed for bot");

                await Task.Delay(durations.PositionUpdateMs);
                InjectEnemyBatch(mock, mapping, rng, config.EnemyCount, 20f, new Vector3(20, 0, 20));
            });
        }

        private static Task RunDuel(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart (duel)");

                await Task.Delay(durations.PlayerListMs);
                var players = BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(8, 0, 8));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected FullPlayerListUpdate (duel)");

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(8, 0, 8), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected SpawnAllowed for bot");

                for (var wave = 0; wave < 4; wave++)
                {
                    await Task.Delay(durations.PositionUpdateMs);
                    InjectEnemyBatch(mock, mapping, rng, config.EnemyCount, 15f + wave * 2, new Vector3(15 + wave, 0, 15 + wave));
                }
            });
        }

        private static Task RunRespawnLoop(MockTransportConnection mock, RpcMapping mapping, ScenarioConfig config, int botActorId)
        {
            return Task.Run(async () =>
            {
                var rng = new Random(config.Seed);
                var durations = config.Durations ?? new ScenarioDurations();

                await Task.Delay(durations.MatchStartMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 3, 999999 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected MatchStart (respawn loop)");

                await Task.Delay(durations.PlayerListMs);
                var players = BuildPlayers(config.EnemyCount, rng, botActorId, new Vector3(5, 0, 5));
                mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));

                await Task.Delay(durations.SpawnMs);
                mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, new Vector3(5, 0, 5), 0 }, -1));
                BotRunner.Utils.Logger.Info("[Scenario] Injected initial spawn for bot");

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    await Task.Delay(durations.RespawnLoopMs);
                    var death = new[] { new PlayerStub(botActorId, "[BOT] Respawn", 0, false, Vector3.Zero) };
                    mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], death, botActorId));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: marked bot as dead");

                    await Task.Delay(durations.SpawnMs);
                    var respawnPos = new Vector3(5 + cycle * 2, 0, 5 + cycle * 2);
                    mock.Inject(new NetEvent(mapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { botActorId, respawnPos, 1 }, -1));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: respawn allowed at {respawnPos}");
                    var aliveAgain = new[] { new PlayerStub(botActorId, "[BOT] Respawn", 0, true, respawnPos) };
                    mock.Inject(new NetEvent(mapping.RpcNameToId["GameRPC.DeltaPlayerListUpdate"], aliveAgain, botActorId));
                    BotRunner.Utils.Logger.Info($"[Scenario] Cycle {cycle + 1}: marked bot alive at {respawnPos}");
                }

                await Task.Delay(durations.PositionUpdateMs);
                InjectEnemyBatch(mock, mapping, rng, config.EnemyCount, 12f, new Vector3(12, 0, 12));
            });
        }

        private static PlayerStub[] BuildPlayers(int enemyCount, Random rng, int botActorId, Vector3 botSpawn)
        {
            var players = new List<PlayerStub> { new(botActorId, "[BOT] Alpha", 0, true, botSpawn) };
            for (var i = 0; i < enemyCount; i++)
            {
                var offset = RandomOffset(rng, 10f, 24f);
                players.Add(new PlayerStub(i + 2, $"Enemy_{i + 1}", 1, true, botSpawn + offset));
            }
            return players.ToArray();
        }

        private static Vector3 RandomOffset(Random rng, float minRadius, float maxRadius)
        {
            var angle = rng.NextDouble() * Math.PI * 2;
            var radius = minRadius + rng.NextDouble() * (maxRadius - minRadius);
            return new Vector3(
                (float)(Math.Cos(angle) * radius),
                0f,
                (float)(Math.Sin(angle) * radius));
        }

        private static void InjectEnemyBatch(MockTransportConnection mock, RpcMapping mapping, Random rng, int enemyCount, float radius, Vector3 center)
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
    }
}
