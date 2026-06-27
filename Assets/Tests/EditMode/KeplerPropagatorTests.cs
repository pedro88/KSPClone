using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class KeplerPropagatorTests
    {
        private const double EarthMu = 3.986004418e14;
        private const double MoonMu  = 4.9048695e12;

        private static BodyRegistry MakeEarthMoonRegistry()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            var moon  = new CelestialBody(CelestialBodyId.Moon,   "Moon",  MoonMu,   66_100_000.0,  CelestialBodyId.Planet);
            return new BodyRegistry(new[] { earth, moon });
        }

        private static Orbit CircularEarthOrbit(double altitude = 500_000.0, double meanAnomAtEpoch = 0.0)
        {
            var earthRadius = 6_371_000.0;
            return new Orbit(
                semiMajorAxis: earthRadius + altitude,
                eccentricity: 0.0,
                inclination: 0.0,
                longitudeOfAscendingNode: 0.0,
                argumentOfPeriapsis: 0.0,
                meanAnomalyAtEpoch: meanAnomAtEpoch,
                epochGameTime: 0.0,
                parentBody: CelestialBodyId.Planet);
        }

        [Test]
        public void CircularOrbit_PositionMagnitude_ConstantOverTime()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = CircularEarthOrbit();

            for (int i = 0; i < 360; i++)
            {
                var (p, _) = KeplerPropagator.StateAt(orbit, i * 60.0, reg);
                Assert.AreEqual(orbit.SemiMajorAxis, p.Length, 1e-6,
                    $"At t={i * 60.0}s circular orbit must keep |r| = a.");
            }
        }

        [Test]
        public void CircularOrbit_VelocityMagnitude_EqualsSqrtMuOverR()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = CircularEarthOrbit();
            var (_, v) = KeplerPropagator.StateAt(orbit, 0.0, reg);
            var expected = Math.Sqrt(EarthMu / orbit.SemiMajorAxis);
            Assert.AreEqual(expected, v.Length, 1e-6);
        }

        [Test]
        public void EllipticOrbit_AtPeriapsisAndApoapsis_PositionsMatchRadii()
        {
            var reg = MakeEarthMoonRegistry();
            // Elliptic orbit around Earth: rp = 7e6, ra = 12e6
            var rp = 7_000_000.0;
            var ra = 12_000_000.0;
            var a  = 0.5 * (rp + ra);
            var e  = (ra - rp) / (ra + rp);
            var orbit = new Orbit(a, e, 0, 0, 0, 0, 0, CelestialBodyId.Planet);

            // At t=0, M=0 → E=0 → ν=0 → at periapsis
            var (pPeri, _) = KeplerPropagator.StateAt(orbit, 0.0, reg);
            Assert.AreEqual(rp, pPeri.Length, 1e-6, "At M=0 the vessel is at periapsis.");

            // Half a period later: apoapsis
            var T = orbit.Period(EarthMu);
            var (pApo, _) = KeplerPropagator.StateAt(orbit, T / 2.0, reg);
            Assert.AreEqual(ra, pApo.Length, 1e-6, "At M=π the vessel is at apoapsis.");
        }

        [Test]
        public void MultiPeriodJump_StateReturnsToSamePoint_WithinTolerance()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = CircularEarthOrbit(meanAnomAtEpoch: 0.3);
            var T = orbit.Period(EarthMu);

            var (p0, _) = KeplerPropagator.StateAt(orbit, 0.0, reg);
            var (pN, _) = KeplerPropagator.StateAt(orbit, 17.0 * T, reg);
            Assert.AreEqual(0.0, (pN - p0).Length, 1.0,
                $"After 17 periods the vessel must be at the same point within 1 m (got |Δr| = {(pN - p0).Length:R} m).");
        }

        [Test]
        public void MultiDayJump_PositionIsFinite_And_StaysOnOrbit()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = CircularEarthOrbit();
            var day = 86_400.0;

            var (p, v) = KeplerPropagator.StateAt(orbit, 30.0 * day, reg);
            Assert.IsFalse(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsNaN(p.Z));
            Assert.IsFalse(double.IsNaN(v.X) || double.IsNaN(v.Y) || double.IsNaN(v.Z));
            Assert.AreEqual(orbit.SemiMajorAxis, p.Length, 1e-3,
                "After 30 days, the circular orbit radius must hold within 1 mm.");
        }

        [Test]
        public void HighEccentricity_KeplerSolver_ConvergesWithPiSeed()
        {
            var reg = MakeEarthMoonRegistry();
            var a = 10_000_000.0;
            var e = 0.9;
            var orbit = new Orbit(a, e, 0, 0, 0, 0.5, 0, CelestialBodyId.Planet);

            // Just check it doesn't throw and returns a finite state at several times.
            for (int i = 0; i < 20; i++)
            {
                var (p, _) = KeplerPropagator.StateAt(orbit, i * 1000.0, reg);
                Assert.IsFalse(double.IsNaN(p.X));
            }
        }

        [Test]
        public void HyperbolicOrbit_Throws_NotSupportedInM0()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = new Orbit(-10_000_000.0, 1.5, 0, 0, 0, 0, 0, CelestialBodyId.Planet);
            Assert.Throws<NotSupportedException>(() => KeplerPropagator.StateAt(orbit, 0.0, reg));
        }

        [Test]
        public void StateAt_DoesNotStepIntegration_SingleEvaluation()
        {
            // Constitution Art. 3: closed-form position(t), no integration.
            // We can't instrument the kernel directly here, but we can
            // assert that StateAt is O(1) in dt — verify by calling at
            // very large dt and confirming the result matches the closed-
            // form value at a small dt computed independently.
            var reg = MakeEarthMoonRegistry();
            var orbit = CircularEarthOrbit(meanAnomAtEpoch: 0.0);
            var T = orbit.Period(EarthMu);

            // Closed-form: ν(t) = n·t (mod 2π) for a circular orbit.
            var (pSmall, _) = KeplerPropagator.StateAt(orbit, 1.0, reg);
            var (pBig, _)   = KeplerPropagator.StateAt(orbit, 1.0 + 1000.0 * T, reg);
            Assert.AreEqual(0.0, (pSmall - pBig).Length, 1.0,
                "Same phase modulo T must give the same position within 1 m, regardless of absolute dt.");
        }

        [Test]
        public void Energy_IsConserved_OverClosedOrbit()
        {
            var reg = MakeEarthMoonRegistry();
            var orbit = new Orbit(10_000_000.0, 0.3, 0, 0, 0, 0, 0, CelestialBodyId.Planet);
            // Specific orbital energy ε = v²/2 - μ/r. Must be constant
            // around the orbit for a closed Keplerian conic.
            double eps0 = double.NaN;
            for (int i = 0; i < 100; i++)
            {
                var t = i * 60.0;
                var (p, v) = KeplerPropagator.StateAt(orbit, t, reg);
                var eps = 0.5 * v.LengthSquared - EarthMu / p.Length;
                if (i == 0) eps0 = eps;
                Assert.AreEqual(eps0, eps, 1e-3,
                    $"Specific orbital energy must be conserved (t={t}s).");
            }
        }
    }
}