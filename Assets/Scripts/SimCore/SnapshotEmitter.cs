#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// One vessel's state at one server-tick. Sent on the snapshot
    /// channel; clients interpolate between consecutive snapshots.
    /// </summary>
    public readonly struct VesselSnapshot
    {
        public VesselId VesselId { get; }
        public double GameTime { get; }
        public long Seq { get; }
        public Vector3d Position { get; }
        public Vector3d Velocity { get; }
        /// <summary>Angular velocity (rad/s, world axes) — ADR-0013 §8.</summary>
        public Vector3d AngularVelocity { get; }
        /// <summary>World-frame orientation (ADR-0019). Identity for on-rails vessels.</summary>
        public Quaterniond Orientation { get; }
        /// <summary>The reconciliation ack: highest applied pilot-input tick (ADR-0013 §7). 0 for vessels with no pilot input.</summary>
        public long LastProcessedClientTick { get; }

        public VesselSnapshot(VesselId vesselId, double gameTime, long seq, Vector3d position, Vector3d velocity)
            : this(vesselId, gameTime, seq, position, velocity, Vector3d.Zero, Quaterniond.Identity, 0L)
        {
        }

        /// <summary>Convenience overload without orientation (defaults to identity).</summary>
        public VesselSnapshot(VesselId vesselId, double gameTime, long seq, Vector3d position, Vector3d velocity,
            Vector3d angularVelocity, long lastProcessedClientTick)
            : this(vesselId, gameTime, seq, position, velocity, angularVelocity, Quaterniond.Identity, lastProcessedClientTick)
        {
        }

        public VesselSnapshot(VesselId vesselId, double gameTime, long seq, Vector3d position, Vector3d velocity,
            Vector3d angularVelocity, Quaterniond orientation, long lastProcessedClientTick)
        {
            VesselId = vesselId;
            GameTime = gameTime;
            Seq = seq;
            Position = position;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
            Orientation = orientation;
            LastProcessedClientTick = lastProcessedClientTick;
        }
    }

    /// <summary>
    /// A bundle of vessel snapshots emitted together at one tick. Each
    /// emitter tick produces one of these; clients see them at the
    /// configured snapshot rate (20–30 Hz, decoupled from the 60 Hz
    /// sim tick per NET-5 / ADR-0006).
    /// </summary>
    public sealed class SnapshotBundle
    {
        public double GameTime { get; }
        public long Seq { get; }
        public IReadOnlyList<VesselSnapshot> Vessels { get; }

        public SnapshotBundle(double gameTime, long seq, IReadOnlyList<VesselSnapshot> vessels)
        {
            GameTime = gameTime;
            Seq = seq;
            Vessels = vessels;
        }
    }

    /// <summary>
    /// Driven by the 60 Hz sim tick but with its own accumulator so
    /// emission rate is independent of tick rate. Each emission
    /// produces one <see cref="SnapshotBundle"/> from the on-rails
    /// vessels' cached state (M0-T09) — no per-emission Kepler solve.
    /// </summary>
    public sealed class SnapshotEmitter
    {
        public const double DefaultRateHz = 25.0;
        public const long   EmittedCount  = 0; // placeholder; see EmittedSeq

        public long EmittedSeq { get; private set; }
        public long EmittedBundles { get; private set; }
        public double MeasuredRateHz { get; private set; }
        public double NominalRateHz { get; }

        private readonly SimWorld _world;
        private readonly ConnectionRegistry _connections;
        private readonly IReadOnlyList<IConnectionSink> _sinks;
        private readonly Action<SnapshotBundle>? _onBundle;

        private double _accumulator;
        private double _secondsSinceRateLog;
        private long _bundlesAtLastLog;

        /// <summary>
        /// Adapter the transport layer implements to receive emitted
        /// bundles. The transport is responsible for serialising and
        /// sending them on its unreliable channel.
        /// </summary>
        public interface IConnectionSink
        {
            void DeliverSnapshot(PlayerId recipient, SnapshotBundle bundle);
        }

        /// <summary>
        /// Convenience constructor with no sinks (used by tests and by
        /// the server bootstrap until the transport layer is wired).
        /// </summary>
        public SnapshotEmitter(SimWorld world, double nominalRateHz = DefaultRateHz)
            : this(world, new ConnectionRegistry(), nominalRateHz, null, Array.Empty<IConnectionSink>())
        { }

        public SnapshotEmitter(
            SimWorld world,
            ConnectionRegistry connections,
            double nominalRateHz,
            Action<SnapshotBundle>? onBundle = null,
            IReadOnlyList<IConnectionSink>? sinks = null)
        {
            _world = world;
            _connections = connections;
            _onBundle = onBundle;
            _sinks = sinks ?? Array.Empty<IConnectionSink>();
            NominalRateHz = nominalRateHz;
        }

        /// <summary>
        /// Called by the host every sim tick (60 Hz). Emits 0 or 1
        /// bundles depending on accumulator state.
        /// </summary>
        public void Tick(double dtSeconds)
        {
            _accumulator += dtSeconds;
            var period = 1.0 / NominalRateHz;
            while (_accumulator >= period)
            {
                _accumulator -= period;
                EmitOne();
            }

            _secondsSinceRateLog += dtSeconds;
            if (_secondsSinceRateLog >= 1.0)
            {
                MeasuredRateHz = (EmittedBundles - _bundlesAtLastLog) / _secondsSinceRateLog;
                _bundlesAtLastLog = EmittedBundles;
                _secondsSinceRateLog = 0.0;
            }
        }

        private void EmitOne()
        {
            EmittedSeq++;
            EmittedBundles++;
            var t = _world.Clock.GameTimeSeconds;
            var snapshots = new List<VesselSnapshot>(_world.Vessels.Count);
            foreach (var v in _world.Vessels.Values)
            {
                if (!v.CachedWorldPosition.HasValue || !v.CachedWorldVelocity.HasValue) continue;
                snapshots.Add(new VesselSnapshot(v.Id, t, EmittedSeq,
                    v.CachedWorldPosition.Value, v.CachedWorldVelocity.Value,
                    v.CachedAngularVelocity ?? Vector3d.Zero,
                    v.CachedOrientation ?? Quaterniond.Identity, v.LastProcessedClientTick));
            }
            var bundle = new SnapshotBundle(t, EmittedSeq, snapshots);
            _onBundle?.Invoke(bundle);
            foreach (var sink in _sinks)
                foreach (var session in _connections.All)
                    sink.DeliverSnapshot(session.Id, bundle);
        }
    }
}