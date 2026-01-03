using System;

namespace BotRunner.Utils
{
    /// <summary>
    /// Lightweight rate limiter for single-threaded loops.
    /// </summary>
    public class RateLimiter
    {
        private readonly TimeSpan _interval;
        private DateTime _last;

        public RateLimiter(TimeSpan interval)
        {
            _interval = interval;
            _last = DateTime.MinValue;
        }

        public bool TryConsume(DateTime utcNow)
        {
            if (utcNow - _last >= _interval)
            {
                _last = utcNow;
                return true;
            }
            return false;
        }

        public void SleepUntilNext()
        {
            var now = DateTime.UtcNow;
            var next = _last + _interval;
            if (next > now)
            {
                var ms = (int)Math.Ceiling((next - now).TotalMilliseconds);
                System.Threading.Thread.Sleep(ms);
            }
            // Intentionally do not modify _last to preserve TryConsume semantics.
        }

        public void Reset(DateTime utcNow)
        {
            _last = utcNow;
        }
    }
}
