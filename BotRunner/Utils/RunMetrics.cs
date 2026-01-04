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
        private readonly Dictionary<string, TimeSpan> _behaviorDurations = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _switchReasons = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<double> _switchTimestampsSeconds = new();
        private string _currentState = "Uninitialized";
        private TimeSpan _stateEnteredAt;
        private int _positionUpdatesSent;
        private int _networkTicksReceived;
        private string _currentBehaviorName = string.Empty;
        private TimeSpan _behaviorEnteredAt;
        private int _behaviorSwitches;
        private int _combatIntentsGenerated;
        private int _combatShouldShoot;
        private int _combatShouldReload;
        private int _combatLineOfSight;
        private int _combatBlockedSight;
        private int _oscillationAlerts;
        private int _maxSwitchesPerSecond;
        private int _actionsQueued;
        private int _actionsExecuted;
        private int _combatActionsQueued;

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
                AddDurationForCurrentBehavior();
                return new RunSummarySnapshot
                {
                    StateSeconds = _stateDurations.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value.TotalSeconds, 3)),
                    StateEntries = new Dictionary<string, int>(_stateEntries, StringComparer.OrdinalIgnoreCase),
                    PositionUpdatesSent = _positionUpdatesSent,
                    NetworkTicksReceived = _networkTicksReceived,
                    TotalRuntimeSeconds = Math.Round(_elapsedProvider().TotalSeconds, 3),
                    CurrentBehaviorName = _currentBehaviorName,
                    BehaviorSwitches = _behaviorSwitches,
                    BehaviorSeconds = _behaviorDurations.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value.TotalSeconds, 3)),
                    BehaviorSwitchesPerMinute = CalculateSwitchFrequency(),
                    CombatIntentsGenerated = _combatIntentsGenerated,
                    CombatShouldShoot = _combatShouldShoot,
                    CombatShouldReload = _combatShouldReload,
                    CombatLineOfSight = _combatLineOfSight,
                    CombatBlockedSight = _combatBlockedSight,
                    ActionsQueued = _actionsQueued,
                    ActionsExecuted = _actionsExecuted,
                    CombatActionsQueued = _combatActionsQueued,
                    SwitchReasons = new Dictionary<string, int>(_switchReasons, StringComparer.OrdinalIgnoreCase),
                    OscillationAlerts = _oscillationAlerts,
                    MaxSwitchesPerSecond = _maxSwitchesPerSecond
                };
            }
        }

        public void RecordBehaviorSwitch(string behaviorName)
        {
            RecordBehaviorDecision(behaviorName, true, "legacy");
        }

        public void RecordBehaviorDecision(string behaviorName, bool switched, string reason)
        {
            lock (_lock)
            {
                var normalizedReason = NormalizeReason(reason);
                if (switched)
                {
                    AddDurationForCurrentBehavior();
                    _behaviorSwitches++;
                    _currentBehaviorName = behaviorName;
                    _behaviorEnteredAt = _elapsedProvider();
                    if (_switchReasons.ContainsKey(normalizedReason))
                    {
                        _switchReasons[normalizedReason]++;
                    }
                    else
                    {
                        _switchReasons[normalizedReason] = 1;
                    }

                    TrackOscillation();
                }
                else
                {
                    if (_currentBehaviorName != behaviorName)
                    {
                        AddDurationForCurrentBehavior();
                        _currentBehaviorName = behaviorName;
                        _behaviorEnteredAt = _elapsedProvider();
                    }
                }
            }
        }

        public void SetCurrentBehavior(string behaviorName)
        {
            RecordBehaviorDecision(behaviorName, false, "state_sync");
        }

        public void RecordCombatIntent(Bot.Combat.CombatIntentDecision decision, float distance, TimeSpan reactionLatency)
        {
            Interlocked.Increment(ref _combatIntentsGenerated);
            if (decision.Intent.ShouldShoot)
            {
                Interlocked.Increment(ref _combatShouldShoot);
            }
            if (decision.Intent.ShouldReload)
            {
                Interlocked.Increment(ref _combatShouldReload);
            }
            if (decision.HasLineOfSight)
            {
                Interlocked.Increment(ref _combatLineOfSight);
            }
            else
            {
                Interlocked.Increment(ref _combatBlockedSight);
            }
        }

        public void RecordActionsQueued(int count, int combatCount)
        {
            Interlocked.Add(ref _actionsQueued, count);
            Interlocked.Add(ref _combatActionsQueued, combatCount);
        }

        public void RecordActionsExecuted(int count)
        {
            Interlocked.Add(ref _actionsExecuted, count);
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

        private void AddDurationForCurrentBehavior()
        {
            if (string.IsNullOrEmpty(_currentBehaviorName))
            {
                return;
            }

            var now = _elapsedProvider();
            var delta = now - _behaviorEnteredAt;
            if (delta < TimeSpan.Zero)
            {
                return;
            }

            if (_behaviorDurations.TryGetValue(_currentBehaviorName, out var existing))
            {
                _behaviorDurations[_currentBehaviorName] = existing + delta;
            }
            else
            {
                _behaviorDurations[_currentBehaviorName] = delta;
            }
            _behaviorEnteredAt = now;
        }

        private double CalculateSwitchFrequency()
        {
            var minutes = Math.Max(0.001, _elapsedProvider().TotalMinutes);
            return Math.Round(_behaviorSwitches / minutes, 3);
        }

        private void TrackOscillation()
        {
            var nowSeconds = _elapsedProvider().TotalSeconds;
            _switchTimestampsSeconds.Add(nowSeconds);
            while (_switchTimestampsSeconds.Count > 0 && nowSeconds - _switchTimestampsSeconds[0] > 1.0)
            {
                _switchTimestampsSeconds.RemoveAt(0);
            }

            _maxSwitchesPerSecond = Math.Max(_maxSwitchesPerSecond, _switchTimestampsSeconds.Count);
            if (_switchTimestampsSeconds.Count > 3)
            {
                _oscillationAlerts++;
            }
        }

        private static string NormalizeReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "unknown";
            }

            if (reason.Contains("min_hold", StringComparison.OrdinalIgnoreCase) ||
                reason.Contains("sticky", StringComparison.OrdinalIgnoreCase))
            {
                return "hysteresis";
            }

            if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return "timeout";
            }

            if (reason.Contains("score", StringComparison.OrdinalIgnoreCase))
            {
                return "score_change";
            }

            return reason.Trim();
        }
    }

    public class RunSummarySnapshot
    {
        public Dictionary<string, double> StateSeconds { get; set; } = new();
        public Dictionary<string, int> StateEntries { get; set; } = new();
        public int PositionUpdatesSent { get; set; }
        public int NetworkTicksReceived { get; set; }
        public double TotalRuntimeSeconds { get; set; }
        public string CurrentBehaviorName { get; set; } = string.Empty;
        public int BehaviorSwitches { get; set; }
        public Dictionary<string, double> BehaviorSeconds { get; set; } = new();
        public double BehaviorSwitchesPerMinute { get; set; }
        public int CombatIntentsGenerated { get; set; }
        public int CombatShouldShoot { get; set; }
        public int CombatShouldReload { get; set; }
        public int CombatLineOfSight { get; set; }
        public int CombatBlockedSight { get; set; }
        public int ActionsQueued { get; set; }
        public int ActionsExecuted { get; set; }
        public int CombatActionsQueued { get; set; }
        public Dictionary<string, int> SwitchReasons { get; set; } = new();
        public int OscillationAlerts { get; set; }
        public int MaxSwitchesPerSecond { get; set; }
    }
}
