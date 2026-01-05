using System;

namespace BotRunner.Utils
{
    /// <summary>
    /// Deterministic time source for offline simulation.
    /// Ticks advance with each logic update, not wall clock.
    /// </summary>
    public class SimulationTime
    {
        private long _currentTick = 0;
        private readonly float _tickDurationMs;
        private readonly DateTime _simulationStart;
        
        public DateTime Now => _simulationStart.AddMilliseconds(_currentTick * _tickDurationMs);
        public long CurrentTick => _currentTick;
        
        public SimulationTime(float tickDurationMs = 16.667f) // 60Hz default
        {
            _tickDurationMs = tickDurationMs;
            _simulationStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        
        public void Advance() => _currentTick++;
        public void AdvanceBy(long ticks) => _currentTick += ticks;
        
        public void Reset()
        {
            _currentTick = 0;
        }
        
        public static SimulationTime Instance { get; } = new SimulationTime();
    }
}