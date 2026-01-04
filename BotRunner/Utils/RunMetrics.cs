using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BotRunner.Utils
{
    /// <summary>
    /// Collects lightweight runtime metrics for offline runs so they can be emitted as a JSON summary.
    /// </summary>
    public class RunMetrics
    {
        private readonly Func<TimeSpan> _elapsedProvider;
        private readonly object _lock = new();
        private readonly Dictionary<string, TimeSpan> _stateDurations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _stateEntries = new(StringComparer.OrdinalIgnoreCase);
        private string _currentState = "Uninitialized";
        private TimeSpan _stateEnteredAt;
        private int _positionUpdatesSent;
        private int _networkTicksReceived;
        private int _behaviorSwitches;
        private string _currentBehaviorName = string.Empty;
        private int _combatIntentsGenerated;
        private int _combatShouldShoot;

        public RunMetrics(Func<TimeSpan> elapsedProvider)
        {
            _elapsedProvider = elapsedProvider;
            _stateEnteredAt = _elapsedProvider();
        }

        public void EnterState(string state)
        {
            lock (_lock)
            {
                AddDurationForCurrent();
                _currentState = state;
                _stateEnteredAt = _elapsedProvider();
                if (_stateEntries.ContainsKey(state))
                {
                    _stateEntries[state]++;
                }
                else
                {
                    _stateEntries[state] = 1;
                }
            }
        }

        public void IncrementPositionUpdatesSent()
        {
            Interlocked.Increment(ref _positionUpdatesSent);
        }

        public void IncrementNetworkTick()
        {
            Interlocked.Increment(ref _networkTicksReceived);
        }

        public RunSummarySnapshot Snapshot()
        {
            lock (_lock)
            {
                AddDurationForCurrent();
                return new RunSummarySnapshot
                {
                    StateSeconds = _stateDurations.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value.TotalSeconds, 3)),
                    StateEntries = new Dictionary<string, int>(_stateEntries, StringComparer.OrdinalIgnoreCase),
                    PositionUpdatesSent = _positionUpdatesSent,
                    NetworkTicksReceived = _networkTicksReceived,
                    TotalRuntimeSeconds = Math.Round(_elapsedProvider().TotalSeconds, 3),
                    BehaviorSwitches = _behaviorSwitches,
                    CurrentBehaviorName = _currentBehaviorName,
                    CombatIntentsGenerated = _combatIntentsGenerated,
                    CombatShouldShoot = _combatShouldShoot
                };
            }
        }

        public void RecordBehaviorSwitch(string behaviorName)
        {
            Interlocked.Increment(ref _behaviorSwitches);
            SetCurrentBehavior(behaviorName);
        }

        public void SetCurrentBehavior(string behaviorName)
        {
            lock (_lock)
            {
                _currentBehaviorName = behaviorName;
            }
        }

        public void RecordCombatIntent(bool shouldShoot)
        {
            Interlocked.Increment(ref _combatIntentsGenerated);
            if (shouldShoot)
            {
                Interlocked.Increment(ref _combatShouldShoot);
            }
        }

        private void AddDurationForCurrent()
        {
            var now = _elapsedProvider();
            var delta = now - _stateEnteredAt;
            if (delta < TimeSpan.Zero || string.IsNullOrEmpty(_currentState))
            {
                return;
            }

            if (_stateDurations.TryGetValue(_currentState, out var existing))
            {
                _stateDurations[_currentState] = existing + delta;
            }
            else
            {
                _stateDurations[_currentState] = delta;
            }
        }
    }

    public class RunSummarySnapshot
    {
        public Dictionary<string, double> StateSeconds { get; set; } = new();
        public Dictionary<string, int> StateEntries { get; set; } = new();
        public int PositionUpdatesSent { get; set; }
        public int NetworkTicksReceived { get; set; }
        public double TotalRuntimeSeconds { get; set; }
        public int BehaviorSwitches { get; set; }
        public string CurrentBehaviorName { get; set; } = string.Empty;
        public int CombatIntentsGenerated { get; set; }
        public int CombatShouldShoot { get; set; }
    }
}
