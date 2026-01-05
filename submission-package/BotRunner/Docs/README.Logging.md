Logging changes

This project now includes a minimal Console-based logging helper to control verbosity without changing runtime behavior.

New config (BotRunner/Config/appsettings.json):
"Logging": {
  "LogLevel": "Info", // One of: Error, Warn, Info, Debug
  "Quiet": false      // If true, suppresses non-error output
}

Usage:
- Default behavior is unchanged (LogLevel=Info).
- Set LogLevel to "Debug" to see per-tick transport and position update messages.
- Set Quiet to true to suppress Info/Debug logs for clean recordings.

Implementation notes:
- Logger.Configure(settings) is called early from Program.Main after settings are loaded.
- Spammy per-tick messages (PositionUpdate send, transport SendEvent) are moved to Debug level.
- No external logging libraries are used; Console only.
