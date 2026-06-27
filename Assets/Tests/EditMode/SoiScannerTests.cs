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
        /// Reference crossing finder: fine sample over the look-ahead and
        /// pick the t at which distance-to-target first dips below the
        /// SOI radius (entry) or rises above it (exit). Used to compare
        /// against the analytical scanner.
        /// </summary>
        private static double? ReferenceCrossing(
            Vessel vessel, BodyRegistry registry, CelestialBody target,
            double fromT, double toT, bool entry)
        {
            double? prevT = null;
            double? prevDist = null;
            var steps = 200_000;
            for (int i = 0; i <= steps; i++)
            {
                var t = fromT + (toT - fromT) * i / steps;
                var (vesselPos, _) = KeplerPropagator.StateAt(vessel.Orbit, t, registry);
                var targetPos = registry.WorldPositionOf(target.Id, t);
                var dist = (vesselPos - targetPos).Length;
                if (prevDist is double pd && prevT is double pt)
                {
                    if (entry && pd > target.SoiRadius && dist <= target.SoiRadius) return t;
                    if (!entry && pd < target.SoiRadius && dist >= target.SoiRadius) return t;
                }
                prevT = t;
                prevDist = dist;
            }
            return null;
        }

        [Test]
        public void Scanner_FindsMoonEntry_WithinToleranceOfReference()
        {
            // Place Moon at (MoonOrbitRadius, 0, 0) at t=0. Vessel on a
            // highly elliptic Earth orbit with periapsis = Earth + 300 km
            // and apoapsis just past the Moon's SOI edge.
            var reg = EarthMoonSystem();

            var peri = EarthRadius + 300_000.0;
            var apo  = MoonOrbitRadius - MoonSoiRadius + 1_000_000.0; // barely reaches into Moon's SOI
            var a = 0.5 * (peri + apo);
            var e = (apo - peri) / (apo + peri);

            // Pick M0 such that apoapsis happens near t = 4 hours (Moon is at +x at t=0).
            var mu = EarthMu;
            var T = 2.0 * Math.PI * Math.Sqrt(a * a * a / mu);
            // apoapsis at M=π → t such that M(t) = π. M(t) = n·t (M0=0) → t = π / n = T/2.
            // We want apoapsis near the Moon at t=0: position at apoapsis is at +x. So we want
            // t_apo = T/2 with M(T/2)=π → fine if the orbit reaches apoapsis at T/2.
            var vessel = new Vessel(VesselId.New(),
                new Orbit(a, e, 0, 0, 0, 0, 0, CelestialBodyId.Planet));

            // We look ahead T (one period) starting at t = T/2 - small margin, so the apoapsis pass is captured.
            var lookAhead = T;
            var fromT = T / 2.0 - 600.0;

            var poi = SoiScanner.ScanNext(vessel, reg, fromT, lookAhead);
            Assert.IsNotNull(poi, "Scanner should find a crossing in the look-ahead window.");
            Assert.AreEqual(PoiType.SoiCrossing, poi!.Value.Type);
            Assert.AreEqual(CelestialBodyId.Moon, poi.Value.ToBody);

            var refEntry = ReferenceCrossing(vessel, reg, reg.Get(CelestialBodyId.Moon), fromT, fromT + lookAhead, entry: true);
            Assert.IsNotNull(refEntry, "Reference must find an entry.");
            Assert.AreEqual(refEntry!.Value, poi.Value.GameTime, 1.0,
                $"Scanner time {poi.Value.GameTime:R} must match reference {refEntry.Value:R} within 1 s.");
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