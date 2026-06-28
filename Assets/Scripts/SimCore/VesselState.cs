#nullable enable annotations

namespace KSPClone.SimCore
{
    /// <summary>
    /// Vessel-side flight mode. Drives which propagation path advances
    /// the vessel each tick and whether a bubble owns it.
    ///
    /// - <see cref="OnRails"/>: closed-form Kepler from <see cref="Vessel.Orbit"/>,
    ///   no bubble, no rigid body. Default after seed/restart and after
    ///   demotion (PHYS-3).
    /// - <see cref="ActivePhysics"/>: lives inside a <see cref="PhysicsBubble"/>,
    ///   stepped by the bubble integrator each fixed tick (PHYS-2).
    /// - <see cref="Suspended"/>: frozen at a snapshot, no bubble, vessel
    ///   clock paused (SUSP-3). Resumed on player load.
    /// </summary>
    public enum VesselState
    {
        OnRails,
        ActivePhysics,
        Suspended
    }
}