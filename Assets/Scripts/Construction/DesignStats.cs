#nullable enable annotations

using System;

namespace KSPClone.Construction
{
    /// <summary>
    /// Aggregate flight stats of a Design, computed from its part tree + catalog
    /// (engine-agnostic, pure — no world state). Single-stage figures (no
    /// decoupling/staging model yet): Δv uses the full stack's wet→dry mass.
    /// The VAB shows these live as parts are added/removed.
    /// </summary>
    public readonly struct DesignStats
    {
        public const double G0 = 9.80665; // standard gravity, for TWR + Tsiolkovsky

        public int PartCount { get; }
        public double DryMassKg { get; }
        public double PropellantKg { get; }
        public double ThrustN { get; }
        /// <summary>Mass-flow-weighted effective Isp (s) across all engines; 0 if none.</summary>
        public double EffectiveIspS { get; }

        public DesignStats(int partCount, double dryMassKg, double propellantKg, double thrustN, double effectiveIspS)
        {
            PartCount = partCount;
            DryMassKg = dryMassKg;
            PropellantKg = propellantKg;
            ThrustN = thrustN;
            EffectiveIspS = effectiveIspS;
        }

        public double WetMassKg => DryMassKg + PropellantKg;

        /// <summary>Thrust-to-weight ratio at Earth's surface (wet). &lt;1 = can't lift off.</summary>
        public double TwrEarthSurface => ThrustN > 0.0 && WetMassKg > 0.0 ? ThrustN / (WetMassKg * G0) : 0.0;

        /// <summary>Ideal Δv (m/s), Tsiolkovsky over the full wet→dry stack.</summary>
        public double DeltaVMps =>
            (ThrustN > 0.0 && PropellantKg > 0.0 && DryMassKg > 0.0)
                ? EffectiveIspS * G0 * Math.Log(WetMassKg / DryMassKg)
                : 0.0;

        public static DesignStats Compute(PartTree tree, PartCatalog catalog)
        {
            int count = 0;
            double dry = 0, prop = 0, thrust = 0, flow = 0;
            foreach (var id in tree.Subtree(tree.Root))
            {
                count++;
                if (!tree.TryGet(id, out var node) || !catalog.TryGet(node.PartType, out var type)) continue;
                dry += type.DryMassKg;
                prop += type.PropellantKg;
                if (type.IsEngine)
                {
                    thrust += type.EngineThrustN;
                    if (type.EngineIspS > 0.0) flow += type.EngineThrustN / type.EngineIspS;
                }
            }
            double ispEff = flow > 0.0 ? thrust / flow : 0.0;
            return new DesignStats(count, dry, prop, thrust, ispEff);
        }
    }
}
