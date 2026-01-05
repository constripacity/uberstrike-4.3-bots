using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BotRunner.Bot.Behaviors;
using BotRunner.Bot.Combat;

namespace BotRunner.Utils
{
    public class ActionFrameRecord
    {
        public DateTime Timestamp { get; set; }
        public string PrimaryDecision { get; set; } = "";
        public string Reason { get; set; } = "";
        public float Confidence { get; set; }
        public bool HadMovement { get; set; }
        public bool HadShootIntent { get; set; }
        public bool HadReloadIntent { get; set; }
    }

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
        private int _oscillationAlerts;
        private int _maxSwitchesPerSecond;
        
        private readonly List<ActionFrameRecord> _actionFrames = new();
        private int _totalDecisionFrames;
        private float _avgDecisionConfidence;
        
        // Target/hysteresis & shooting debug metrics
        private readonly List<double> _targetLockDurationsMs = new();
        private int _targetSwitches;
        private int _shootOpportunities;
        private int _shootChosen;
        private readonly Dictionary<string, int> _shootBlockedReasons = new(StringComparer.OrdinalIgnoreCase);

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
                
                var primaryDecisions = _actionFrames
                    .GroupBy(f => f.PrimaryDecision)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Compute average decision interval (based on sim-time timestamps recorded in frames)
                double avgDecisionIntervalMs = 0;
                double decisionFps = 0;
                if (_actionFrames.Count > 1)
                {
                    var ordered = _actionFrames.OrderBy(f => f.Timestamp).ToArray();
                    double totalMs = 0;
                    for (int i = 1; i < ordered.Length; i++)
                    {
                        totalMs += (ordered[i].Timestamp - ordered[i - 1].Timestamp).TotalMilliseconds;
                    }
                    avgDecisionIntervalMs = totalMs / (ordered.Length - 1);
                    if (avgDecisionIntervalMs > 0)
                        decisionFps = 1000.0 / avgDecisionIntervalMs;
                }

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
                    SwitchReasons = new Dictionary<string, int>(_switchReasons, StringComparer.OrdinalIgnoreCase),
                    OscillationAlerts = _oscillationAlerts,
                    MaxSwitchesPerSecond = _maxSwitchesPerSecond,
                ActionPipeline = new ActionPipelineMetricsSummary
                {
                    TotalDecisionFrames = _totalDecisionFrames,
                    AvgDecisionConfidence = _avgDecisionConfidence,
                    PrimaryDecisions = primaryDecisions,
                    AvgTargetLockMs = _targetLockDurationsMs.Count > 0 ? (float)(_targetLockDurationsMs.Average()) : 0f,
                    TargetSwitches = _targetSwitches,
                    ShootOpportunities = _shootOpportunities,
                    ShootChosen = _shootChosen,
                    ShootBlockedReasons = new Dictionary<string,int>(_shootBlockedReasons, StringComparer.OrdinalIgnoreCase),
                    AvgDecisionIntervalMs = (float)avgDecisionIntervalMs,
                    DecisionFramesPerSecond = (float)Math.Round(decisionFps, 2)
                }
                };
            }
        }
        
        public void RecordActionFrame(ActionFrame frame)
        {
            lock (_lock)
            {
                _actionFrames.Add(new ActionFrameRecord
                {
                    Timestamp = frame.FrameTime,
                    PrimaryDecision = frame.PrimaryDecision,
                    Reason = frame.Reason,
                    Confidence = frame.Confidence,
                    HadMovement = frame.Movement.HasTarget,
                    HadShootIntent = frame.Combat.ShouldShoot,
                    HadReloadIntent = frame.Combat.ShouldReload
                });

                _totalDecisionFrames++;
                _avgDecisionConfidence = ((_avgDecisionConfidence * (_totalDecisionFrames - 1)) + frame.Confidence) / _totalDecisionFrames;
            }
        }

        public void RecordTargetLock(double durationMs)
        {
            lock (_lock)
            {
                _targetLockDurationsMs.Add(durationMs);
            }
        }

        public void RecordTargetSwitch()
        {
            Interlocked.Increment(ref _targetSwitches);
        }

        public void RecordShootOpportunity(string blockedReason, bool chosen)
        {
            lock (_lock)
            {
                _shootOpportunities++;
                if (chosen)
                    _shootChosen++;

                if (!string.IsNullOrEmpty(blockedReason))
                {
                    if (_shootBlockedReasons.ContainsKey(blockedReason))
                        _shootBlockedReasons[blockedReason]++;
                    else
                        _shootBlockedReasons[blockedReason] = 1;
                }
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

    public class ActionPipelineMetricsSummary
    {
        public int TotalDecisionFrames { get; set; }
        public float AvgDecisionConfidence { get; set; }
        public Dictionary<string, int> PrimaryDecisions { get; set; } = new();
        public float AvgTargetLockMs { get; set; }
        public int TargetSwitches { get; set; }
        public int ShootOpportunities { get; set; }
        public int ShootChosen { get; set; }
        public Dictionary<string, int> ShootBlockedReasons { get; set; } = new();
        // Added sim-time based metrics
        public float AvgDecisionIntervalMs { get; set; }
        public float DecisionFramesPerSecond { get; set; }
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
        public Dictionary<string, int> SwitchReasons { get; set; } = new();
        public int OscillationAlerts { get; set; }
        public int MaxSwitchesPerSecond { get; set; }
        public ActionPipelineMetricsSummary? ActionPipeline { get; set; }
    }
}
