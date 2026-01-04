using System;
using System.Collections.Generic;
using System.Linq;

namespace BotRunner.Bot.AI
{
    public class UtilityAISelector
    {
        private readonly List<IUtilityBehavior> _behaviors;
        private readonly Random _noiseRandom;
        private readonly float _noiseAmplitude;
        private readonly float _stickinessBonus;
        private readonly TimeSpan _minHold;
        private readonly float _overrideDelta;
        private IUtilityBehavior? _current;
        private DateTime _lastSwitchUtc = DateTime.MinValue;

        public UtilityAISelector(
            IEnumerable<IUtilityBehavior> behaviors,
            float stickinessBonus,
            TimeSpan minHold,
            float overrideDelta,
            int? noiseSeed = null,
            float noiseAmplitude = 0.01f)
        {
            _behaviors = behaviors.ToList();
            _noiseRandom = noiseSeed.HasValue ? new Random(noiseSeed.Value) : new Random();
            _noiseAmplitude = noiseAmplitude;
            _stickinessBonus = stickinessBonus;
            _minHold = minHold;
            _overrideDelta = overrideDelta;
        }

        public string? SelectedBehaviorName => _current?.Name;

        public SelectionDecision Select(BehaviorContext ctx)
        {
            var now = ctx.NowUtc;
            var best = _current;
            var bestScore = float.MinValue;
            var scoreList = new List<BehaviorScore>(_behaviors.Count);
            float currentAdjustedScore = float.MinValue;

            foreach (var b in _behaviors)
            {
                var rawScore = b.Score(ctx);
                var noise = ((float)_noiseRandom.NextDouble() * 2f - 1f) * _noiseAmplitude;
                var score = rawScore + noise;
                if (_current != null && ReferenceEquals(b, _current))
                {
                    score += _stickinessBonus;
                    currentAdjustedScore = score;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = b;
                }

                scoreList.Add(new BehaviorScore(b.Name, rawScore, score, ReferenceEquals(b, _current)));
            }

            if (best == null)
            {
                throw new InvalidOperationException("UtilityAISelector requires at least one behavior.");
            }

            var holdElapsed = now - _lastSwitchUtc;
            if (_current != null && best != _current && holdElapsed < _minHold)
            {
                if (currentAdjustedScore.Equals(float.MinValue))
                {
                    currentAdjustedScore = _current.Score(ctx) + _stickinessBonus;
                }
                var delta = bestScore - currentAdjustedScore;
                if (delta < _overrideDelta)
                {
                    return new SelectionDecision(_current, scoreList, false, $"min_hold (elapsed={holdElapsed.TotalMilliseconds:0}ms delta={delta:0.00})");
                }
            }

            if (best != _current)
            {
                _current = best;
                _lastSwitchUtc = now;
                return new SelectionDecision(best, scoreList, true, "score_win");
            }

            return new SelectionDecision(_current, scoreList, false, "sticky");
        }
    }

    public readonly struct BehaviorScore
    {
        public BehaviorScore(string name, float rawScore, float adjustedScore, bool isCurrent)
        {
            Name = name;
            RawScore = rawScore;
            AdjustedScore = adjustedScore;
            IsCurrent = isCurrent;
        }

        public string Name { get; }
        public float RawScore { get; }
        public float AdjustedScore { get; }
        public bool IsCurrent { get; }
    }

    public readonly struct SelectionDecision
    {
        public SelectionDecision(IUtilityBehavior? behavior, IReadOnlyList<BehaviorScore> scores, bool switched, string reason)
        {
            Behavior = behavior ?? throw new ArgumentNullException(nameof(behavior));
            Scores = scores;
            Switched = switched;
            Reason = reason;
        }

        public IUtilityBehavior Behavior { get; }
        public IReadOnlyList<BehaviorScore> Scores { get; }
        public bool Switched { get; }
        public string Reason { get; }
    }
}
