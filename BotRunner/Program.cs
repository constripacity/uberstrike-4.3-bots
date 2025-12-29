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
            var photonConnection = new PhotonConnection(settings.Server.Endpoint, settings.PhotonAppId);
            var rpcMapping = RpcMapping.Default();
            var rpcSender = new RpcSender(photonConnection, rpcMapping, settings.Bot.Name);
            var rpcRouter = new RpcRouter(worldState, matchState);
            var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room);

            await photonConnection.ConnectAsync(cts.Token);
            rpcRouter.Register(photonConnection);

            var networkInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.NetworkTickRateHz);
            var botInterval = TimeSpan.FromMilliseconds(1000.0 / settings.Room.BotLogicTickRateHz);
            var stopwatch = Stopwatch.StartNew();
            var nextNetworkTick = TimeSpan.Zero;
            var nextBotTick = TimeSpan.Zero;

            Console.WriteLine("[Lifecycle] Bot initialized. Entering main loop...");
            Console.WriteLine($"[Lifecycle] Network tick: {settings.Room.NetworkTickRateHz} Hz, Bot tick: {settings.Room.BotLogicTickRateHz} Hz");

            while (!cts.Token.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                if (now >= nextNetworkTick)
                {
                    // Photon pump - mirrors PhotonPeer.Service() cadence in the retail client (~50Hz).
                    photonConnection.Update();
                    rpcRouter.FlushIncoming();
                    nextNetworkTick += networkInterval;
                }

                if (now >= nextBotTick)
                {
                    // Game logic tick - intentionally slower to keep behavior human-like.
                    botBrain.Tick();
                    nextBotTick += botInterval;
                }

                // Sleep just enough to avoid a busy loop while keeping timing responsive.
                var nextTick = TimeSpan.FromTicks(Math.Min(nextNetworkTick.Ticks, nextBotTick.Ticks));
                var delay = nextTick - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    Thread.Sleep(delay);
                }
            }

            Console.WriteLine("[Lifecycle] Leaving room and shutting down...");
            rpcSender.SendLeaveRoom();
            photonConnection.Disconnect();
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
