# UberStrike 4.3 Bots

**A reverse-engineering and automation project for UberStrike 4.3.**

![Status](https://img.shields.io/badge/Status-Phase%201%3A%20Offline-blue) ![Version](https://img.shields.io/badge/Version-Phase%201.0-blue) ![Unity](https://img.shields.io/badge/Unity-2017.4.40f1-black)

## Project Focus

This repository is dedicated to creating autonomous bots for UberStrike 4.3, a classic Unity-based FPS game.

### Current Status: Phase 1 (Offline Mode)

We are focusing on **Client-Side Injection**. Our bots operate within the game process, hooking into the Unity engine to control player characters in **Offline Practice Mode**. 

This phase allows:
- Testing AI behaviors without server dependencies
- Educational understanding of Unity game hooking
- Safe, isolated bot development

### Future Goal: Phase 2 (Server Emulation)

The long-term vision is to build a custom server emulator to enable online multiplayer matches between bots, bypassing the defunct official servers.

---

## Repository Structure

```text
uberstrike-4.3-bots/
├── [ARCHITECTURE.md](ARCHITECTURE.md) # Detailed technical architecture analysis  
├── [ROADMAP.md](ROADMAP.md) # Development goals and timeline  
├── UnityIntegration/ # Phase 1: Client-Side Injection  
│   ├── [BotInjector.cs](UnityIntegration/BotInjector.cs) # DLL injection and game hooking  
│   ├── [BotController.cs](UnityIntegration/BotController.cs) # Main AI logic with perception/decision layers  
│   ├── [PracticeModeDetector.cs](UnityIntegration/PracticeModeDetector.cs) # Detects offline mode  
│   ├── [LocalSimulationManager.cs](UnityIntegration/LocalSimulationManager.cs) # Client-side game simulation  
│   ├── [README.md](UnityIntegration/README.md) # Detailed injection instructions  
├── ServerEmulator/ # Phase 2: Server Emulation (WIP)  
│   ├── [Protocol/](ServerEmulator/Protocol/) # Network protocol reverse engineering  
│   ├── [GameLogic/](ServerEmulator/GameLogic/) # Authoritative server rules  
├── Research/ # Analysis tools and findings  
│   ├── [NetworkAnalyzer.cs](Research/NetworkAnalyzer.cs) # Protocol inspection tools  
│   ├── [ComponentScanner.cs](Research/ComponentScanner.cs) # Unity component discovery  
├── docs/  
│   ├── [legacy/](docs/legacy/) # Previous headless bot research  
│   ├── [ValidationChecklist.md](docs/ValidationChecklist.md) # Testing procedures  
│   ├── [UberStrike-Network-Analysis.md](docs/UberStrike-Network-Analysis.md) # Architecture findings  
└── BotRunner/ # Legacy: Headless bot runner (for reference only)
```

*Note: The full project tree is listed under [Docs/PROJECT_TREE.md](docs/PROJECT_TREE.md) (low opacity color added).*

---

## Getting Started (Phase 1)

### Prerequisites
- **UberStrike 4.3 Client** (installed locally)
- **.NET Framework 3.5** (for Unity 2017 compatibility)
- **Unity 2017.4.40f1 DLL references** (located in UberStrike_Data/Managed/)
- **Mono injector** such as [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector)

### Quick Start

1. **Clone the repository**:
   ```bash
   git clone https://github.com/constripacity/uberstrike-4.3-bots.git
   cd uberstrike-4.3-bots/UnityIntegration
   ```

2. **Compile the bot DLL** (adjust Unity path as needed):
   ```bash
   # Windows (cmd or PowerShell)
   csc /target:library /out:UberStrikeBots.dll ^
       /reference:"C:\Program Files\Unity\Hub\Editor\2017.4.40f1\Editor\Data\Managed\UnityEngine.dll" ^ 
       *.cs
   ```

3. **Inject and test**:
   - Launch UberStrike 4.3 game executable
   - Enter **Practice Mode** (any map)
   - Inject `UberStrikeBots.dll` using your injector tool (e.g., SharpMonoInjector)
   - Observe bots spawning and performing automatic behaviors

### Documentation

Detailed instructions, debugging, and other useful guides:
- [UnityIntegration/README.md](UnityIntegration/README.md) - Injection guide
- [docs/ValidationChecklist.md](docs/ValidationChecklist.md) - Testing procedures
- [ARCHITECTURE.md](ARCHITECTURE.md) - Technical background document

---

## Phase 1 AI Bot Features

**Current Implementation:**
- Game Hooking: Inject bots into Unity process
- **Auto-Detection**: Detects Practice/Offline mode
- Basic AI Framework: Perception, decision-making, and execution layers
- Local Simulation of client-side logic including:
  - Hit detection
  - Physics-based interactions
  - Damage and logs

**Planned Enhancements:**
- Advanced Behaviors: Dynamic pathfinding, combat tactics, teamwork
- Difficulty Scaling: AI Rookie to Veteran bot profiles
- Optimization Goals: Performance (~10+ adaptive bots)

---

## Disclaimer & Limitations
This project serves **educational and research purposes only**, focusing on:
- AI development for Unity-based games
- Unity engine reverse engineering techniques

**Not allowed as use cases:**
- Cheating or disrupting public multiplayer matches.
- Any malicious/harmful purposes are strictly forbidden.

**Technical Limitations:**
- Works **Offline-only** in practicing mode.
- Some Uberstrike online API Extensions not yet implemented.
- Lags can occur if >= too complex e.g., high # bot).