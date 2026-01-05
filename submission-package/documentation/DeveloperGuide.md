# Developer Guide

This project is optimized for deterministic, offline simulation. The runtime assembles five core layers:

1. **Transport** — `ITransportConnection` implementations (`MockTransportConnection` for offline) move bytes.  
2. **Router** — `RpcRouter` translates Photon event codes into strongly typed handlers and updates shared state.  
3. **State** — `WorldState` and `MatchState` hold player snapshots and match lifecycle data.  
4. **AI** — `BotBrain` builds a `BehaviorContext`, selects a utility behavior, and generates combat intents.  
5. **Pipeline** — `ActionPipeline` resolves movement/combat conflicts and emits coherent frames.

## Wiring and control loop

`Program.cs` constructs the transport, router, state containers, and the bot brain using the shared `SimulationTime` source:

```csharp
var runMetrics = new RunMetrics(() => SimulationTime.Instance.Elapsed);
var botBrain = new BotBrain(worldState, matchState, rpcSender, settings.Bot, settings.Room, runMetrics, scenarioConfig.Seed);
var rpcRouter = new RpcRouter(worldState, matchState, rpcMapping);
rpcRouter.Register(transport);
await transport.ConnectAsync(cts.Token);
```

Each simulation tick pumps the transport, flushes incoming RPCs, runs `BotBrain.Tick()`, and advances the deterministic clock:

```csharp
if (currentTick >= nextNetworkTick) { transport.Service(); rpcRouter.FlushIncoming(); }
if (currentTick >= nextBotTick) { botBrain.Tick(); }
simTime.Advance();
```

### Behavior selection snapshot

The bot’s decision context flows through the utility selector and metrics pipeline:

```csharp
var context = new BehaviorContext(
    _currentPosition,
    self,
    target,
    target != null ? Vector3.Distance(target.Position, _currentPosition) : float.PositiveInfinity,
    now - _fsmStateEnteredUtc,
    _activeBehaviorName,
    now,
    isEngagingState: _state == BotFsmState.Engaging,
    healthRatio: _combatIntentGenerator.Simulator.GetBotState().HealthRatio,
    ammoRatio: _combatIntentGenerator.Simulator.GetBotState().GetCurrentWeapon()?.AmmoRatio ?? 1f,
    enemyCount: visibleEnemies.Count,
    nearbyEnemiesCount: nearbyEnemies,
    nearbyAlliesCount: nearbyAllies,
    isOutnumbered: isOutnumbered);

var decision = _utility.Select(context);
_metrics?.RecordBehaviorSpread(decision.Scores);
_metrics?.RecordBehaviorDecision(decision.Behavior.Name, decision.Switched, decision.Reason);
```

This context is fully driven by `SimulationTime.Instance.Now`, guaranteeing consistent state ages and timers per seed.

## Running and validating

Run any scenario deterministically by pinning the seed:

```bash
dotnet run --project BotRunner -- --scenario flipping_regression --seed 777 --quiet
```

Determinism check (three identical MD5 sums expected):

```bash
for i in {1..3}; do
  dotnet run --project BotRunner -- --scenario flipping_regression --seed 777 --quiet
  md5sum run-summary.json >> checksums.txt
done
```

The `run-summary.json` file contains only simulation-derived values (`TotalSimulationTicks`, tick-based state/behavior durations, and `ChecksumMd5`).

## Extending the system

- **New scenarios:** Implement `IScenario` with `AdvanceTicks`-driven `ScenarioStep`s. The `ScenarioRunner` pumps transport, router, bot brain, and advances `SimulationTime` for each tick.  
- **New metrics:** Use `RunMetrics` hooks such as `RecordBehaviorSpread`, `RecordPipelineConflict`, and `RecordActionFrame` to keep additions deterministic.  
- **Transport integration:** Leave Photon stubs untouched (e.g., `Photon3TransportConnection.cs`), and continue to feed state exclusively through `RpcRouter` and the mock transport for offline tests.

Keep all timing logic bound to `SimulationTime` and ensure every `Random` instance is seeded from the scenario configuration to preserve byte-identical outputs.
