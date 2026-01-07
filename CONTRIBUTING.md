# Contributing

Thank you for wanting to contribute. This document gives practical, minimal guidance for working on this reverse-engineering research project.

## Development setup
1. Fork the repository and clone your fork:
   ```bash
   git clone https://github.com/<your-user>/uberstrike-4.3-bots.git
   ```
2. Open the solution in Visual Studio / VS Code.
3. Requirements:
   - .NET SDK (for tools and BotRunner development) — use the version targeted by projects (check csproj).
   - Unity 2017.4.x managed assemblies when building UnityIntegration DLLs (copy from UberStrike_Data/Managed).
4. Building the Unity injection DLL:
   - Create a Class Library that references the game’s UnityEngine assemblies and UnityIntegration/*.cs.
   - Compile to `UberStrikeBot.dll` (see README.md for recommended csc command).
5. Running/injecting:
   - Use a mono injector (SharpMonoInjector) to load `UberStrikeBot.dll` into the running UberStrike client. See UnityIntegration/README.md for exact injector parameters.

## Code style
- Language: C# for runtime/integration code; keep projects idiomatic to .NET/C#.
- Formatting:
  - Use consistent indentation (4 spaces).
  - Use PascalCase for types and methods, camelCase for locals and parameters.
  - Keep short, focused commits with descriptive messages (imperative tense).
- Tests and examples should be documented in docs/ or Config/Examples/.

## Testing requirements
- Ensure builds succeed for modified projects:
  - dotnet build for .NET projects (BotRunner).
  - When editing UnityIntegration, confirm the compiled DLL loads in the target Unity version used by the game.
- Use BotTestingHarness and logs for debugging injected behavior.
- When adding features that affect determinism or scenarios, run relevant deterministic scenarios in BotRunner (see docs/ScenariosAndDeterminism.md).

## Pull request process
1. Branch from main: git checkout -b feat/short-description
2. Make small, focused commits.
3. Run build and local checks.
4. Open a Pull Request against the main repository:
   - Describe the change, why, and testing performed.
   - Reference issues if applicable.
5. PR reviewers may request changes — address them in new commits.
6. Squash or rebase as requested by maintainers before merge.

## Issue reporting template
When filing an issue, include:
- Title: short summary
- Environment:
  - OS, Unity version, .NET SDK
  - UberStrike client version
- Steps to reproduce (precise)
- Expected behavior vs actual behavior
- Logs / console output (attach if large)
- Reproduction package or minimal repro steps (if possible)
- Screenshots or short recordings (if helpful)

Minimal issue example:
```
Title: BotInjector fails to find local player on map X

Environment:
- Windows 10
- UberStrike 4.3 client
- Unity 2017.4.40f1

Steps:
1. Launch UberStrike
2. Enter Practice Mode on map X
3. Inject UberStrikeBot.dll with SharpMonoInjector (UberStrikeBot.BotInjector.Load)

Observed:
- Unity console shows no "[BotInjector] Found player" messages

Expected:
- BotInjector finds player and injects BotController

Logs: attach Player.log
```

## Responsible research & legal note
This project is explicitly for research, education, and offline experimentation. Do not use these tools to interfere with live multiplayer services or enable cheating. Respect applicable laws and terms of service. See LICENSE for licensing details.

## Contact & discussions
- Use Issues for bugs and design discussions.
- For larger architecture proposals, open an issue referencing ARCHITECTURE.md and ROADMAP.md.