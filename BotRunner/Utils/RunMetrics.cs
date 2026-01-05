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
        public bool LeadPredictionUsed { get; set; }
        public float HitProbability { get; set; }
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

        // Combat effectiveness
        private int _shotsFired;
        private float _totalHitProbability;
        private int _leadPredictionUsed;
        private int _actualHits;
        private int _totalDamageDealt;
        private int _totalDamageTaken;
        private int _misses;

        // Team metrics (multi-agent)
        public class TeamMetrics
        {
            public int FocusFireOpportunities { get; set; }
            public int FocusFireExecuted { get; set; }
            public int FriendlyFireAvoided { get; set; }
            public int TargetSwitches { get; set; }
            public Dictionary<string, int> TargetDistribution { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<double> AllyDistances { get; } = new();

            public float TargetDistributionScore
            {
                get
                {
                    if (TargetDistribution.Count == 0) return 1f;
                    var totalShots = TargetDistribution.Values.Sum();
                    if (totalShots <= 0) return 1f;

                    double entropy = 0.0;
                    foreach (var shots in TargetDistribution.Values)
                    {
                        var p = shots / (double)totalShots;
                        if (p > 0.0)
                            entropy -= p * Math.Log(p);
                    }

                    var maxEntropy = Math.Log(TargetDistribution.Count);
                    return maxEntropy > 0 ? (float)(entropy / maxEntropy) : 1f;
                }
            }
        }

        public TeamMetrics TeamStats { get; } = new TeamMetrics();

        public RunMetrics(Func<TimeSpan> elapsedProvider)
        {
            _elapsedProvider = elapsedProvider;
            _stateEnteredAt = _elapsedProvider();
        }

        public void RecordFocusFireOpportunity(bool executed)
        {
            lock (_lock)
            {
                TeamStats.FocusFireOpportunities++;
                if (executed) TeamStats.FocusFireExecuted++;
            }
        }

        public void RecordFriendlyFireAvoided()
        {
            lock (_lock)
            {
                TeamStats.FriendlyFireAvoided++;
            }
        }

        public void RecordTargetEngagement(int enemyId)
        {
            lock (_lock)
            {
                var key = enemyId.ToString();
                if (!TeamStats.TargetDistribution.ContainsKey(key))
                    TeamStats.TargetDistribution[key] = 0;
                TeamStats.TargetDistribution[key]++;
            }
        }

        public void RecordAllyDistance(float distance)
        {
            lock (_lock)
            {
                TeamStats.AllyDistances.Add(distance);
            }
        }

        public void RecordHit(int damage, bool leadUsed)
        {
            lock (_lock)
            {
                _actualHits++;
                _totalDamageDealt += damage;
                if (leadUsed) _leadPredictionUsed++;
            }
        }

        public void RecordMiss()
        {
            lock (_lock)
            {
                _misses++;
            }
        }

        public void RecordDamageTaken(int damage)
        {
            lock (_lock)
            {
                _totalDamageTaken += damage;
            }
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

                // Build team metrics summary
                TeamMetricsSummary? teamSummary = null;
                if (TeamStats != null)
                {
                    var allyDistances = TeamStats.AllyDistances.ToArray();
                    teamSummary = new TeamMetricsSummary
                    {
                        FocusFire = new FocusFireSummary
                        {
                            Opportunities = TeamStats.FocusFireOpportunities,
                            Executed = TeamStats.FocusFireExecuted,
                            ExecutionRate = TeamStats.FocusFireOpportunities > 0 ? Math.Round((double)TeamStats.FocusFireExecuted / TeamStats.FocusFireOpportunities, 3) : 0.0
                        },
                        FriendlyFireAvoided = TeamStats.FriendlyFireAvoided,
                        TargetDistribution = new TargetDistributionSummary
                        {
                            Score = TeamStats.TargetDistributionScore,
                            Details = new Dictionary<string,int>(TeamStats.TargetDistribution, StringComparer.OrdinalIgnoreCase)
                        },
                        AllyPositioning = new AllyPositioningSummary
                        {
                            AvgDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Average(), 3) : 0.0,
                            MinDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Min(), 3) : 0.0,
                            MaxDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Max(), 3) : 0.0
                        }
                    };
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
                    },
                    CombatEffectiveness = new CombatEffectivenessMetrics
                    {
                        ShotsFired = _shotsFired,
                        ActualHits = _actualHits,
                        Misses = _misses,
                        EstimatedHits = (int)(_totalHitProbability),
                        HitProbabilityAvg = _shotsFired > 0 ? _totalHitProbability / _shotsFired : 0,
                        TotalDamageDealt = _totalDamageDealt,
                        TotalDamageTaken = _totalDamageTaken,
                        LeadPredictionUsed = _leadPredictionUsed,
                        AvgLeadTimeMs = 0 // Not implemented yet
                    },
                    TeamMetrics = teamSummary
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
                    HadReloadIntent = frame.Combat.ShouldReload,
                    LeadPredictionUsed = frame.Combat.LeadPredictionUsed,
                    HitProbability = frame.Combat.Accuracy
                });

                if (frame.Combat.ShouldShoot)
                {
                    _shotsFired++;
                    _totalHitProbability += frame.Combat.Accuracy;
                    if (frame.Combat.LeadPredictionUsed)
                    {
                        _leadPredictionUsed++;
                    }
                }

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

    public class CombatEffectivenessMetrics
    {
        public int ShotsFired { get; set; }
        public int ActualHits { get; set; }
        public int Misses { get; set; }
        public int EstimatedHits { get; set; }
        public float HitProbabilityAvg { get; set; }
        public int TotalDamageDealt { get; set; }
        public int TotalDamageTaken { get; set; }
        public int LeadPredictionUsed { get; set; }
        public float AvgLeadTimeMs { get; set; }
    }

    public class TeamMetricsSummary
    {
        public FocusFireSummary FocusFire { get; set; } = new FocusFireSummary();
        public int FriendlyFireAvoided { get; set; }
        public TargetDistributionSummary TargetDistribution { get; set; } = new TargetDistributionSummary();
        public AllyPositioningSummary AllyPositioning { get; set; } = new AllyPositioningSummary();
        public WavePerformanceSummary? WavePerformance { get; set; }
    }

    public class FocusFireSummary
    {
        public int Opportunities { get; set; }
        public int Executed { get; set; }
        public double ExecutionRate { get; set; }
    }

    public class TargetDistributionSummary
    {
        public float Score { get; set; }
        public Dictionary<string, int> Details { get; set; } = new();
    }

    public class AllyPositioningSummary
    {
        public double AvgDistance { get; set; }
        public double MinDistance { get; set; }
        public double MaxDistance { get; set; }
    }

    public class WavePerformanceSummary
    {
        public int WavesSurvived { get; set; }
        public int TotalWaves { get; set; }
        public double SurvivalRate { get; set; }
        public List<int> EnemiesPerWave { get; set; } = new();
        public List<bool> SurvivedWave { get; set; } = new();
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
        public CombatEffectivenessMetrics? CombatEffectiveness { get; set; }
        public TeamMetricsSummary? TeamMetrics { get; set; }
    }
}
