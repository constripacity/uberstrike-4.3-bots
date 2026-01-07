# UberStrike 4.3 Bots

**A reverse-engineering and automation project for UberStrike 4.3.**

![Status](https://img.shields.io/badge/Status-Phase%201%3A%20Offline-blue)
![Unity](https://img.shields.io/badge/Unity-2017.4.40f1-black)
![.NET](https://img.shields.io/badge/.NET-3.5-purple)

## 🎯 Project Focus

This repository is dedicated to creating autonomous bots for UberStrike 4.3. 

**Current Status: Phase 1 (Offline Mode)**
We are currently focusing on **Client-Side Injection**. Our bots run *inside* the game process, hooking into the Unity engine to control the local player character in Offline Practice mode.

**Future Goal: Phase 2 (Server Emulation)**
The long-term goal is to build a custom server emulator to enable online multiplayer matches between bots without relying on the defunct official servers.

## 📂 Repository Structure

```plaintext
uberstrike-4.3-bots/
├── ARCHITECTURE.md          # Detailed architecture analysis
├── ROADMAP.md               # Development goals
├── UnityIntegration/        # The Injection System (Phase 1)
│   ├── BotInjector.cs       # DLL Injection entry point
│   ├── BotController.cs     # Main bot logic script
│   └── ...
├── ServerEmulator/          # Server Emulation Logic (Phase 2 - WIP)
│   ├── Protocol/            # Network protocol definitions
│   └── GameLogic/           # Authoritative server rules
├── Research/                # Analysis tools and scripts
│   └── NetworkAnalyzer.cs   # Tools for packet inspection
└── docs/                    # Documentation and findings
```

## 🚀 Getting Started (Phase 1)

### Requirements
*   **UberStrike 4.3 Client**: You must have the game client installed.
*   **Mono Injector**: A tool like [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector) (or similar) to inject our DLL into the game process.
*   **Visual Studio**: To build the `UnityIntegration` DLL.

### Installation
1.  Clone this repository.
2.  Open the `UnityIntegration` project in Visual Studio.
3.  Add references to `UnityEngine.dll` and `Assembly-CSharp.dll` from your UberStrike game folder (`UberStrike_Data/Managed/`).
4.  Build the solution to generate `UberStrikeBots.dll`.

### Usage
1.  Start UberStrike 4.3 and enter **Offline Practice Mode**.
2.  Use your injector to inject `UberStrikeBots.dll` into `UberStrike.exe`.
    *   **Namespace**: `UberStrikeBots`
    *   **Class**: `BotLoader`
    *   **Method**: `Init`
3.  The bot should take control (watch the console/log for "Bot Injected").

## ⚠️ Disclaimer
This project is for educational and research purposes only. It is not intended for use in public multiplayer matches (if any still exist) or to disrupt the experience of others.

## 🤝 Contributing
See `ROADMAP.md` for current tasks. We welcome contributions in:
*   Reverse engineering the network protocol.
*   Improving bot behavior (A*, combat logic).
*   Updating documentation.
