using System;

namespace BotRunner.Config
{
    /// <summary>
    /// Serializable configuration for the bot runner. This mirrors the values a real
    /// UberStrike client would pass when connecting to Photon and joining a game room.
    /// </summary>
    public class AppSettings
    {
        public ServerSettings Server { get; set; } = new();
        public RoomSettings Room { get; set; } = new();
        public BotSettings Bot { get; set; } = new();
        public string PhotonAppId { get; set; } = "TODO_PHOTON_APP_ID"; // TODO: fill with actual application id from the live client
    }

    public class ServerSettings
    {
        public string Endpoint { get; set; } = "127.0.0.1:5055"; // Typical Photon UDP endpoint
        public string Region { get; set; } = "us";
    }

    public class RoomSettings
    {
        public string RoomName { get; set; } = "TestRoom";
        public int ExpectedPlayerCount { get; set; } = 8;
        public int NetworkTickRateHz { get; set; } = 50; // Photon pump target, mirrors client service rate (~20ms)
        public int BotLogicTickRateHz { get; set; } = 20; // Game logic tick
    }

    public class BotSettings
    {
        public string Name { get; set; } = "[BOT] Alpha";
        public int Cmid { get; set; } = 999999; // TODO: use a valid CMID from authentication flow
        public int AccessLevel { get; set; } = 0; // Normal player access
        public BotConfig Config { get; set; } = new();
    }
}
