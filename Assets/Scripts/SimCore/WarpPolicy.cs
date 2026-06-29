using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Centralised policy for warp-kind selection and warp-safety checks
    /// (TIME-7). The single threshold is the physics ceiling: at or below
    /// <see cref="PhysicsWarpMaxMultiplier"/> physics keeps stepping
    /// (bounded by the 60 Hz integrator); above it, warp is on-rails
    /// (analytic conics, cost-independent of the multiplier, so any rate
    /// goes). The bands are contiguous — there is no rejected gap
    /// (ADR-0010, amended).
    /// </summary>
    public static class WarpPolicy
    {
        public const double PhysicsWarpMaxMultiplier = 4.0;

        public static WarpKind ClassifyMultiplier(double multiplier)
        {
            if (multiplier <= 1.0)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be > 1 to be a warp.");
            return multiplier <= PhysicsWarpMaxMultiplier ? WarpKind.Physics : WarpKind.OnRails;
        }

        /// <summary>
        /// All in-scope vessels must be warp-safe for an OnRails warp to
        /// activate (TIME-7 + Constitution Art. 3). In M0 every vessel
        /// is on-rails by default; the check is a safety gate for later
        /// milestones where active-physics craft must be excluded.
        /// </summary>
        public static bool AllWarpSafe(IEnumerable<Vessel> vessels)
        {
            foreach (var v in vessels)
                if (!v.OnRails) return false;
            return true;
        }
    }
}