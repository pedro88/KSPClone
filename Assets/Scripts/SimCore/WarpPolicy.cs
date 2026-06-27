using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Centralised policy for warp-kind selection and warp-safety checks
    /// (TIME-7). The threshold values are intentionally simple — see
    /// the M0 ticket for the rationale; physics warp keeps active
    /// physics stepping (low multiplier, ≤ 4×); on-rails warp is the
    /// high-multiplier analytic-only mode (≥ 1000×). Anything in
    /// between is rejected so we never silently drop frames.
    /// </summary>
    public static class WarpPolicy
    {
        public const double PhysicsWarpMaxMultiplier = 4.0;
        public const double OnRailsWarpMinMultiplier = 1000.0;

        public static WarpKind ClassifyMultiplier(double multiplier)
        {
            if (multiplier <= 1.0)
                throw new ArgumentOutOfRangeException(nameof(multiplier), "Multiplier must be > 1 to be a warp.");
            if (multiplier <= PhysicsWarpMaxMultiplier) return WarpKind.Physics;
            if (multiplier >= OnRailsWarpMinMultiplier) return WarpKind.OnRails;
            throw new ArgumentOutOfRangeException(nameof(multiplier),
                $"Multiplier {multiplier} is in the unsupported gap (1, {PhysicsWarpMaxMultiplier}] .. [{OnRailsWarpMinMultiplier}, ∞).");
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