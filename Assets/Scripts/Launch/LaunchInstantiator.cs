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
        public int EngineCount { get; }
        public LaunchResult(Vessel vessel, int partCount, double totalMassKg, int engineCount)
        {
            Vessel = vessel; PartCount = partCount; TotalMassKg = totalMassKg; EngineCount = engineCount;
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
            SimWorld world, VesselMassRegistry masses, VesselEngineRegistry engines = null)
        {
            if (design is null) throw new ArgumentNullException(nameof(design));
            if (world is null) throw new ArgumentNullException(nameof(world));

            // Immutable snapshot so concurrent edits can't mutate this instantiation.
            var snapshot = design.Tree.Clone();

            // Pass 1: aggregate mass (dry + propellant), count parts, list engine types + pool.
            double totalMass = 0.0;
            double propellantPool = 0.0;
            int partCount = 0;
            var engineTypes = new System.Collections.Generic.List<PartType>();
            foreach (var nodeId in snapshot.Subtree(snapshot.Root))
            {
                partCount++;
                if (!snapshot.TryGet(nodeId, out var node) || !catalog.TryGet(node.PartType, out var type))
                    continue;
                totalMass += type.DryMassKg + type.PropellantKg;
                propellantPool += type.PropellantKg;
                if (type.IsEngine) engineTypes.Add(type);
            }

            // Pass 2: build engine modules, splitting the shared propellant pool
            // evenly (no per-tank feed plumbing yet — a later slice). All engines
            // thrust along the vessel +Y (matches the seed craft + the +Y-up
            // render/attitude convention, ADR-0019), mounted just below the CoM.
            var engineModules = new System.Collections.Generic.List<EngineModule>();
            double perEngineProp = engineTypes.Count > 0 ? propellantPool / engineTypes.Count : 0.0;
            foreach (var type in engineTypes)
                engineModules.Add(new EngineModule(
                    name: type.Id.Value, thrustNewtons: type.EngineThrustN, ispSeconds: type.EngineIspS,
                    mountLocal: new Vector3d(0, -2, 0), thrustDirLocal: new Vector3d(0, 1, 0),
                    propellantKg: perEngineProp));

            var vessel = new Vessel(VesselId.New(), spawnOrbit);
            world.RegisterVessel(vessel);

            // Placeholder inertia (≈ solid body of this mass); real per-part
            // geometry arrives with the propulsion slice.
            var m = Math.Max(totalMass, 1.0);
            masses?.Set(vessel.Id, new RigidVesselMass(m, m, m, m));
            if (engines != null && engineModules.Count > 0)
                engines.Set(vessel.Id, engineModules);

            return new LaunchResult(vessel, partCount, totalMass, engineModules.Count);
        }
    }
}
