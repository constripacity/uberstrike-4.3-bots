using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using BotRunner.Bot.Behaviors;
using BotRunner.Bot.Combat;

namespace BotRunner.Utils
{
    public class ActionFrameRecord
    {
        public long SimulationTick { get; set; }
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
        private readonly double _tickDurationMs = SimulationTime.Instance.TickDurationMs;
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
        private double _decisionSpreadTotal;
        private int _decisionSpreadSamples;
        private int _closeCallCount;
        private int _pipelineConflictCount;
        private readonly List<double> _decisionIntervalsMs = new();
        private double _totalExecutionMs;
        private double _peakWorkingSetMb;
        private int[] _gcCollections = new int[3];
        
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
        public class TacticalMetrics
        {
            public int FlankingAttempts { get; set; }
            public int SuccessfulFlanks { get; set; } // Reached flank position
            public int CrossfireOpportunities { get; set; } // Enemy between allies
            public float AvgFlankAngle { get; set; } // Degrees from ally->enemy line
            public List<float> FlankDistances { get; } = new();

            public float FlankSuccessRate =>
                FlankingAttempts > 0 ? SuccessfulFlanks / (float)FlankingAttempts : 0f;
        }

        public TacticalMetrics TacticalStats { get; } = new TacticalMetrics();

        public void RecordFlankAttempt(bool successful, float angle, float distance)
        {
            lock (_lock)
            {
                TacticalStats.FlankingAttempts++;
                if (successful) TacticalStats.SuccessfulFlanks++;
                TacticalStats.AvgFlankAngle = ((TacticalStats.AvgFlankAngle * (TacticalStats.FlankingAttempts - 1)) + angle) / TacticalStats.FlankingAttempts;
                TacticalStats.FlankDistances.Add(distance);
            }
        }

        public void RecordCrossfireOpportunity()
        {
            lock (_lock)
            {
                TacticalStats.CrossfireOpportunities++;
            }
        }

        public class TeamMetrics
        {
            public int FocusFireOpportunities { get; set; }
            public int FocusFireExecuted { get; set; }
            public int FriendlyFireAvoided { get; set; }
            public int TargetSwitches { get; set; }
            public Dictionary<string, int> TargetDistribution { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<double> AllyDistances { get; } = new();
            public Dictionary<string, WeaponEfficiency> WeaponStats { get; } = new(StringComparer.OrdinalIgnoreCase);

            public class WeaponEfficiency
            {
                public int ShotsFired { get; set; }
                public int Hits { get; set; }
                public int OptimalRangeTicks { get; set; }
                public int TotalTicks { get; set; }
                public double Accuracy => ShotsFired > 0 ? (double)Hits / ShotsFired : 0;
                public double OptimalRangeRate => TotalTicks > 0 ? (double)OptimalRangeTicks / TotalTicks : 0;
            }

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

        public void RecordFrameInterval(double intervalMs)
        {
            lock (_lock)
            {
                _decisionIntervalsMs.Add(intervalMs);
            }
        }

        public void RecordPerformanceSnapshot(double totalExecutionMs, double peakWorkingSetMb, int[]? gcCollections = null)
        {
            lock (_lock)
            {
                _totalExecutionMs = totalExecutionMs;
                _peakWorkingSetMb = peakWorkingSetMb;
                if (gcCollections != null && gcCollections.Length >= 3)
                {
                    _gcCollections = new[] { gcCollections[0], gcCollections[1], gcCollections[2] };
                }
            }
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

        public void RecordWeaponUsage(int weaponId, string weaponName, bool isHit, bool isOptimalRange)
        {
            lock (_lock)
            {
                if (!TeamStats.WeaponStats.TryGetValue(weaponName, out var stats))
                {
                    stats = new TeamMetrics.WeaponEfficiency();
                    TeamStats.WeaponStats[weaponName] = stats;
                }

                stats.TotalTicks++;
                if (isOptimalRange) stats.OptimalRangeTicks++;
                
                // Note: processShootIntent calls this via RecordHit/RecordMiss
                // but we might want more granular weapon tracking here
            }
        }

        public void RecordWeaponShot(string weaponName, bool hit)
        {
            lock (_lock)
            {
                if (!TeamStats.WeaponStats.TryGetValue(weaponName, out var stats))
                {
                    stats = new TeamMetrics.WeaponEfficiency();
                    TeamStats.WeaponStats[weaponName] = stats;
                }
                stats.ShotsFired++;
                if (hit) stats.Hits++;
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
                    var ordered = _actionFrames.OrderBy(f => f.SimulationTick).ToArray();
                    double totalMs = 0;
                    for (int i = 1; i < ordered.Length; i++)
                    {
                        totalMs += (ordered[i].SimulationTick - ordered[i - 1].SimulationTick) * _tickDurationMs;
                    }
                    avgDecisionIntervalMs = totalMs / (ordered.Length - 1);
                    if (avgDecisionIntervalMs > 0)
                        decisionFps = 1000.0 / avgDecisionIntervalMs;
                }

                var decisionSpreadAvg = _decisionSpreadSamples > 0 ? _decisionSpreadTotal / _decisionSpreadSamples : 0.0;
                var closeCallRate = _decisionSpreadSamples > 0 ? (double)_closeCallCount / _decisionSpreadSamples : 0.0;
                var stateTicks = _stateDurations.ToDictionary(kvp => kvp.Key, kvp => (long)Math.Round(kvp.Value.TotalMilliseconds / _tickDurationMs));
                var behaviorTicks = _behaviorDurations.ToDictionary(kvp => kvp.Key, kvp => (long)Math.Round(kvp.Value.TotalMilliseconds / _tickDurationMs));
                var totalSimulationTicks = (long)Math.Round(_elapsedProvider().TotalMilliseconds / _tickDurationMs);

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
                                ExecutionRate = TeamStats.FocusFireOpportunities > 0 ? Math.Round((double)TeamStats.FocusFireExecuted / TeamStats.FocusFireOpportunities, 4) : 0.0
                            },
                        FriendlyFireAvoided = TeamStats.FriendlyFireAvoided,
                        TargetDistribution = new TargetDistributionSummary
                        {
                            Score = (float)Math.Round(TeamStats.TargetDistributionScore, 4),
                            Details = new Dictionary<string,int>(TeamStats.TargetDistribution, StringComparer.OrdinalIgnoreCase)
                        },
                            AllyPositioning = new AllyPositioningSummary
                            {
                                AvgDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Average(), 4) : 0.0,
                                MinDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Min(), 4) : 0.0,
                                MaxDistance = allyDistances.Length > 0 ? Math.Round(allyDistances.Max(), 4) : 0.0
                            },
                            Tactical = new TacticalMetricsSummary
                            {
                                FlankingAttempts = TacticalStats.FlankingAttempts,
                                SuccessfulFlanks = TacticalStats.SuccessfulFlanks,
                                FlankSuccessRate = Math.Round(TacticalStats.FlankSuccessRate, 4),
                                CrossfireOpportunities = TacticalStats.CrossfireOpportunities,
                                AvgFlankAngle = (float)Math.Round(TacticalStats.AvgFlankAngle, 4)
                            },
                            WeaponEfficiency = TeamStats.WeaponStats.ToDictionary(
                                kv => kv.Key,
                                kv => new WeaponEfficiencySummary
                                {
                                    Accuracy = Math.Round(kv.Value.Accuracy, 4),
                                    OptimalRangeRate = Math.Round(kv.Value.OptimalRangeRate, 4)
                                })
                        };
                }

                var frameIntervals = _decisionIntervalsMs.ToList();
                frameIntervals.Sort();
                var frameAvg = frameIntervals.Count > 0 ? frameIntervals.Average() : 0;
                var frameP95 = frameIntervals.Count > 0 ? Percentile(frameIntervals, 0.95) : 0;
                var frameP99 = frameIntervals.Count > 0 ? Percentile(frameIntervals, 0.99) : 0;
                var frameMax = frameIntervals.Count > 0 ? frameIntervals.Max() : 0;

                var performanceGrade = frameAvg < 2.0 ? "A" : frameAvg < 5.0 ? "B" : "C";
                var decisionQualityGrade = _avgDecisionConfidence > 0.8 ? "A" : _avgDecisionConfidence > 0.6 ? "B" : "C";

                var performanceMetrics = new PerformanceMetrics
                {
                    TotalExecutionMs = _totalExecutionMs,
                    PeakWorkingSetMb = _peakWorkingSetMb,
                    GcCollections = _gcCollections.ToArray(),
                    FrameTimeStats = new FrameTimeStats
                    {
                        AvgMs = Math.Round(frameAvg, 4),
                        P95Ms = Math.Round(frameP95, 4),
                        P99Ms = Math.Round(frameP99, 4),
                        MaxMs = Math.Round(frameMax, 4)
                    }
                };

                var validationChecksum = ComputeChecksum(stateTicks, behaviorTicks, primaryDecisions, _behaviorSwitches, _totalDecisionFrames, totalSimulationTicks);

                return new RunSummarySnapshot
                {
                    StateSeconds = _stateDurations.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value.TotalSeconds, 4)),
                    StateTicks = stateTicks,
                    StateEntries = new Dictionary<string, int>(_stateEntries, StringComparer.OrdinalIgnoreCase),
                    PositionUpdatesSent = _positionUpdatesSent,
                    NetworkTicksReceived = _networkTicksReceived,
                    TotalRuntimeSeconds = Math.Round(_elapsedProvider().TotalSeconds, 4),
                    TotalSimulationTicks = totalSimulationTicks,
                    CurrentBehaviorName = _currentBehaviorName,
                    BehaviorSwitches = _behaviorSwitches,
                    BehaviorSeconds = _behaviorDurations.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value.TotalSeconds, 4)),
                    BehaviorTicks = behaviorTicks,
                    BehaviorSwitchesPerMinute = CalculateSwitchFrequency(),
                    SwitchesPerMinute = CalculateSwitchFrequency(),
                    SwitchReasons = new Dictionary<string, int>(_switchReasons, StringComparer.OrdinalIgnoreCase),
                    OscillationAlerts = _oscillationAlerts,
                    MaxSwitchesPerSecond = _maxSwitchesPerSecond,
                    DecisionSpreadAvg = Math.Round(decisionSpreadAvg, 4),
                    CloseCallRate = Math.Round(closeCallRate, 4),
                    PipelineConflictCount = _pipelineConflictCount,
                    PerformanceMetrics = performanceMetrics,
                    ValidationSummary = new ValidationSummary
                    {
                        Deterministic = true,
                        Checksum = validationChecksum,
                        PerformanceGrade = performanceGrade,
                        DecisionQualityGrade = decisionQualityGrade
                    },
                    ActionPipeline = new ActionPipelineMetricsSummary
                    {
                        TotalDecisionFrames = _totalDecisionFrames,
                        AvgDecisionConfidence = (float)Math.Round(_avgDecisionConfidence, 4),
                        PrimaryDecisions = primaryDecisions,
                        AvgTargetLockMs = _targetLockDurationsMs.Count > 0 ? (float)Math.Round(_targetLockDurationsMs.Average(), 4) : 0f,
                        TargetSwitches = _targetSwitches,
                        ShootOpportunities = _shootOpportunities,
                        ShootChosen = _shootChosen,
                        ShootBlockedReasons = new Dictionary<string,int>(_shootBlockedReasons, StringComparer.OrdinalIgnoreCase),
                        AvgDecisionIntervalMs = (float)Math.Round(avgDecisionIntervalMs, 4),
                        DecisionFramesPerSecond = (float)Math.Round(decisionFps, 4)
                    },
                    CombatEffectiveness = new CombatEffectivenessMetrics
                    {
                        ShotsFired = _shotsFired,
                        ActualHits = _actualHits,
                        Misses = _misses,
                        EstimatedHits = (int)(_totalHitProbability),
                        HitProbabilityAvg = _shotsFired > 0 ? (float)Math.Round(_totalHitProbability / _shotsFired, 4) : 0,
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
                    SimulationTick = SimulationTime.Instance.CurrentTick,
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

        public void RecordBehaviorSpread(IReadOnlyList<Bot.AI.BehaviorScore> scores)
        {
            if (scores == null || scores.Count < 2)
            {
                return;
            }

            lock (_lock)
            {
                var ordered = scores.OrderByDescending(s => s.AdjustedScore).ToArray();
                var spread = ordered[0].AdjustedScore - ordered[1].AdjustedScore;
                _decisionSpreadTotal += spread;
                _decisionSpreadSamples++;
                if (Math.Abs(spread) <= 0.1f)
                {
                    _closeCallCount++;
                }
            }
        }

        public void RecordPipelineConflict()
        {
            Interlocked.Increment(ref _pipelineConflictCount);
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
                _stateEnteredAt = now;
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
                _behaviorEnteredAt = now;
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
            return Math.Round(_behaviorSwitches / minutes, 4);
        }

        private void TrackOscillation()
        {
            var nowSeconds = _elapsedProvider().TotalSeconds;
            if (_switchTimestampsSeconds.Count > 0 && nowSeconds < _switchTimestampsSeconds[0])
            {
                _switchTimestampsSeconds.Clear();
            }
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

        private static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            if (sorted.Count == 0) return 0;
            var rank = percentile * (sorted.Count - 1);
            var low = (int)Math.Floor(rank);
            var high = (int)Math.Ceiling(rank);
            if (low == high) return sorted[low];
            var weight = rank - low;
            return sorted[low] + (sorted[high] - sorted[low]) * weight;
        }

        private static string ComputeChecksum(
            IDictionary<string, long> stateTicks,
            IDictionary<string, long> behaviorTicks,
            IDictionary<string, int> primaryDecisions,
            int behaviorSwitches,
            int totalDecisionFrames,
            long? totalSimulationTicks)
        {
            var parts = new List<string>
            {
                string.Join(";", stateTicks.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")),
                string.Join(";", behaviorTicks.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")),
                string.Join(";", primaryDecisions.OrderBy(k => k.Key).Select(k => $"{k.Key}:{k.Value}")),
                behaviorSwitches.ToString(),
                totalDecisionFrames.ToString(),
                totalSimulationTicks?.ToString() ?? "0"
            };

            var payload = string.Join("|", parts);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
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
        public TacticalMetricsSummary Tactical { get; set; } = new TacticalMetricsSummary();
        public Dictionary<string, WeaponEfficiencySummary> WeaponEfficiency { get; set; } = new();
        public WavePerformanceSummary? WavePerformance { get; set; }
    }

    public class WeaponEfficiencySummary
    {
        public double Accuracy { get; set; }
        public double OptimalRangeRate { get; set; }
    }

    public class TacticalMetricsSummary
    {
        public int FlankingAttempts { get; set; }
        public int SuccessfulFlanks { get; set; }
        public double FlankSuccessRate { get; set; }
        public int CrossfireOpportunities { get; set; }
        public float AvgFlankAngle { get; set; }
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

    public class FrameTimeStats
    {
        public double AvgMs { get; set; }
        public double P95Ms { get; set; }
        public double P99Ms { get; set; }
        public double MaxMs { get; set; }
    }

    public class PerformanceMetrics
    {
        public double TotalExecutionMs { get; set; }
        public double PeakWorkingSetMb { get; set; }
        public int[] GcCollections { get; set; } = Array.Empty<int>();
        public FrameTimeStats FrameTimeStats { get; set; } = new FrameTimeStats();
    }

    public class ValidationSummary
    {
        public bool Deterministic { get; set; }
        public string? Checksum { get; set; }
        public string PerformanceGrade { get; set; } = "C";
        public string DecisionQualityGrade { get; set; } = "C";
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
        public Dictionary<string, long> StateTicks { get; set; } = new();
        public Dictionary<string, int> StateEntries { get; set; } = new();
        public int PositionUpdatesSent { get; set; }
        public int NetworkTicksReceived { get; set; }
        public double TotalRuntimeSeconds { get; set; }
        public long TotalSimulationTicks { get; set; }
        public string CurrentBehaviorName { get; set; } = string.Empty;
        public int BehaviorSwitches { get; set; }
        public Dictionary<string, double> BehaviorSeconds { get; set; } = new();
        public Dictionary<string, long> BehaviorTicks { get; set; } = new();
        public double BehaviorSwitchesPerMinute { get; set; }
        public double SwitchesPerMinute { get; set; }
        public Dictionary<string, int> SwitchReasons { get; set; } = new();
        public int OscillationAlerts { get; set; }
        public int MaxSwitchesPerSecond { get; set; }
        public double DecisionSpreadAvg { get; set; }
        public double CloseCallRate { get; set; }
        public int PipelineConflictCount { get; set; }
        public PerformanceMetrics? PerformanceMetrics { get; set; }
        public ValidationSummary? ValidationSummary { get; set; }
        public ActionPipelineMetricsSummary? ActionPipeline { get; set; }
        public CombatEffectivenessMetrics? CombatEffectiveness { get; set; }
        public TeamMetricsSummary? TeamMetrics { get; set; }
    }
}
