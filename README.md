# UberStrike 4.3 Bots

**A reverse-engineering and automation project for UberStrike 4.3.**

![Status](https://img.shields.io/badge/Status-Phase%201%3A%20Offline-blue) ![Version](https://img.shields.io/badge/Version-Phase%201.0-blue) ![Unity](https://img.shields.io/badge/Unity-2017.4.40f1-black) ![.NET](https://img.shields.io/badge/.NET-3.5-purple) ![License](https://img.shields.io/badge/License-MIT-green)

## 🎯 Project Focus

This repository is dedicated to creating autonomous bots for UberStrike 4.3, a classic Unity-based FPS game.

### **Current Status: Phase 1 (Offline Mode)**
We are focusing on **Client-Side Injection**. Our bots run *inside* the game process, hooking into the Unity engine to control player characters in **Offline Practice Mode**. This approach allows for:
- Testing AI behaviors without server dependencies
- Educational understanding of Unity game hooking
- Safe, isolated bot development

### **Future Goal: Phase 2 (Server Emulation)**
The long-term vision is to build a custom server emulator to enable online multiplayer matches between bots, bypassing the defunct official servers.

## 📂 Repository Structure
uberstrike-4.3-bots/
├── ARCHITECTURE.md # Detailed technical architecture analysis  
├── ROADMAP.md # Development goals and timeline  
├── UnityIntegration/ # Phase 1: Client-Side Injection  
│   ├── BotInjector.cs # DLL injection and game hooking  
│   ├── BotController.cs # Main AI logic with perception/decision layers  
│   ├── PracticeModeDetector.cs # Safety: detects offline mode  
│   ├── LocalSimulationManager.cs # Client-side game simulation  
│   └── README.md # Detailed injection instructions  
├── ServerEmulator/ # Phase 2: Server Emulation (WIP)  
│   ├── Protocol/ # Network protocol reverse engineering  
│   └── GameLogic/ # Authoritative server rules  
├── Research/ # Analysis tools and findings  
│   ├── NetworkAnalyzer.cs # Protocol inspection tools  
│   └── ComponentScanner.cs # Unity component discovery  
├── docs/ # Documentation  
│   ├── legacy/ # Previous headless bot research  
│   ├── ValidationChecklist.md # Testing procedures  
│   └── UberStrike-Network-Analysis.md # Architecture findings  
└── BotRunner/ # Legacy: Headless bot runner (for reference only)

## 🚀 Getting Started (Phase 1)

### **Prerequisites**
- **UberStrike 4.3 Client** installed  
- **.NET Framework 3.5** (for Unity 2017 compatibility)  
- **Unity 2017.4.40f1 DLL references** (from UberStrike_Data/Managed/)  
- **Mono injector** such as [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector)

### **Quick Start**
1. Clone the repository:
```bash
git clone https://github.com/constripacity/uberstrike-4.3-bots.git
cd uberstrike-4.3-bots/UnityIntegration
```

2. Compile the bot DLL (adjust Unity path as needed):
```bash
# Windows (cmd/powershell)
csc /target:library /out:UberStrikeBots.dll ^
    /reference:"C:\Program Files\Unity\Hub\Editor\2017.4.40f1\Editor\Data\Managed\UnityEngine.dll" ^
    *.cs
```

3. Inject and test:
- Launch UberStrike 4.3  
- Enter Practice Mode (any map)  
- Inject UberStrikeBots.dll using your injector (e.g., SharpMonoInjector)  
- Bots should spawn and begin autonomous behavior

Detailed instructions, debugging, and configuration are in:
- UnityIntegration/README.md - Injection guide  
- docs/ValidationChecklist.md - Testing procedures  
- ARCHITECTURE.md - Technical background

## 🔧 Features (Phase 1)

Current Implementation
- ✅ Game Hooking: DLL injection into Unity process  
- ✅ Practice Mode Detection: Auto-detects offline mode  
- ✅ Basic AI: Perception, decision, execution layers  
- ✅ Local Simulation: Client-side hit detection and damage  
- ✅ Debug Tools: Visual overlays and logging

In Development
- 🔄 Advanced Behaviors: Cover usage, weapon selection, team coordination  
- 🔄 Difficulty Scaling: Rookie to Veteran skill levels  
- 🔄 Performance Optimization: 8+ bot support

## 📖 Documentation
- ARCHITECTURE.md — Technical architecture and design decisions  
- ROADMAP.md — Development timeline and goals  
- docs/UberStrike-Network-Analysis.md — Network protocol analysis  
- docs/ValidationChecklist.md — Testing and verification procedures

## ⚠️ Important Notes

Disclaimer  
This project is for educational and research purposes only. It demonstrates:
- Unity game modification techniques  
- AI behavior development  
- Reverse engineering approaches

Not intended for:
- Public multiplayer match disruption  
- Cheating or unfair advantages  
- Any malicious purposes

Limitations
- Phase 1 only works in Practice/Offline mode  
- Online multiplayer requires Phase 2 (server emulation)  
- Performance varies by system configuration  
- Some UberStrike features may not be fully supported

## 🤝 Contributing
We welcome contributions. Current focus areas:
- Phase 1 Enhancements: Improved bot behaviors, performance optimizations, debugging tools  
- Phase 2 Research: Protocol reverse engineering, server emulation development, network security analysis  
- Documentation: Code docs, tutorials, architectural diagrams

See ROADMAP.md for tasks and CONTRIBUTING.md for contribution guidelines.

## 📜 License
MIT License — see LICENSE for details.

## 🔗 Related Projects
- Original UberStrike Client — Reference client code  
- UberServer — Server implementation attempts  
- SharpMonoInjector — DLL injection tool

## 🆘 Support
For issues or discussions:
- Check existing documentation and issues  
- Create a new issue with detailed information

This project is independently developed and not affiliated with the original UberStrike developers.