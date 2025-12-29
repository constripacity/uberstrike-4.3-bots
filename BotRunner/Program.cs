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
using System.Linq;

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
                Console.WriteLine("[Lifecycle] Ctrl+C detected, shutting down bot...");
                cts.Cancel();
                eventArgs.Cancel = true;
            };

            var settings = LoadSettings();
            var worldState = new WorldState();
            var matchState = new MatchState();
            var transport = TransportConnectionFactory.Create(settings);
            var rpcMapping = RpcMapping.Default();
            var rpcSender = new RpcSender(transport, rpcMapping, settings.Bot.Name);
            rpcSender.LocalActorId = settings.Bot.Cmid;
            var rpcRouter = new RpcRouter(worldState, matchState, rpcMapping);
            var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room);

            rpcRouter.Register(transport);
            await transport.ConnectAsync(cts.Token);

            var runScenario = args.Any(a => string.Equals(a, "--scenario", StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(a, "--scenario=demo", StringComparison.OrdinalIgnoreCase));
            if (runScenario)
            {
                ScenarioRunner.RunDemoScenario(transport, rpcMapping);
            }

            var networkInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.NetworkTickRateHz);
            var botInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.BotLogicTickRateHz);
            var stopwatch = Stopwatch.StartNew();
            var nextNetworkTick = stopwatch.Elapsed + networkInterval;
            var nextBotTick = stopwatch.Elapsed + botInterval;

            Console.WriteLine("[Lifecycle] Bot initialized. Entering main loop...");
            Console.WriteLine($"[Lifecycle] Network tick: {settings.Room.NetworkTickRateHz} Hz, Bot tick: {settings.Room.BotLogicTickRateHz} Hz");

            while (!cts.Token.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                if (now >= nextNetworkTick)
                {
                    // Photon pump - mirrors PhotonPeer.Service() cadence in the retail client (~50Hz).
                    transport.Service();
                    rpcRouter.FlushIncoming();
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

            Console.WriteLine("[Lifecycle] Leaving room and shutting down...");
            try
            {
                rpcSender.SendLeaveRoom(rpcSender.LocalActorId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Shutdown] Leave failed: {ex.Message}");
            }
            transport.Disconnect();
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
    }
}
