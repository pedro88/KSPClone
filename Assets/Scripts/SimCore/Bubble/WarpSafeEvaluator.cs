#nullable enable annotations

namespace KSPClone.SimCore
{
    /// <summary>
    /// Decides whether a vessel is <c>warp-safe</c>: it can be left to
    /// coast on analytic rails without a player present (PHYS-3, ADR-0002).
    /// In M1 the evaluator is the simple conjunction "no active thrust and
    /// not in atmosphere". Slice 1.2 will tighten the rules once the
    /// integrator reports atmosphere state per tick.
    /// </summary>
    public sealed class WarpSafeEvaluator
    {
        /// <summary>
        /// True when the vessel can be safely demoted to on-rails: it
        /// has no live engine thrust and is not under atmospheric drag.
        /// In M1 <paramref name="underAtmosphere"/> is reported by the
        /// Unity host's integrator once a body has an atmosphere
        /// defined; today the host passes false for every vessel.
        /// </summary>
        public bool IsWarpSafe(Vessel vessel, bool underAtmosphere)
        {
            if (vessel is null) return false;
            if (underAtmosphere) return false;
            if (vessel.ThrustActive) return false;
            return true;
        }
    }
}