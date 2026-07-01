using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Time-ordered per-vessel buffer of authoritative snapshots.
    /// Insertions must be in non-decreasing GameTime order; the buffer
    /// keeps a sliding window (configurable max length) and drops the
    /// oldest snapshot when capacity is reached.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        public int Count => _buffer.Count;
        public IReadOnlyList<VesselSnapshot> Snapshots => _buffer;

        private readonly List<VesselSnapshot> _buffer;
        private readonly int _maxLength;

        public SnapshotBuffer(int maxLength = 32)
        {
            if (maxLength < 2) throw new ArgumentOutOfRangeException(nameof(maxLength));
            _maxLength = maxLength;
            _buffer = new List<VesselSnapshot>(maxLength);
        }

        public void Add(VesselSnapshot snapshot)
        {
            if (_buffer.Count > 0 && snapshot.GameTime < _buffer[_buffer.Count - 1].GameTime)
                throw new ArgumentException("Snapshot must arrive in non-decreasing GameTime order.");
            _buffer.Add(snapshot);
            if (_buffer.Count > _maxLength)
                _buffer.RemoveAt(0);
        }

        public void Clear() => _buffer.Clear();

        public bool TryBracket(double gameTime, out VesselSnapshot before, out VesselSnapshot after)
        {
            before = default;
            after = default;
            for (int i = 0; i < _buffer.Count; i++)
            {
                var s = _buffer[i];
                if (s.GameTime <= gameTime) before = s;
                else { after = s; return true; }
            }
            return false;
        }
    }

    /// <summary>
    /// Per-vessel interpolator. Maintains a render clock that runs
    /// <see cref="InterpolationDelay"/> seconds behind the latest
    /// received snapshot, linearly interpolating position between the
    /// bracketing snapshots (NET-4).
    /// </summary>
    public sealed class VesselInterpolator
    {
        public double InterpolationDelay { get; set; } = 0.1; // 100 ms ≈ 2.5 snapshot intervals at 25 Hz
        public SnapshotBuffer Buffer { get; } = new();

        public void OnSnapshot(VesselSnapshot snapshot) => Buffer.Add(snapshot);

        /// <summary>
        /// Compute the position to render at server-time
        /// <paramref name="serverGameTime"/>. If only one snapshot is
        /// available, return its position. If the render time is past
        /// the newest snapshot, extrapolate briefly using its velocity.
        /// </summary>
        public Vector3d Sample(double serverGameTime) => SampleState(serverGameTime).Position;

        /// <summary>
        /// Interpolated transform <b>and</b> velocity at
        /// <paramref name="serverGameTime"/> (NET-4, M1-T13). Both are
        /// linearly interpolated between the bracketing snapshots; past the
        /// newest snapshot, position extrapolates along the held velocity.
        /// </summary>
        public InterpolatedState SampleState(double serverGameTime)
        {
            var renderTime = serverGameTime - InterpolationDelay;
            if (Buffer.Count == 0) return default;

            if (Buffer.Count == 1)
            {
                var only = Buffer.Snapshots[0];
                return new InterpolatedState(only.Position, only.Velocity, only.Orientation);
            }

            if (!Buffer.TryBracket(renderTime, out var before, out var after))
            {
                // Past the newest snapshot: extrapolate position, hold velocity + orientation.
                var newest = Buffer.Snapshots[Buffer.Count - 1];
                var dt = renderTime - newest.GameTime;
                if (dt <= 0.0) return new InterpolatedState(newest.Position, newest.Velocity, newest.Orientation);
                return new InterpolatedState(newest.Position + newest.Velocity * dt, newest.Velocity, newest.Orientation);
            }

            if (before.GameTime >= after.GameTime)
                return new InterpolatedState(before.Position, before.Velocity, before.Orientation);

            var t = (renderTime - before.GameTime) / (after.GameTime - before.GameTime);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return new InterpolatedState(
                before.Position * (1.0 - t) + after.Position * t,
                before.Velocity * (1.0 - t) + after.Velocity * t,
                Quaterniond.Slerp(before.Orientation, after.Orientation, t));
        }
    }

    /// <summary>Interpolated render state: transform + velocity + orientation (NET-4).</summary>
    public readonly struct InterpolatedState
    {
        public Vector3d Position { get; }
        public Vector3d Velocity { get; }
        public Quaterniond Orientation { get; }

        public InterpolatedState(Vector3d position, Vector3d velocity)
            : this(position, velocity, Quaterniond.Identity) { }

        public InterpolatedState(Vector3d position, Vector3d velocity, Quaterniond orientation)
        {
            Position = position;
            Velocity = velocity;
            Orientation = orientation;
        }
    }
}