# UberStrike 4.3 Bot Development Platform

**A comprehensive bot framework for UberStrike 4.3 with dual-mode architecture**

![Status](https://img.shields.io/badge/Status-Multi%20Mode%20Active-blue) ![Unity](https://img.shields.io/badge/Unity-2017.4.40f1-black) ![.NET](https://img.shields.io/badge/.NET-10%20%26%203.5-purple) ![Architecture](https://img.shields.io/badge/Architecture-Dual%20Mode%3A%20Headless%20%2B%20In%2DGame-green)

## 🎯 Project Overview

This repository provides a **dual-mode bot development platform** for UberStrike 4.3:

### **Mode 1: Headless Bot Runner** ✅ **ACTIVE & IMPLEMENTED**
A standalone .NET application that simulates UberStrike's Photon RPC surface for **offline AI experimentation**. This mode provides:
- **Deterministic simulation** for reproducible AI testing
- **20+ behavioral scenarios** for comprehensive validation
- **Utility AI framework** with sophisticated decision making
- **Complete isolation** from actual game binaries

### **Mode 2: In-Game Injection** 🚧 **PHASE 1 ACTIVE**
Direct Unity engine integration via DLL injection for **actual game control** in offline practice modes:
- **Live game hooking** via Unity component manipulation
- **Practice mode detection** and safety mechanisms
- **Local simulation** for client-side game logic
- **Real visual feedback** in actual UberStrike client

### **Mode 3: Server Emulation** 📅 **PHASE 2 FUTURE**
Custom authoritative server implementation to enable **online multiplayer bot matches**.

---

## 📂 Repository Structure
```
uberstrike-4.3-bots/
├── BotRunner/ # MODE 1: Headless Bot Framework (ACTIVE)
│   ├── BotRunner.csproj # .NET 10 project (deterministic simulation)
│   ├── Program.cs # Application entry and runtime loop
│   ├── Bot/ # Core bot intelligence components
│   │   ├── BotBrain.cs # State machine and orchestration
│   │   ├── BotCombat.cs # Combat behavior helpers
│   │   ├── AI/ # Utility AI selection system
│   │   │   ├── BehaviorContext.cs
│   │   │   ├── IUtilityBehavior.cs
│   │   │   ├── UtilityAISelector.cs
│   │   │   └── UtilityBehaviors.cs
│   │   ├── BotConfig.cs # Parameter configuration
│   │   ├── BotMovement.cs # Navigation and movement
│   │   └── Behaviors/ # Pluggable behavior implementations
│   ├── Scenarios/ # 20+ test scenarios
│   │   └── ScenarioRunner.cs # Scenario execution engine
│   ├── State/ # Game state models
│   └── Utils/ # Shared utilities
│
├── UnityIntegration/ # MODE 2: In-Game Injection (PHASE 1)
│   ├── BotInjector.cs # DLL injection entry point
│   ├── BotController.cs # Main AI with perception/decision layers
│   ├── PracticeModeDetector.cs # Safety: offline mode detection
│   ├── LocalSimulationManager.cs # Client-side game logic
│   └── README.md # Injection guide
│
├── ServerEmulator/ # MODE 3: Server Emulation (FUTURE)
│   ├── Protocol/ # Network protocol reverse engineering
│   └── GameLogic/ # Authoritative server rules
│
├── Research/ # Analysis tools
│   ├── NetworkAnalyzer.cs # Protocol inspection
│   └── ComponentScanner.cs # Unity component discovery
│
├── Extras/vision_demo/ # Optional: Computer vision research
├── docs/ # Documentation
│   ├── ARCHITECTURE.md # Technical architecture
│   ├── ROADMAP.md # Development timeline
│   ├── SCENARIOS.md # Scenario catalog (20+)
│   ├── ValidationChecklist.md # Testing procedures
│   └── PROJECT_TREE.md # Complete file structure
│
├── scripts/ # Validation and benchmarking
└── LICENSE # MIT License
```

---

## 🚀 Getting Started

### **Choose Your Development Mode:**

#### **Option A: Headless Bot Runner (Recommended for AI Research)**
```
# 1. Clone and build
git clone https://github.com/constripacity/uberstrike-4.3-bots.git
cd uberstrike-4.3-bots
dotnet build

# 2. List available scenarios
dotnet run --project BotRunner -- --list-scenarios

# 3. Run a scenario
dotnet run --project BotRunner -- --scenario duel
dotnet run --project BotRunner -- --scenario swarm
dotnet run --project BotRunner -- --scenario regression_suite
Option B: In-Game Injection (For Live Game Integration)
bash
# 1. Navigate to injection module
cd UnityIntegration

# 2. Compile DLL (adjust Unity path)
csc /target:library /out:UberStrikeBots.dll ^
    /reference:"C:\Program Files\Unity\Hub\Editor\2017.4.40f1\Editor\Data\Managed\UnityEngine.dll" ^
    *.cs

# 3. Inject into UberStrike Practice Mode
# Use SharpMonoInjector or similar tool
🔬 Features by Mode
Mode 1: Headless Bot Runner (Complete)
✅ Deterministic Simulation: Same seed = identical outcomes every time
✅ 20+ Behavioral Scenarios:

duel - 1v1 combat testing

swarm - Multi-enemy survival

retreat - Disengage decision testing

weapon_test - Range-based weapon selection

team_duel - Multi-bot coordination

regression_suite - Comprehensive validation bundle

✅ Utility AI Framework: Sophisticated decision making with hysteresis
✅ Performance Benchmarking: Scripted validation suites
✅ Vision System Integration: Optional computer vision pipeline
✅ Cross-Platform: Windows, Linux, macOS support

Mode 2: In-Game Injection (Phase 1 Active)
✅ Game Hooking: DLL injection into Unity process
✅ Practice Mode Detection: Auto-detects offline environment
✅ Basic AI Layers: Perception, decision, execution
✅ Local Simulation: Client-side hit detection
✅ Debug Tools: Visual overlays and logging

Mode 3: Server Emulation (Future)
📅 Protocol Reverse Engineering: Photon transport layer
📅 Authoritative Server: Game rule enforcement
📅 Multiplayer Support: Online bot matches...

...Additional details clipped for brevity...