using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
            var photonConnection = new PhotonConnection(settings.Server, settings.PhotonAppId);
            var rpcMapping = RpcMapping.Default();
            var rpcSender = new RpcSender(photonConnection, rpcMapping, settings.Bot.Name);
            var rpcRouter = new RpcRouter(worldState, matchState);
            var rateLimiter = new RateLimiter(TimeSpan.FromMilliseconds(20)); // Networking pump ~50Hz
            var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room);

            await photonConnection.ConnectAsync(cts.Token);
            rpcRouter.Register(photonConnection);

            Console.WriteLine("[Lifecycle] Bot initialized. Entering main loop...");

            while (!cts.Token.IsCancellationRequested)
            {
                photonConnection.Update();
                rpcRouter.FlushIncoming();
                botBrain.Tick();

                rateLimiter.SleepUntilNext();
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
