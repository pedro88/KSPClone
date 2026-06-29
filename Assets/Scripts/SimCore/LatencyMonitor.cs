#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Tracks measured RTT and derives the input buffer sizing that
    /// covers the in-flight window without overflow (M1-T12, NET-6).
    ///
    /// The monitor keeps an exponential moving average of recent RTT
    /// samples. From the average it recommends an input-buffer size
    /// in *number of fixed 60 Hz ticks* — call
    /// <see cref="RecommendedBufferSize"/> to read it. The buffer must
    /// hold at least ceil(½·RTT + 1 tick + jitter margin).
    /// </summary>
    public sealed class LatencyMonitor
    {
        public double AverageRttSeconds { get; private set; }
        public double WorstRttSeconds { get; private set; }
        public int SampleCount { get; private set; }
        public double FixedDt { get; }

        private readonly double _alpha;
        private readonly double _jitterMarginSeconds;

        public LatencyMonitor(double fixedDt = 1.0 / 60.0, double emaAlpha = 0.2, double jitterMarginSeconds = 0.020)
        {
            FixedDt = fixedDt;
            _alpha = emaAlpha;
            _jitterMarginSeconds = jitterMarginSeconds;
        }

        public void RecordSample(double rttSeconds)
        {
            if (rttSeconds < 0.0) throw new ArgumentOutOfRangeException(nameof(rttSeconds));
            if (SampleCount == 0)
            {
                AverageRttSeconds = rttSeconds;
                WorstRttSeconds = rttSeconds;
            }
            else
            {
                AverageRttSeconds = _alpha * rttSeconds + (1.0 - _alpha) * AverageRttSeconds;
                if (rttSeconds > WorstRttSeconds) WorstRttSeconds = rttSeconds;
            }
            SampleCount++;
        }

        /// <summary>
        /// Recommended input buffer size in fixed ticks:
        /// ceil((½·RTT + 1 tick + jitter) / fixedDt).
        /// </summary>
        public int RecommendedBufferSize()
        {
            if (SampleCount == 0) return 16; // sane default before any sample
            var targetSeconds = 0.5 * AverageRttSeconds + FixedDt + _jitterMarginSeconds;
            return (int)Math.Ceiling(targetSeconds / FixedDt);
        }
    }
}