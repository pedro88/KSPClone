#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// One engine on a vessel: thrust magnitude, Isp, mount point in
    /// the vessel's local frame, propellant reservoir. M1 treats the
    /// vessel as one rigid body (ADR-0005); the mount point is where
    /// the engine force is applied.
    /// </summary>
    public sealed class EngineModule
    {
        public string Name { get; }
        public double ThrustNewtons { get; set; }
        public double IspSeconds { get; set; }
        public Vector3d MountLocalPosition { get; set; }
        public Vector3d ThrustDirectionLocal { get; set; } // unit vector; world up = vessel up + thrust dir
        public double PropellantMassKg { get; set; }

        public const double G0 = 9.80665;

        public EngineModule(string name, double thrustNewtons, double ispSeconds, Vector3d mountLocal, Vector3d thrustDirLocal, double propellantKg)
        {
            Name = name;
            ThrustNewtons = thrustNewtons;
            IspSeconds = ispSeconds;
            MountLocalPosition = mountLocal;
            ThrustDirectionLocal = thrustDirLocal.Normalized();
            PropellantMassKg = propellantKg;
        }

        /// <summary>
        /// Mass flow at full throttle: ṁ = F / (Isp · g₀).
        /// </summary>
        public double MassFlowAtFullThrottle() => ThrustNewtons / (IspSeconds * G0);

        /// <summary>
        /// Thrust × throttle [0..1]. Returns zero when propellant is empty.
        /// </summary>
        public double EffectiveThrust(double throttle)
        {
            if (throttle <= 0.0 || PropellantMassKg <= 0.0) return 0.0;
            return ThrustNewtons * Math.Clamp(throttle, 0.0, 1.0);
        }
    }

    /// <summary>
    /// Engine list keyed by vessel id. Slice 1.2 populates this when a
    /// Design is instantiated (BUILD-4) or seeded by the demo
    /// vessel in M1.
    /// </summary>
    public sealed class VesselEngineRegistry
    {
        private readonly Dictionary<VesselId, List<EngineModule>> _byVessel = new();

        public IReadOnlyList<EngineModule>? EnginesFor(VesselId id)
            => _byVessel.TryGetValue(id, out var list) ? list : null;

        public void Set(VesselId id, IEnumerable<EngineModule> engines)
            => _byVessel[id] = new List<EngineModule>(engines);

        public void Clear(VesselId id) => _byVessel.Remove(id);

        /// <summary>
        /// Apply one tick of mass flow at the given throttle, summed
        /// across every engine. Returns the total delta-v applied via
        /// Tsiolkovsky (informational; the integrator applies the
        /// actual force).
        /// </summary>
        public double ConsumePropellant(VesselId id, double throttle, double dtSeconds, VesselMassRegistry massRegistry)
        {
            var list = EnginesFor(id);
            if (list is null || list.Count == 0) return 0.0;
            var mass = massRegistry.Get(id);
            if (mass is null) return 0.0;

            double totalFlow = 0.0;
            double totalIspWeightedFlow = 0.0;
            double m0 = mass.MassKg;
            foreach (var e in list)
            {
                if (throttle <= 0.0 || e.PropellantMassKg <= 0.0) continue;
                var flow = e.MassFlowAtFullThrottle() * throttle;
                var burnt = Math.Min(flow * dtSeconds, e.PropellantMassKg);
                e.PropellantMassKg -= burnt;
                totalFlow += burnt;
                totalIspWeightedFlow += burnt * e.IspSeconds;
            }

            var m1 = m0 - totalFlow;
            if (m1 < 1.0) m1 = 1.0;
            mass.MassKg = m1;

            // Crude inertia scaling: I ∝ m (treating the vessel as a point mass with
            // its principal axes scaled to a uniform sphere). Slice 1.5 will keep
            // the proper inertia tensor across stage splits.
            var ratio = m1 / m0;
            mass.InertiaPrincipalX *= ratio;
            mass.InertiaPrincipalY *= ratio;
            mass.InertiaPrincipalZ *= ratio;

            if (totalFlow <= 0.0 || m1 >= m0) return 0.0;
            // Effective Isp weighted by mass flow: <Isp> = Σ(ṁᵢ·Ispᵢ) / Σ(ṁᵢ)
            var ispEff = totalIspWeightedFlow / totalFlow;
            return ispEff * EngineModule.G0 * Math.Log(m0 / m1); // Tsiolkovsky Δv
        }
    }
}