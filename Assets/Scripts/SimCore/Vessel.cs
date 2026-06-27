namespace KSPClone.SimCore
{
    public sealed class Vessel
    {
        public VesselId Id { get; }
        public Orbit Orbit { get; set; }
        public bool OnRails { get; set; } = true;

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

        public Vessel(VesselId id, Orbit orbit)
        {
            Id = id;
            Orbit = orbit;
        }
    }
}