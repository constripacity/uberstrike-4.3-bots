# UberStrike 4.3 Bot Framework

**✅ Phase 4 Complete: Fully Autonomous AI Combatants with Visual & Combat Integration**

A bot framework focusing on UberStrike 4.3's client-side architecture.

This project aims to restore bot functionality for the UberStrike 4.3 client through a dual-mode approach:
1.  **Headless Simulation**: For deterministic AI training (BotRunner).
2.  **In-Game Injection**: ✅ **COMPLETE** - Autonomous bots in Practice Mode with full 3D models, weapons, and combat.

> **Note**: This framework currently targets **Offline/Practice modes**. It does not yet implement a full authoritative server for online play.

![Status](https://img.shields.io/badge/Status-Phase%204%20Complete-success)
![Unity](https://img.shields.io/badge/Unity-2017.4.40f1-black)
![.NET](https://img.shields.io/badge/.NET-10%20%26%203.5-purple)
![Architecture](https://img.shields.io/badge/Architecture-Dual%20Mode%3A%20Headless%20%2B%20In%2DGame-green)

## 🎯 Project Overview

This repository provides a **dual-mode bot development platform** for UberStrike 4.3:

### **Mode 1: Headless Bot Runner** ✅ **ACTIVE & IMPLEMENTED**
A standalone .NET application that simulates UberStrike's Photon RPC surface for **offline AI experimentation**. This mode provides:
- **Deterministic simulation** for reproducible AI testing
- **20+ behavioral scenarios** for comprehensive validation
- **Utility AI framework** with sophisticated decision making
- **Complete isolation** from actual game binaries

### **Mode 2: In-Game Injection** ✅ **PHASE 4 COMPLETE**
Direct Unity engine integration via DLL injection for **actual game control** in offline practice modes:
- ✅ **Full 3D Character Models** - Complete armor, skins, animations
- ✅ **Weapon Systems** - Randomized loadouts (Sniper/MG/Shotgun)
- ✅ **Autonomous Combat** - Bots track, aim, and shoot independently
- ✅ **Physics Integration** - Proper gravity, collisions, navigation
- ✅ **DamageForwarder System** - Hit detection on all body parts
- ✅ **Animation Sync** - Natural movement and aiming behaviors

### **Mode 3: Server Emulation** 📅 **FUTURE PHASE**
Custom authoritative server implementation to enable **online multiplayer bot matches**.

---

## 📂 Repository Structure

```
uberstrike-4.3-bots/
├── BotRunner/                      # MODE 1: Headless Bot Framework (ACTIVE)
│   ├── BotRunner.csproj            # .NET 10 project (deterministic simulation)
│   ├── Program.cs                  # Application entry and runtime loop
│   ├── Bot/                        # Core bot intelligence components
│   │   ├── BotBrain.cs             # State machine and orchestration
│   │   ├── BotCombat.cs            # Combat behavior helpers
│   │   ├── AI/                     # Utility AI selection system
│   │   │   ├── BehaviorContext.cs
│   │   │   ├── IUtilityBehavior.cs
│   │   │   ├── UtilityAISelector.cs
│   │   │   └── UtilityBehaviors.cs
│   │   ├── BotConfig.cs            # Parameter configuration
│   │   ├── BotMovement.cs          # Navigation and movement
│   │   └── Behaviors/              # Pluggable behavior implementations
│   ├── Scenarios/                  # 20+ test scenarios
│   │   └── ScenarioRunner.cs       # Scenario execution engine
│   ├── State/                      # Game state models
│   └── Utils/                      # Shared utilities
│
├── UnityIntegration/               # MODE 2: In-Game Injection (PHASE 1)
│   ├── BotInjector.cs              # DLL injection entry point
│   ├── BotController.cs            # Main AI with perception/decision layers
│   ├── PracticeModeDetector.cs     # Safety: offline mode detection
│   ├── LocalSimulationManager.cs   # Client-side game logic
│   └── README.md                   # Injection guide
│
├── ServerEmulator/                 # MODE 3: Server Emulation (FUTURE)
│   ├── Protocol/                   # Network protocol reverse engineering
│   └── GameLogic/                  # Authoritative server rules
│
├── Research/                       # Analysis tools
│   ├── NetworkAnalyzer.cs          # Protocol inspection
│   └── ComponentScanner.cs         # Unity component discovery
│
├── Extras/vision_demo/             # Optional: Computer vision research
├── docs/                           # Documentation
│   ├── ARCHITECTURE.md             # Technical architecture
│   ├── ROADMAP.md                  # Development timeline
│   ├── SCENARIOS.md                # Scenario catalog (20+)
│   ├── ValidationChecklist.md      # Testing procedures
│   └── PROJECT_TREE.md             # Complete file structure
│
├── scripts/                        # Validation and benchmarking
└── LICENSE                         # MIT License
```

---

## 🚀 Getting Started

### **Choose Your Development Mode:**

#### **Option A: Headless Bot Runner (Recommended for AI Research)**

```bash
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
```

#### **Option B: In-Game Injection (For Live Game Integration)**

```bash
# 1. Navigate to injection module
cd UnityIntegration

# 2. Compile DLL (adjust Unity path)
csc /target:library /out:UberStrikeBots.dll ^
    /reference:"C:\Program Files\Unity\Hub\Editor\2017.4.40f1\Editor\Data\Managed\UnityEngine.dll" ^
    *.cs

# 3. Inject into UberStrike Practice Mode
# Use SharpMonoInjector or similar tool
```

---

## 🔬 Features by Mode

### Mode 1: Headless Bot Runner (Complete)

- ✅ **Deterministic Simulation**: Same seed = identical outcomes every time
- ✅ **20+ Behavioral Scenarios**:
  - `duel` - 1v1 combat testing
  - `swarm` - Multi-enemy survival
  - `retreat` - Disengage decision testing
  - `weapon_test` - Range-based weapon selection
  - `team_duel` - Multi-bot coordination
  - `regression_suite` - Comprehensive validation bundle
- ✅ **Utility AI Framework**: Sophisticated decision making with hysteresis
- ✅ **Performance Benchmarking**: Scripted validation suites
- ✅ **Vision System Integration**: Optional computer vision pipeline
- ✅ **Cross-Platform**: Windows, Linux, macOS support

### Mode 2: In-Game Injection (Phase 4 Complete)

- ✅ **Full Character Models**: RemoteCharacter prefab with armor/skins
- ✅ **Weapon Attachment**: Randomized loadouts from player arsenal
- ✅ **Autonomous AI**: Patrol, chase, combat decision-making
- ✅ **DamageForwarder**: Hit detection across all body colliders
- ✅ **Animation Sync**: LateUpdate() forces AvatarDecorator positioning
- ✅ **Practice Mode Safety**: Auto-detects offline environment
- ✅ **Debug Tools**: F1 spawn, F3 HUD, F9 probe, F12 toggle

### Mode 3: Server Emulation (Future)

- 📅 **Protocol Reverse Engineering**: Photon transport layer
- 📅 **Authoritative Server**: Game rule enforcement
- 📅 **Multiplayer Support**: Online bot matches

---

## 📊 Scenario Catalog

The framework includes 20+ scenarios for comprehensive AI validation:

### 🤖 AI Behavior Testing
- `duel` - 1v1 at varying distances
- `swarm` - Survival against multiple enemies
- `retreat` - Disengage decisions under pressure
- `flipping_test` - Engage threshold oscillation testing

### 🎯 Combat Proficiency
- `weapon_test` - Range-based weapon switching
- `moving_target` - Velocity-based aim prediction
- `shoot_window_test` - Firing interval consistency
- `ammo_pressure` - Resource management under fire

### 👥 Team Coordination
- `team_duel` - Multi-bot focus fire and positioning
- `spawn_wave` - Wave survival mechanics

### ⚡ Stress & Performance
- `many_actors` - 10+ actor stress testing
- `load_spike_test` - Rapid position update bursts
- `loop` - Lifecycle reset validation

### 🛡️ Failure & Recovery
- `bad_payload` - Malformed RPC handling
- `reorder_drop` - Packet loss simulation
- `state_integrity_test` - State transition validation

### 📈 Validation Suites
- `regression_suite` - Comprehensive test bundle
- `deterministic_suite` - Fixed-step validation

**Full catalog**: See `docs/SCENARIOS.md`

---

## 🧪 Determinism & Validation

The headless framework guarantees logical determinism:

### Determinism Checklist
- ✅ **Time Source**: `SimulationTime.Instance` only (no wall-clock APIs)
- ✅ **Randomness**: All `Random` instances seeded from configuration
- ✅ **Metrics**: `RunMetrics` uses simulation ticks exclusively
- ✅ **Checksum**: Identical seeds produce identical `ChecksumMd5`

### Validation Scripts

**Windows:**
```powershell
.\scripts\final-validation.ps1      # Full validation suite
.\scripts\validate-determinism.ps1  # Determinism check
.\scripts\benchmark.ps1             # Performance benchmark
```

**Linux/macOS:**
```bash
./scripts/final-validation.sh
./scripts/validate-determinism.sh
./scripts/benchmark.sh
```

Validation compares `ChecksumMd5` in `run-summary.json`, ignoring wall-clock performance fields.

---

## 🎮 In-Game Injection Details

### Requirements
- UberStrike 4.3 Client installed
- .NET Framework 3.5 (Unity 2017 compatibility)
- Mono Injector (SharpMonoInjector recommended)

### Quick Injection Test
1. Launch UberStrike 4.3
2. Enter Practice Mode (any map)
3. Inject `UberStrikeBots.dll`
4. Bots spawn with autonomous behavior

### Safety Features
- Auto-detects practice mode only
- Graceful failure if wrong mode detected
- No online multiplayer interference
- Comprehensive error logging

---

## 🔧 Advanced Features

### Optional Vision System

```bash
# Computer vision for enemy detection
pip install -r Extras/vision_demo/requirements.txt
python Extras/vision_demo/vision_system/test_vision.py
```

### Custom Scenario Development

```csharp
// Create custom scenarios in BotRunner/Scenarios/
public class MyCustomScenario : IScenario {
    public void Execute(BotBrain bot) {
        // Custom bot behavior logic
    }
}
```

### Configuration & Tuning

```json
// BotConfig.json
{
    "ReactionTime": 0.25,
    "Accuracy": 0.75,
    "Aggression": 0.6,
    "MovementStyle": "Tactical"
}
```

---

## ⚠️ Important Notes

### Project Intent
This is a reference implementation and research platform designed to:
- Demonstrate UberStrike 4.3 client architecture
- Provide deterministic AI experimentation environment
- Enable safe bot behavior development
- Serve as educational resource for game reverse engineering

### Usage Guidelines

**✅ Permitted:**
- Offline AI research and development
- Educational study of game architecture
- Private server experimentation (with authorization)
- Academic and research purposes

**❌ Not Permitted:**
- Public server disruption or cheating
- Unauthorized multiplayer interference
- Malicious or disruptive applications
- Commercial exploitation without permission

### Technical Limitations
- **Headless Mode**: No visual feedback, simulation only
- **Injection Mode**: Practice mode only (no online)
- **Server Emulation**: Future development phase
- **Performance**: Varies by system configuration

---

## 🤝 Contributing

We welcome contributions in several areas:

### Mode 1 Enhancements
- New behavioral scenarios
- Improved utility AI algorithms
- Additional validation tests
- Performance optimizations

### Mode 2 Development
- Enhanced in-game behaviors
- Better practice mode integration
- Additional debugging tools
- Configuration system improvements

### Research & Documentation
- Protocol reverse engineering
- Architectural documentation
- Tutorials and guides
- Performance analysis

See `ROADMAP.md` for detailed tasks and development timeline.

---

## 📚 Documentation

| Document | Description |
|----------|-------------|
| `ARCHITECTURE.md` | Technical architecture and design decisions |
| `ROADMAP.md` | Development timeline and goals |
| `SCENARIOS.md` | Complete scenario catalog (20+) |
| `PROJECT_TREE.md` | Complete file structure reference |
| `ValidationChecklist.md` | Testing and verification procedures |

---

## 📜 License

MIT License - See `LICENSE` for full details.

**Disclaimer**: This project is independently developed and not affiliated with the original UberStrike developers or publishers.

---

## 🔗 Related Resources

- [Original UberStrike Client](https://github.com/festivaldev/UberStrike-Reverse-Engineered) - Reference implementation
- [UberServer Attempt](https://github.com/Paradise-SH/uberstrike-server) - Server implementation research
- [SharpMonoInjector](https://github.com/warbler/SharpMonoInjector) - Recommended injection tool
- [Photon Engine](https://www.photonengine.com/) - Underlying networking technology

---

## 🆘 Support & Community

For questions, issues, or discussions:
1. Check existing documentation
2. Review open/closed issues
3. Create new issue with detailed context
4. Follow project guidelines and disclaimer

**Note**: This is a research-focused project. Commercial support is not available.

---
## Credits

Constripacity – for founding this project and architecting the Bot Development
