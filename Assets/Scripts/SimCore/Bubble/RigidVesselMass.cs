#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Mass + inertia tensor of one rigid vessel. M1 ships a single
    /// scalar mass plus a diagonal inertia tensor (per-axis principal
    /// moments). A future slice may store a full 3×3 tensor once part
    /// trees need asymmetric bodies.
    ///
    /// Per ADR-0005 a vessel is one rigid body: no inter-part flex,
    /// only articulation points (covered by docking in Slice 1.5).
    /// </summary>
    public sealed class RigidVesselMass
    {
        public double MassKg { get; set; }
        public double InertiaPrincipalX { get; set; }
        public double InertiaPrincipalY { get; set; }
        public double InertiaPrincipalZ { get; set; }

        public RigidVesselMass() { }

        public RigidVesselMass(double massKg, double ix, double iy, double iz)
        {
            MassKg = massKg;
            InertiaPrincipalX = ix;
            InertiaPrincipalY = iy;
            InertiaPrincipalZ = iz;
        }
    }

    /// <summary>
    /// Registry of <see cref="RigidVesselMass"/> keyed by vessel id.
    /// Populated by the design / build layer when a Design is launched
    /// (M3, BUILD-4). For M1 the server seeds a default mass from the
    /// vessel's dry mass until a part tree is available.
    /// </summary>
    public sealed class VesselMassRegistry
    {
        private readonly Dictionary<VesselId, RigidVesselMass> _byVessel = new();

        public RigidVesselMass? Get(VesselId id) => _byVessel.TryGetValue(id, out var m) ? m : null;

        public void Set(VesselId id, RigidVesselMass mass) => _byVessel[id] = mass;

        public void Clear(VesselId id) => _byVessel.Remove(id);
    }
}