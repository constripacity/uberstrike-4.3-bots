Option A (recommended): “Make logs manageable without changing behavior”

Add a LogLevel and Quiet option to config and wire it into logging.
Constraints:
- Keep tick rates unchanged (restore to original 50/20 if you modified them)
- No external logging frameworks required (Console is fine)
- Implement a small static Logger helper with levels: Error / Warn / Info / Debug
- Add config binding for Logging.LogLevel (one of: Error, Warn, Info, Debug) and Logging.Quiet (bool)
- Logger.Configure(settings.Logging) should set the active level and Quiet flag
- Replace spammy per-tick logs (PositionUpdate send + transport send) with Debug level so they only appear when LogLevel=Debug
- Keep all behavioral semantics identical (no functional changes beyond logging verbosity)

Deliverables:
- Diffs showing code changes (prefer single replace_in_file per file with concise SEARCH/REPLACE blocks)
- Updated README section “Logging” describing the new config keys and recommended local overrides (mention appsettings.Local.json)
- Ensure default appsettings.json preserves original tick rates and defaults to LogLevel=Info, Quiet=false
