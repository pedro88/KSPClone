#nullable enable annotations

using System;
using KSPClone.Construction;
using KSPClone.SimCore;

namespace KSPClone.Launch
{
    /// <summary>What a launch produced, for callers/tests.</summary>
    public readonly struct LaunchResult
    {
        public Vessel Vessel { get; }
        public int PartCount { get; }
        public double TotalMassKg { get; }
        public LaunchResult(Vessel vessel, int partCount, double totalMassKg)
        {
            Vessel = vessel; PartCount = partCount; TotalMassKg = totalMassKg;
        }
    }

    /// <summary>
    /// The one-way boundary from the design-time system into flight (M3-T08,
    /// BUILD-4, Constitution Art. 7). This is the <b>only</b> place the
    /// Construction and flight (SimCore) worlds meet: it reads an immutable
    /// snapshot of a <see cref="Design"/> and instantiates a new <see cref="Vessel"/>
    /// in the world, leaving the Design untouched. It lives in its own assembly
    /// that references both — neither SimCore nor Construction references the
    /// other.
    /// </summary>
    public static class LaunchInstantiator
    {
        /// <summary>
        /// Instantiate <paramref name="design"/> as a new Vessel at
        /// <paramref name="spawnOrbit"/> (the pad), registered in
        /// <paramref name="world"/> with its aggregate mass. The Vessel starts
        /// on-rails at the pad with unoccupied stations (occupancy is empty by
        /// default) and zero relative velocity; promotion to active-physics
        /// happens via the normal path when a player loads it. The source Design's
        /// tree and op seq are not modified.
        /// </summary>
        public static LaunchResult Launch(
            Design design, PartCatalog catalog, Orbit spawnOrbit,
            SimWorld world, VesselMassRegistry masses)
        {
            if (design is null) throw new ArgumentNullException(nameof(design));
            if (world is null) throw new ArgumentNullException(nameof(world));

            // Immutable snapshot so concurrent edits can't mutate this instantiation.
            var snapshot = design.Tree.Clone();

            double totalMass = 0.0;
            int partCount = 0;
            foreach (var nodeId in snapshot.Subtree(snapshot.Root))
            {
                partCount++;
                if (snapshot.TryGet(nodeId, out var node) &&
                    catalog.TryGet(node.PartType, out var type))
                    totalMass += type.DryMassKg;
            }

            var vessel = new Vessel(VesselId.New(), spawnOrbit);
            world.RegisterVessel(vessel);

            // Placeholder inertia (≈ solid body of this mass); real per-part
            // geometry + resources arrive with the propulsion slice.
            var m = Math.Max(totalMass, 1.0);
            masses?.Set(vessel.Id, new RigidVesselMass(m, m, m, m));

            return new LaunchResult(vessel, partCount, totalMass);
        }
    }
}
