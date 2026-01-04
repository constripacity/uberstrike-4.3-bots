using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using BotRunner.Bot;
using BotRunner.Config;
using BotRunner.Networking;
using BotRunner.State;
using BotRunner.Utils;
using BotRunner.Scenarios;

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
                scenarioConfig.Seed = Environment.TickCount & int.MaxValue;
            }
            var runScenario = !string.IsNullOrWhiteSpace(scenarioConfig.ScenarioName);
            var worldState = new WorldState();
            var matchState = new MatchState();
            var transport = TransportConnectionFactory.Create(settings);
            var rpcMapping = RpcMapping.Default();
            var rpcSender = new RpcSender(transport, rpcMapping, settings.Bot.Name);
            rpcSender.LocalActorId = settings.Bot.Cmid;
            var rpcRouter = new RpcRouter(worldState, matchState, rpcMapping);
            var stopwatch = Stopwatch.StartNew();
            var runMetrics = new RunMetrics(() => stopwatch.Elapsed);
            var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room, runMetrics, scenarioConfig.Seed);

            rpcRouter.Register(transport);
            await transport.ConnectAsync(cts.Token);

            if (runScenario && scenarioConfig.ScenarioName != null)
            {
                if (transport is MockTransportConnection mock)
                {
                    rpcSender.LocalActorId = settings.Bot.Cmid;
                    ScenarioRunner.Run(mock, rpcMapping, scenarioConfig, rpcSender.LocalActorId);
                }
                else
                {
                    BotRunner.Utils.Logger.Info("[Scenario] Offline scenarios require MockTransportConnection; skipping.");
                }
            }

            var networkInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.NetworkTickRateHz);
            var botInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.BotLogicTickRateHz);
            var nextNetworkTick = stopwatch.Elapsed + networkInterval;
            var nextBotTick = stopwatch.Elapsed + botInterval;

            BotRunner.Utils.Logger.Info("[Lifecycle] Bot initialized. Entering main loop...");
            BotRunner.Utils.Logger.Info($"[Lifecycle] Network tick: {settings.Room.NetworkTickRateHz} Hz, Bot tick: {settings.Room.BotLogicTickRateHz} Hz");

            while (!cts.Token.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                if (now >= nextNetworkTick)
                {
                    // Photon pump - mirrors PhotonPeer.Service() cadence in the retail client (~50Hz).
                    transport.Service();
                    rpcRouter.FlushIncoming();
                    runMetrics.IncrementNetworkTick();
                    nextNetworkTick = now + networkInterval; // reduce drift under load
                }

                if (now >= nextBotTick)
                {
                    // Game logic tick - intentionally slower to keep behavior human-like.
                    botBrain.Tick();
                    nextBotTick = now + botInterval; // reduce drift under load
                }

                // Sleep just enough to avoid a busy loop while keeping timing responsive.
                var nextTick = nextNetworkTick < nextBotTick ? nextNetworkTick : nextBotTick;
                var delay = nextTick - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(delay, cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                }
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
            WriteRunSummary(runMetrics.Snapshot(), scenarioConfig, stopwatch.Elapsed);
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

        private static void WriteRunSummary(RunSummarySnapshot snapshot, ScenarioConfig scenarioConfig, TimeSpan elapsed)
        {
            try
            {
                var summary = new
                {
                    Scenario = scenarioConfig.ScenarioName,
                    Seed = scenarioConfig.Seed,
                    EnemyCount = scenarioConfig.EnemyCount,
                    TotalRuntimeSeconds = Math.Round(elapsed.TotalSeconds, 3),
                    snapshot.StateSeconds,
                    snapshot.StateEntries,
                    snapshot.PositionUpdatesSent,
                    TicksReceived = snapshot.NetworkTicksReceived,
                    snapshot.BehaviorSwitches,
                    snapshot.CurrentBehaviorName,
                    snapshot.CombatIntentsGenerated,
                    snapshot.CombatShouldShoot
                };
                var json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
                var path = Path.Combine(AppContext.BaseDirectory, "run-summary.json");
                File.WriteAllText(path, json);
                BotRunner.Utils.Logger.Info($"[Lifecycle] Run summary written to {path}");
                BotRunner.Utils.Logger.Info($"[Lifecycle] Summary -> scenario={summary.Scenario}, runtime={summary.TotalRuntimeSeconds}s, states={summary.StateEntries.Count}, positionUpdates={summary.PositionUpdatesSent}");
            }
            catch (Exception ex)
            {
                BotRunner.Utils.Logger.Warn($"[Lifecycle] Failed to write run summary: {ex.Message}");
            }
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
