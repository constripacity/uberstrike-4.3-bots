using System;
using System.Diagnostics;
using System.Threading;

namespace BotRunner.Utils
{
    /// <summary>
    /// Simple cadence controller used to pace networking and AI ticks to avoid spamming the server.
    /// </summary>
    public class RateLimiter
    {
        private readonly TimeSpan _interval;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private long _nextTickTicks;

        public RateLimiter(TimeSpan interval)
        {
            _interval = interval;
            _nextTickTicks = _stopwatch.ElapsedTicks + interval.Ticks;
        }

        public void SleepUntilNext()
        {
            var now = _stopwatch.ElapsedTicks;
            var remaining = _nextTickTicks - now;
            if (remaining > 0)
            {
                var ms = (int)Math.Max(1, TimeSpan.FromTicks(remaining).TotalMilliseconds);
                Thread.Sleep(ms);
            }

            _nextTickTicks = _stopwatch.ElapsedTicks + _interval.Ticks;
        }
    }
}
