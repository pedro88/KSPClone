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