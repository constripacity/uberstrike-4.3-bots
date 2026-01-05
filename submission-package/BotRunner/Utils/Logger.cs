using System;
using BotRunner.Config;

namespace BotRunner.Utils
{
    public enum LogLevel
    {
        Error = 0,
        Warn = 1,
        Info = 2,
        Debug = 3,
        Trace = 4
    }

    public static class Logger
    {
        private static LogLevel _level = LogLevel.Info;
        private static bool _quiet = false;

        public static void Configure(LoggingSettings settings, string? envOverride = null)
        {
            if (settings == null) return;
            _quiet = settings.Quiet;
            var selectedLevel = (envOverride ?? settings.LogLevel)?.ToLowerInvariant();
            _level = selectedLevel switch
            {
                "error" => LogLevel.Error,
                "warn" => LogLevel.Warn,
                "info" => LogLevel.Info,
                "debug" => LogLevel.Debug,
                "trace" => LogLevel.Trace,
                _ => LogLevel.Info
            };
        }

        private static void Write(LogLevel msgLevel, string prefix, string message)
        {
            if (_quiet && msgLevel != LogLevel.Error) return;
            if (msgLevel > _level) return;
            Console.WriteLine($"{prefix} {message}");
        }

        public static void Error(string message) => Write(LogLevel.Error, "[Error]", message);
        public static void Warn(string message) => Write(LogLevel.Warn, "[Warn]", message);
        public static void Info(string message) => Write(LogLevel.Info, "[Info]", message);
        public static void Debug(string message) => Write(LogLevel.Debug, "[Debug]", message);
        public static void Trace(string message) => Write(LogLevel.Trace, "[Trace]", message);
    }
}
