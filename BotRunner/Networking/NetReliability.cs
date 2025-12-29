namespace BotRunner.Networking
{
    /// <summary>
    /// Explicit reliability flag so callers can mirror the retail client's reliable vs. unreliable sends.
    /// </summary>
    public enum NetReliability
    {
        Reliable,
        Unreliable
    }
}
