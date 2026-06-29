#nullable enable annotations

namespace KSPClone.SimCore
{
    public sealed class Vessel
    {
        public VesselId Id { get; }
        public Orbit Orbit { get; set; }
        public VesselState State { get; set; } = VesselState.OnRails;

        /// <summary>
        /// Bubble that owns this vessel when <see cref="State"/> is
        /// <see cref="VesselState.ActivePhysics"/>. <c>null</c> otherwise.
        /// </summary>
        public BubbleId? BubbleId { get; set; }

        /// <summary>
        /// Vessel-side clock. For on-rails vessels this is pinned to the
        /// master clock (Constitution Art. 4: the universe stays synced
        /// even when empty). For suspended active-physics vessels it
        /// pauses at the snapshot time (M1).
        /// </summary>
        public double VesselClockSeconds { get; set; }

        /// <summary>
        /// Most recently evaluated world-frame state, populated by
        /// <see cref="SimWorld.Tick"/> for on-rails vessels.
        /// </summary>
        public Vector3d? CachedWorldPosition { get; set; }
        public Vector3d? CachedWorldVelocity { get; set; }

        /// <summary>
        /// Most recently evaluated local-frame state inside the bubble
        /// (only meaningful while <see cref="State"/> is
        /// <see cref="VesselState.ActivePhysics"/>). The host updates these
        /// from the rigidbody each fixed tick so the sim core can observe
        /// the vessel without taking a Unity dependency.
        /// </summary>
        public Vector3d? CachedLocalPosition { get; set; }
        public Vector3d? CachedLocalVelocity { get; set; }

        /// <summary>
        /// Most recent angular velocity (rad/s, world axes) of the active
        /// rigid body, written back by the integrator each tick. Replicated
        /// in the snapshot so the client reconciler resets the full predicted
        /// state (ADR-0013 §8). Null while on-rails.
        /// </summary>
        public Vector3d? CachedAngularVelocity { get; set; }

        /// <summary>
        /// Highest pilot-input client tick the server has applied to this
        /// vessel (the reconciliation ack — ADR-0013 §7). Stamped into every
        /// snapshot so the client can reset to authoritative state and replay
        /// only its still-unacked inputs. 0 until the first input is applied.
        /// </summary>
        public long LastProcessedClientTick { get; set; }

        /// <summary>
        /// True if at least one of the vessel's engines is producing
        /// thrust this tick. Reported by the integrator; consulted by
        /// <see cref="WarpSafeEvaluator"/> to keep an active burn from
        /// being demoted to on-rails mid-throttle (PHYS-3).
        /// </summary>
        public bool ThrustActive { get; set; }

        /// <summary>
        /// Pilot-throttle command in [0..1]. Written by the input channel
        /// (M1-T08) from the pilot's client; read by the integrator each
        /// tick to drive engine force and propellant consumption.
        /// </summary>
        public double ThrottleCommand { get; set; }

        /// <summary>
        /// Pilot attitude command as a (pitch, yaw, roll) rate triple
        /// in radians per second. Written by the input channel; read by
        /// the integrator and applied as torque on the rigidbody
        /// (M1-T08, Slice 1.2).
        /// </summary>
        public Vector3d AttitudeCommand { get; set; }

        /// <summary>
        /// True iff the vessel is propagated analytically (no bubble, no
        /// rigid body). Backwards-compatible read of
        /// <see cref="State"/>; kept as a property so every existing
        /// caller (warps, POI scanner, persistence, wire codec) compiles
        /// unchanged through the M1 VesselState migration.
        /// </summary>
        public bool OnRails => State == VesselState.OnRails;

        public Vessel(VesselId id, Orbit orbit)
        {
            Id = id;
            Orbit = orbit;
        }
    }
}