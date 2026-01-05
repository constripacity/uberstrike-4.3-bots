using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BotRunner.Bot;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Scenarios;
using System.Diagnostics;

namespace BotRunner
{
    /// <summary>
    /// Entry point for the headless UberStrike bot runner.
    /// Responsible for bootstrapping configuration, wiring dependencies, and
    /// driving the core update loops for networking and bot behavior.
    /// </summary>
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                BotRunner.Utils.Logger.Info("[Lifecycle] Ctrl+C detected, shutting down bot...");
                cts.Cancel();
                eventArgs.Cancel = true;
            };

            var settings = LoadSettings();
            var envLogLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
            BotRunner.Utils.Logger.Configure(settings.Logging, envLogLevel);

            if (args.Any(a => a.Equals("--list-scenarios", StringComparison.OrdinalIgnoreCase) || a.Equals("-l", StringComparison.OrdinalIgnoreCase)))
            {
                var scenarios = ScenarioRunner.GetRegisteredScenarios();
                Console.WriteLine("Available Scenarios:");
                foreach (var scenario in scenarios.OrderBy(s => s))
                {
                    Console.WriteLine($"  - {scenario}");
                }
                return;
            }

            var scenarioConfig = settings.Scenario ?? new ScenarioConfig();
            var scenarioOverride = GetScenario(args);
            if (!string.IsNullOrWhiteSpace(scenarioOverride))
            {
                scenarioConfig.ScenarioName = scenarioOverride;
            }
            var seedOverride = GetSeed(args);
            if (seedOverride.HasValue)
            {
                scenarioConfig.Seed = seedOverride.Value;
            }
            if (scenarioConfig.Seed <= 0)
            {
                scenarioConfig.Seed = 1;
            }
            var runScenario = !string.IsNullOrWhiteSpace(scenarioConfig.ScenarioName);
            var worldState = new WorldState();
            var matchState = new MatchState();
            var transport = TransportConnectionFactory.Create(settings);
            var rpcMapping = RpcMapping.Default();
            var rpcSender = new RpcSender(transport, rpcMapping, settings.Bot.Name);
            rpcSender.LocalActorId = settings.Bot.Cmid;
            var rpcRouter = new RpcRouter(worldState, matchState, rpcMapping);
            var runMetrics = new RunMetrics(() => SimulationTime.Instance.Elapsed);
            var wallClock = Stopwatch.StartNew();
            var gcStart = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room, runMetrics, scenarioConfig.Seed);

            rpcRouter.Register(transport);
            await transport.ConnectAsync(cts.Token);

            // If running with MockTransport and no offline scenario configured, inject a minimal
            // match so the bot will enter the main loop and the ActionPipeline can produce frames.
            if (transport is MockTransportConnection mockTransport && string.IsNullOrWhiteSpace(scenarioConfig.ScenarioName))
            {
                // Inject MatchStart, player list and spawn allowed for the bot using deterministic ticks
                mockTransport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.MatchStart"], new object[] { 2, 0 }, -1));
                var players = ScenarioUtils.BuildPlayers(scenarioConfig.EnemyCount, new Random(scenarioConfig.Seed), rpcSender.LocalActorId, new System.Numerics.Vector3(8, 0, 8));
                mockTransport.Inject(new NetEvent(rpcMapping.RpcNameToId["GameRPC.FullPlayerListUpdate"], players, -1));
                mockTransport.Inject(new NetEvent(rpcMapping.RpcNameToId["FpsGameRPC.SetNextSpawnPointForPlayer"], new object[] { rpcSender.LocalActorId, new System.Numerics.Vector3(8, 0, 8), 0 }, -1));
            }

            ScenarioRunSummary? suiteSummary = null;
            if (runScenario && scenarioConfig.ScenarioName != null)
            {
                if (transport is MockTransportConnection mock)
                {
                    rpcSender.LocalActorId = settings.Bot.Cmid;
                    suiteSummary = await ScenarioRunner.Run(mock, rpcMapping, scenarioConfig, rpcSender.LocalActorId, botBrain, worldState, matchState, settings.Bot.Config, rpcRouter);
                if (suiteSummary != null)
                {
                    BotRunner.Utils.Logger.Info($"[Scenario] Regression suite success={suiteSummary.Success}");
                    Environment.ExitCode = suiteSummary.Success ? 0 : 1;
                    // Write run summary immediately and exit so offline scenario runs terminate deterministically.
                    wallClock.Stop();
                    var gcEndScenario = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                    var gcDeltaScenario = new[]
                    {
                        gcEndScenario[0] - gcStart[0],
                        gcEndScenario[1] - gcStart[1],
                        gcEndScenario[2] - gcStart[2]
                    };
                    var peakMbScenario = Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d;
                    runMetrics.RecordPerformanceSnapshot(wallClock.Elapsed.TotalMilliseconds, peakMbScenario, gcDeltaScenario);
                    WriteRunSummary(runMetrics.Snapshot(), scenarioConfig);
                    BotRunner.Utils.Logger.Info("[Lifecycle] Exiting after offline scenario");
                    Environment.Exit(Environment.ExitCode);
                }
                }
                else
                {
                    BotRunner.Utils.Logger.Info("[Scenario] Offline scenarios require MockTransportConnection; skipping.");
                }
            }

            var simTime = SimulationTime.Instance;
            simTime.Reset();
            var networkIntervalTicks = Math.Max(1, (long)Math.Round((1000.0 / settings.Room.NetworkTickRateHz) / simTime.TickDurationMs));
            var botIntervalTicks = Math.Max(1, (long)Math.Round((1000.0 / settings.Room.BotLogicTickRateHz) / simTime.TickDurationMs));
            var nextNetworkTick = networkIntervalTicks;
            var nextBotTick = botIntervalTicks;

            BotRunner.Utils.Logger.Info("[Lifecycle] Bot initialized. Entering main loop...");
            BotRunner.Utils.Logger.Info($"[Lifecycle] Network tick: {settings.Room.NetworkTickRateHz} Hz, Bot tick: {settings.Room.BotLogicTickRateHz} Hz");

            while (!cts.Token.IsCancellationRequested)
            {
                var currentTick = simTime.CurrentTick;

                if (currentTick >= nextNetworkTick)
                {
                    // Photon pump - mirrors PhotonPeer.Service() cadence in the retail client (~50Hz).
                    transport.Service();
                    rpcRouter.FlushIncoming();
                    runMetrics.IncrementNetworkTick();
                    nextNetworkTick += networkIntervalTicks; // reduce drift under load
                }

                if (currentTick >= nextBotTick)
                {
                    // Game logic tick - intentionally slower to keep behavior human-like.
                    botBrain.Tick();
                    nextBotTick += botIntervalTicks; // reduce drift under load
                }

                simTime.Advance();
            }

            BotRunner.Utils.Logger.Info("[Lifecycle] Leaving room and shutting down...");
            try
            {
                rpcSender.SendLeaveRoom(rpcSender.LocalActorId);
            }
            catch (Exception ex)
            {
                BotRunner.Utils.Logger.Warn($"[Shutdown] Leave failed: {ex.Message}");
            }
            transport.Disconnect();
            wallClock.Stop();
            var gcEnd = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
            var gcDelta = new[]
            {
                gcEnd[0] - gcStart[0],
                gcEnd[1] - gcStart[1],
                gcEnd[2] - gcStart[2]
            };
            var peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1024d / 1024d;
            runMetrics.RecordPerformanceSnapshot(wallClock.Elapsed.TotalMilliseconds, peakMb, gcDelta);
            WriteRunSummary(runMetrics.Snapshot(), scenarioConfig);
        }

        private static AppSettings LoadSettings()
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "appsettings.json");
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Missing configuration file at {configPath}");
            }

            var json = File.ReadAllText(configPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });

            if (settings == null)
            {
                throw new InvalidOperationException("Failed to parse appsettings.json. Ensure the JSON matches AppSettings schema.");
            }

            return settings;
        }

        private static void WriteRunSummary(RunSummarySnapshot snapshot, ScenarioConfig scenarioConfig)
        {
            try
            {
                var summaryCore = new
                {
                    Scenario = scenarioConfig.ScenarioName,
                    Seed = scenarioConfig.Seed,
                    EnemyCount = scenarioConfig.EnemyCount,
                    snapshot.TotalSimulationTicks,
                    snapshot.StateTicks,
                    snapshot.StateEntries,
                    snapshot.PositionUpdatesSent,
                    TicksReceived = snapshot.NetworkTicksReceived,
                    snapshot.CurrentBehaviorName,
                    snapshot.BehaviorSwitches,
                    snapshot.BehaviorTicks,
                    snapshot.SwitchReasons,
                    snapshot.DecisionSpreadAvg,
                    snapshot.CloseCallRate,
                    snapshot.PipelineConflictCount,
                    snapshot.ValidationSummary,
                    snapshot.OscillationAlerts,
                    snapshot.MaxSwitchesPerSecond,
                    ActionPipeline = snapshot.ActionPipeline,
                    CombatEffectiveness = snapshot.CombatEffectiveness,
                    TeamMetrics = snapshot.TeamMetrics
                };
                var options = new JsonSerializerOptions { WriteIndented = true };
                var coreJson = JsonSerializer.Serialize(summaryCore, options);
                var checksum = snapshot.ValidationSummary?.Checksum ?? ComputeMd5(coreJson);
                var finalSummary = new
                {
                    ChecksumMd5 = checksum,
                    snapshot.StateSeconds,
                    summaryCore.StateTicks,
                    summaryCore.StateEntries,
                    summaryCore.PositionUpdatesSent,
                    summaryCore.TicksReceived,
                    summaryCore.CurrentBehaviorName,
                    summaryCore.BehaviorSwitches,
                    snapshot.BehaviorSeconds,
                    summaryCore.BehaviorTicks,
                    snapshot.BehaviorSwitchesPerMinute,
                    snapshot.SwitchesPerMinute,
                    summaryCore.SwitchReasons,
                    summaryCore.DecisionSpreadAvg,
                    summaryCore.CloseCallRate,
                    summaryCore.PipelineConflictCount,
                    snapshot.PerformanceMetrics,
                    summaryCore.ValidationSummary,
                    summaryCore.OscillationAlerts,
                    summaryCore.MaxSwitchesPerSecond,
                    summaryCore.ActionPipeline,
                    summaryCore.CombatEffectiveness,
                    summaryCore.TeamMetrics,
                    summaryCore.Scenario,
                    summaryCore.Seed,
                    summaryCore.EnemyCount,
                    summaryCore.TotalSimulationTicks,
                    snapshot.TotalRuntimeSeconds
                };
                var json = JsonSerializer.Serialize(finalSummary, options);
                var path = Path.Combine(Directory.GetCurrentDirectory(), "run-summary.json");
                File.WriteAllText(path, json);
                BotRunner.Utils.Logger.Info($"[Lifecycle] Run summary written to {path}");
                BotRunner.Utils.Logger.Info($"[Lifecycle] Summary -> scenario={summaryCore.Scenario}, states={summaryCore.StateEntries.Count}, positionUpdates={summaryCore.PositionUpdatesSent}");
            }
            catch (Exception ex)
            {
                BotRunner.Utils.Logger.Warn($"[Lifecycle] Failed to write run summary: {ex.Message}");
            }
        }

        private static string ComputeMd5(string input)
        {
            using var md5 = MD5.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input);
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private static string? GetScenario(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Equals("--scenario", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return args[i + 1];
                    }
                    return "demo";
                }

                if (a.StartsWith("--scenario=", StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring("--scenario=".Length);
                }
            }

            return null;
        }

        private static int? GetSeed(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a.Equals("--seed", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var value))
                    {
                        return value;
                    }
                }
                if (a.StartsWith("--seed=", StringComparison.OrdinalIgnoreCase) && int.TryParse(a.Substring("--seed=".Length), out var parsed))
                {
                    return parsed;
                }
            }

            return null;
        }
    }
}
