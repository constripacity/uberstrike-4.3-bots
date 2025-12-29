using System;

namespace BotRunner.Utils
{
    /// <summary>
    /// Simple wall-clock rate limiter for single-threaded loops.
    /// </summary>
    public class RateLimiter
    {
        private readonly TimeSpan _interval;
        private DateTime _lastRunUtc;

        public RateLimiter(TimeSpan interval)
        {
            _interval = interval;
            _lastRunUtc = DateTime.MinValue;
        }

        public bool ShouldRun(DateTime utcNow)
        {
            if (utcNow - _lastRunUtc >= _interval)
            {
                _lastRunUtc = utcNow;
                return true;
            }
            return false;
        }

        public void Reset()
        {
            _lastRunUtc = DateTime.MinValue;
        }
    }
}
