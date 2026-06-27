using System;

namespace KSPClone.SimCore
{
    public sealed class SimScheduler
    {
        public const double FixedDt = 1.0 / 60.0;
        public const double MaxAdvanceSeconds = 0.25;

        public long TickCount { get; private set; }

        public event Action<long>? CatchupClamped;

        private readonly SimWorld _world;
        private double _accumulator;

        public SimScheduler(SimWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public void Advance(double deltaSeconds)
        {
            if (deltaSeconds < 0.0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "deltaSeconds must be non-negative.");

            var added = deltaSeconds;
            if (added > MaxAdvanceSeconds)
            {
                CatchupClamped?.Invoke((long)Math.Round((added - MaxAdvanceSeconds) / FixedDt));
                added = MaxAdvanceSeconds;
            }

            _accumulator += added;

            while (_accumulator >= FixedDt)
            {
                _world.Tick(FixedDt);
                _accumulator -= FixedDt;
                TickCount++;
            }
        }
    }
}