#nullable enable annotations

namespace KSPClone.SimCore
{
    /// <summary>
    /// A control seat on a vessel owning a disjoint set of systems
    /// (CONTEXT: Station; Constitution Art. 6 — control partitioned, never
    /// contended). M1 only routes <see cref="Pilot"/> (throttle + attitude);
    /// the others exist so the wire and the control registry are stable
    /// before the M2 crew layer fills them in.
    /// </summary>
    public enum Station
    {
        /// <summary>Attitude + throttle.</summary>
        Pilot = 0,
        /// <summary>Staging, resources, power, abort.</summary>
        Engineer = 1,
        /// <summary>Maneuver nodes, map / transfer planning.</summary>
        Navigator = 2,
    }
}
