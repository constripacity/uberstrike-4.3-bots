# Adding a New Utility Behavior

Utility behaviors implement `IUtilityBehavior` and plug into the `UtilityAISelector` list built in `BotRunner/Bot/BotBrain.cs`. Every behavior receives a `BehaviorContext` populated from deterministic `SimulationTime`.

## 1. Implement the behavior

Create a new class under `BotRunner/Bot/Behaviors/` that implements the `IUtilityBehavior` contract:

```csharp
using BotRunner.Bot.AI;
using BotRunner.State;
using System.Numerics;

public class SniperBehavior : IUtilityBehavior
{
    public string Name => "Sniper";

    public float Score(BehaviorContext ctx)
    {
        if (ctx.NearestEnemy == null) return 0f;
        // Prefer long shots when health and ammo are healthy
        var distanceScore = ctx.DistanceToEnemy > 20f ? 1f : 0.2f;
        var stability = ctx.IsEngagingState ? 0.3f : 0.6f;
        return (distanceScore + stability) * (ctx.AmmoRatio ?? 1f);
    }

    public MovementIntent GetIntent(BehaviorContext ctx)
    {
        // Hold position and aim directly at the enemy
        return ctx.NearestEnemy != null
            ? new MovementIntent(ctx.NearestEnemy.Position)
            : MovementIntent.None;
    }
}
```

`BehaviorContext` already exposes distance, ammo/health ratios, nearby ally counts, and a deterministic `NowUtc`.

## 2. Register it in the selector

`BotBrain` wires the selector in its constructor. Add the new behavior to the list in `BotRunner/Bot/BotBrain.cs`:

```csharp
_utility = new UtilityAISelector(
    new IUtilityBehavior[]
    {
        new UtilityWanderBehavior(_wanderBehavior),
        // ...
        new UtilityStrafeBehavior(new StrafeBehavior(2f, rootSeed ^ 0x4), preferredMin, strafeMax),
        new UtilityHoldBehavior(_holdBehavior, preferredMin, preferredMax),
        new UtilityCoverBehavior(new CoverBehavior()),
        new SniperBehavior() // new behavior
    },
    stickinessBonus: util.StickinessBonus,
    minHold: TimeSpan.FromMilliseconds(util.MinHoldMilliseconds),
    overrideDelta: util.OverrideDelta,
    noiseSeed: rootSeed ^ 0x5,
    noiseAmplitude: util.NoiseAmplitude);
```

The selector will automatically:
- Apply stickiness and minimum hold times
- Add deterministic noise using `noiseSeed`
- Record spread and switch reasons via `RunMetrics.RecordBehaviorSpread` and `RecordBehaviorDecision`

## 3. Validate deterministically

Run a seeded scenario to exercise the new behavior without wall-clock drift:

```bash
dotnet run --project BotRunner -- --scenario duel --seed 4242 --quiet
```

Inspect `run-summary.json` to confirm the new behavior appears under `BehaviorTicks` and `SwitchReasons`. Re-running with the same seed should produce an identical `ChecksumMd5` and `md5sum run-summary.json`.
