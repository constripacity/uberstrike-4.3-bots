# Scenarios and Determinism

All scenarios are fixed-step and advance `SimulationTime` explicitly. No `Task.Delay` or wall-clock polling is used in deterministic paths.

## Fixed-step runner

`ScenarioRunner` drives each `ScenarioStep` and advances the simulation for a specific number of ticks:

```csharp
foreach (var step in scenario.GetSteps())
{
    step.Action();
    var ticks = step.AdvanceTicks > 0 ? step.AdvanceTicks : SimulationTime.Instance.ToTicks(step.Delay);
    AdvanceSimulation(transport, router, botBrain, ticks);
}
```

`AdvanceSimulation` pumps the mock transport, routes RPCs, ticks the bot brain, and then increments `SimulationTime`.

## Writing deterministic scenarios

Use `ScenarioStep.AdvanceTicks` to control time progression. Example (`FlippingRegressionScenario`):

```csharp
var intervalTicks = ScenarioUtils.TicksFromMs(Math.Max(75, _durations.PositionUpdateMs / 2));
foreach (var offset in flipOffsets)
{
    yield return new ScenarioStep { AdvanceTicks = intervalTicks };
    yield return Inject(() =>
    {
        var pos = new Vector3(spawn.X + engageThreshold + offset, spawn.Y, spawn.Z);
        // ... construct payload ...
        _transport!.Inject(new NetEvent(_mapping!.RpcNameToId["FpsGameRPC.PositionUpdate"], batch, -1));
    });
}
```

The runner executes the inject step, services the transport/router/bot for `intervalTicks`, and moves the simulation clock forward by the same amount.

## Seed management

- Set `ScenarioConfig.Seed` via CLI (`--seed 777`) or `Config/appsettings.json`.  
- Every `Random` instance is created with an explicit seed (default `1` if none provided).  
- `SimulationTime` starts at a fixed epoch (`2024-01-01T00:00:00Z`) and advances by `TickDurationMs` (default 16.667 ms) per tick.

## Regression and stress suites

- **Regression:** `regression_suite` runs `bad_payload`, `reorder_drop`, `duel`, `swarm`, `retreat`, and `load_spike` in order.  
- **Deterministic suite:** `deterministic_suite` runs `flipping_test`, `swarm_retreat_test`, `load_spike_test`, and `state_integrity_test`.  
- **Stress:** `many_actors` simulates 10+ actors moving independently to test frame stability without wall-clock drift.

Run a suite with:

```bash
dotnet run --project BotRunner -- --scenario regression_suite --seed 777 --quiet
```

## Verifying byte-identical outputs

`run-summary.json` is produced from simulation ticks only and includes `ChecksumMd5`. Re-running with the same seed should give identical file hashes:

```bash
dotnet run --project BotRunner -- --scenario flipping_regression --seed 777 --quiet
md5sum run-summary.json
```

If hashes diverge, check the determinism checklist in `README.md` (SimulationTime-only usage, seeded `Random`, fixed-step scenarios, and absence of delays).
