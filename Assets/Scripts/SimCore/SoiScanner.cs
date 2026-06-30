#nullable enable annotations

using System;

namespace KSPClone.SimCore
{
    /// <summary>
    /// Predicts upcoming SOI-crossing times for on-rails vessels.
    /// Bracket + bisection on f(t) = |r_vessel(t) − r_body(t)| − r_SOI
    /// over a look-ahead window, per orbital-mechanics.md §4.
    /// </summary>
    public static class SoiScanner
    {
        public const double BisectionToleranceSeconds = 1.0;
        public const int    BisectionMaxIters         = 60;

        /// <summary>
        /// Scans the vessel for the next SOI crossing within
        /// [fromGameTime, fromGameTime + lookAheadSeconds]. Returns null
        /// if none found in the window. The vessel's parent SOI is
        /// checked for *leaving*; each sibling/ancestor SOI is checked
        /// for *entering*.
        /// </summary>
        public static Poi? ScanNext(Vessel vessel, BodyRegistry registry, double fromGameTime, double lookAheadSeconds)
        {
            var orbit = vessel.Orbit;
            var parent = registry.Get(orbit.ParentBody);

            Poi? best = null;

            // Leaving parent's SOI.
            var leaveT = FindCrossingTime(vessel, registry, parent, fromGameTime, lookAheadSeconds, leaving: true);
            if (leaveT is double lt && (best is null || lt < best.Value.GameTime))
                best = new Poi(PoiType.SoiCrossing, lt, vessel.Id, parent.Id, CelestialBodyId.Root); // ToBody resolved at apply time

            // Entering any other body's SOI.
            foreach (var kv in registry.Bodies)
            {
                var other = kv.Value;
                if (other.Id == parent.Id) continue;
                var enterT = FindCrossingTime(vessel, registry, other, fromGameTime, lookAheadSeconds, leaving: false);
                if (enterT is double et && (best is null || et < best.Value.GameTime))
                    best = new Poi(PoiType.SoiCrossing, et, vessel.Id, parent.Id, other.Id);
            }

            return best;
        }

        private static double? FindCrossingTime(
            Vessel vessel, BodyRegistry registry, CelestialBody target,
            double fromGameTime, double lookAheadSeconds, bool leaving)
        {
            // Bracket by sampling coarse intervals of mean anomaly. For a
            // typical vessel the orbit period is the natural bracket size.
            var mu = registry.Get(vessel.Orbit.ParentBody).GravParameterMu;
            var period = vessel.Orbit.Period(mu);
            if (double.IsNaN(period) || period <= 0.0) return null;

            var t1 = fromGameTime;
            var t2 = fromGameTime + lookAheadSeconds;
            // Coarse sample: 60 steps per period, up to the look-ahead.
            var coarseSteps = Math.Max(8, (int)Math.Ceiling((t2 - t1) / period * 60.0));
            coarseSteps = Math.Min(coarseSteps, 1024);

            double? prevT = null;
            double? prevF = null;
            for (int i = 0; i <= coarseSteps; i++)
            {
                var t = t1 + (t2 - t1) * i / coarseSteps;
                var f = DistanceMinusSoi(vessel, registry, target, t);
                if (prevF is double pf && prevT is double pt)
                {
                    // Sign change: f crosses zero.
                    if ((pf < 0 && f >= 0) || (pf > 0 && f <= 0))
                    {
                        // Determine which side is "outside" the SOI based on `leaving`.
                        var root = BisectRoot(vessel, registry, target, pt, t);
                        if (root.HasValue && (leaving ? prevF < 0 : prevF > 0))
                            return root;
                    }
                }
                prevT = t;
                prevF = f;
            }
            return null;
        }

        private static double DistanceMinusSoi(Vessel vessel, BodyRegistry registry, CelestialBody target, double t)
        {
            // World-frame position of the vessel (parent world pos + parent-frame
            // state). StateAt alone would return the parent-frame position, which
            // is only equal to world-frame when the parent sits at the origin —
            // true under the M0-T06 static tree, no longer true once Earth
            // orbits the Sun (M2-T12).
            var (_, _, vesselWorldPos, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, t, registry);
            var targetPos = registry.WorldPositionOf(target.Id, t);
            return (vesselWorldPos - targetPos).Length - target.SoiRadius;
        }

        private static double? BisectRoot(Vessel vessel, BodyRegistry registry, CelestialBody target, double a, double b)
        {
            var fa = DistanceMinusSoi(vessel, registry, target, a);
            var fb = DistanceMinusSoi(vessel, registry, target, b);
            if ((fa > 0 && fb > 0) || (fa < 0 && fb < 0)) return null;

            for (int i = 0; i < BisectionMaxIters; i++)
            {
                var m = 0.5 * (a + b);
                var fm = DistanceMinusSoi(vessel, registry, target, m);
                if (Math.Abs(b - a) < BisectionToleranceSeconds || Math.Abs(fm) < 1e-3)
                    return m;
                if ((fm > 0) == (fa > 0)) { a = m; fa = fm; } else { b = m; }
            }
            return 0.5 * (a + b);
        }
    }
}