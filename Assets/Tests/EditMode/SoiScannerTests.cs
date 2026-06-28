#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SoiScannerTests
    {
        private const double EarthMu = 3.986004418e14;
        private const double MoonMu  = 4.9048695e12;

        private const double EarthRadius = 6_371_000.0;
        private const double MoonOrbitRadius = 384_400_000.0;
        private const double MoonSoiRadius = 66_100_000.0;
        private const double EarthSoiRadius = 924_000_000.0;

        private static BodyRegistry EarthMoonSystem()
        {
            var earth = new CelestialBody(
                CelestialBodyId.Planet, "Earth",
                EarthMu, EarthSoiRadius, CelestialBodyId.Root);
            var moon = new CelestialBody(
                CelestialBodyId.Moon, "Moon",
                MoonMu, MoonSoiRadius, CelestialBodyId.Planet,
                // Moon's orbit around Earth (real numbers, circular equatorial).
                orbitAroundParent: new Orbit(
                    semiMajorAxis: MoonOrbitRadius,
                    eccentricity: 0.0,
                    inclination: 0.0,
                    longitudeOfAscendingNode: 0.0,
                    argumentOfPeriapsis: 0.0,
                    meanAnomalyAtEpoch: 0.0,
                    epochGameTime: 0.0,
                    parentBody: CelestialBodyId.Planet));
            return new BodyRegistry(new[] { earth, moon });
        }

        /// <summary>
        /// Builds an Earth orbit whose apoapsis is aimed at the Moon's
        /// position at <c>encounterTime</c>, with apoapsis just inside the
        /// Moon's SOI. This produces a real, bound (near-apoapsis, low-speed)
        /// lunar encounter that M0's elliptic-only propagator can represent.
        /// </summary>
        private static (Orbit orbit, double encounterTime) MoonInterceptOrbit()
        {
            const double encounterTime = 200_000.0;
            var moonMeanMotion = Math.Sqrt(EarthMu / (MoonOrbitRadius * MoonOrbitRadius * MoonOrbitRadius));
            var moonAngle = moonMeanMotion * encounterTime;          // Moon circular, M0 = 0
            var peri = EarthRadius + 300_000.0;
            var apo  = MoonOrbitRadius - MoonSoiRadius * 0.5;         // apoapsis just inside Moon SOI
            var a = 0.5 * (peri + apo);
            var e = (apo - peri) / (apo + peri);
            var meanMotion = Math.Sqrt(EarthMu / (a * a * a));
            var argp = KeplerPropagator.WrapTwoPi(moonAngle - Math.PI);                  // apoapsis toward the Moon
            var m0   = KeplerPropagator.WrapTwoPi(Math.PI - meanMotion * encounterTime); // apoapsis at encounterTime
            return (new Orbit(a, e, 0.0, 0.0, argp, m0, 0.0, CelestialBodyId.Planet), encounterTime);
        }

        /// <summary>
        /// Reference crossing finder: coarse-samples the look-ahead to bracket
        /// the first entry/exit of the target SOI, then bisects the bracket to
        /// sub-second accuracy. Used to compare against the analytical scanner.
        /// </summary>
        private static double? ReferenceCrossing(
            Vessel vessel, BodyRegistry registry, CelestialBody target,
            double fromT, double toT, bool entry)
        {
            double F(double t)
            {
                var (vesselPos, _) = KeplerPropagator.StateAt(vessel.Orbit, t, registry);
                return (vesselPos - registry.WorldPositionOf(target.Id, t)).Length - target.SoiRadius;
            }

            var steps = 200_000;
            double? prevT = null;
            double? prevF = null;
            for (int i = 0; i <= steps; i++)
            {
                var t = fromT + (toT - fromT) * i / steps;
                var f = F(t);
                if (prevF is double pf && prevT is double pt)
                {
                    var crosses = entry ? (pf > 0 && f <= 0) : (pf < 0 && f >= 0);
                    if (crosses)
                    {
                        double a = pt, b = t, fa = pf;
                        for (int k = 0; k < 60 && b - a > 1e-3; k++)
                        {
                            var m = 0.5 * (a + b);
                            var fm = F(m);
                            if ((fm > 0) == (fa > 0)) { a = m; fa = fm; } else { b = m; }
                        }
                        return 0.5 * (a + b);
                    }
                }
                prevT = t;
                prevF = f;
            }
            return null;
        }

        [Test]
        public void Scanner_FindsMoonEntry_WithinToleranceOfReference()
        {
            // Vessel on a transfer orbit whose apoapsis meets the Moon inside
            // its SOI: a real entry crossing exists in [0, encounterTime].
            var reg = EarthMoonSystem();
            var (orbit, encounterTime) = MoonInterceptOrbit();
            var vessel = new Vessel(VesselId.New(), orbit);

            var lookAhead = encounterTime;
            var poi = SoiScanner.ScanNext(vessel, reg, 0.0, lookAhead);
            Assert.IsNotNull(poi, "Scanner should find a crossing in the look-ahead window.");
            Assert.AreEqual(PoiType.SoiCrossing, poi!.Value.Type);
            Assert.AreEqual(CelestialBodyId.Moon, poi.Value.ToBody);

            var refEntry = ReferenceCrossing(vessel, reg, reg.Get(CelestialBodyId.Moon), 0.0, lookAhead, entry: true);
            Assert.IsNotNull(refEntry, "Reference must find an entry.");
            Assert.AreEqual(refEntry!.Value, poi.Value.GameTime, 2.0,
                $"Scanner time {poi.Value.GameTime:R} must match reference {refEntry.Value:R} within 2 s.");
        }

        [Test]
        public void Scanner_DoesNotFire_WhenVesselStaysInsideEarthSoi()
        {
            var reg = EarthMoonSystem();
            // Low circular orbit around Earth — never approaches Moon.
            var vessel = new Vessel(VesselId.New(),
                new Orbit(EarthRadius + 400_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var poi = SoiScanner.ScanNext(vessel, reg, 0.0, lookAheadSeconds: 86400.0);
            // Might still find leaving-Earth-SOI if the period is right; with low orbit it stays well inside.
            Assert.IsTrue(poi is null || poi.Value.ToBody != CelestialBodyId.Moon);
        }
    }
}