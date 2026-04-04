/// <summary>
/// Extension point for developer debug tools (god mode, no hits, etc.).
/// The default implementation (NoOpDebugOverrides) does nothing — all gameplay is normal.
/// Authorized team members can swap in a real implementation via a private submodule.
/// </summary>
public interface IDebugOverrides
{
    /// <summary>When true, the local player takes no damage from any source.</summary>
    bool ShouldBlockDamage { get; }

    /// <summary>When true, bot weapons skip damage to the local player.</summary>
    bool ShouldBlockBotDamageToPlayer { get; }

    /// <summary>When true, LevelBoundary and DeathArea don't kill the player.</summary>
    bool ShouldBlockEnvironmentKill { get; }
}

/// <summary>
/// Default implementation — all debug features disabled. Normal gameplay.
/// This is the only implementation in the public repository.
/// </summary>
public class NoOpDebugOverrides : IDebugOverrides
{
    public static readonly NoOpDebugOverrides Instance = new NoOpDebugOverrides();

    public bool ShouldBlockDamage => false;
    public bool ShouldBlockBotDamageToPlayer => false;
    public bool ShouldBlockEnvironmentKill => false;
}

/// <summary>
/// Global registry for debug overrides. Defaults to NoOp.
/// Private submodule can call DebugOverrideRegistry.Set() to activate real overrides.
/// </summary>
public static class DebugOverrideRegistry
{
    private static IDebugOverrides _current = NoOpDebugOverrides.Instance;

    public static IDebugOverrides Current => _current;

    public static void Set(IDebugOverrides overrides)
    {
        _current = overrides ?? NoOpDebugOverrides.Instance;
    }
}
