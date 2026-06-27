using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class OrbitTests
    {
        private const double EarthMu = 3.986004418e14;

        [Test]
        public void Orbit_Construct_RoundTripsAllElements()
        {
            var orbit = new Orbit(
                semiMajorAxis: 7_000_000.0,
                eccentricity: 0.1,
                inclination: 0.5,
                longitudeOfAscendingNode: 1.0,
                argumentOfPeriapsis: 2.0,
                meanAnomalyAtEpoch: 0.3,
                epochGameTime: 100.0,
                parentBody: CelestialBodyId.Planet);

            Assert.AreEqual(7_000_000.0, orbit.SemiMajorAxis);
            Assert.AreEqual(0.1, orbit.Eccentricity);
            Assert.AreEqual(0.5, orbit.Inclination);
            Assert.AreEqual(1.0, orbit.LongitudeOfAscendingNode);
            Assert.AreEqual(2.0, orbit.ArgumentOfPeriapsis);
            Assert.AreEqual(0.3, orbit.MeanAnomalyAtEpoch);
            Assert.AreEqual(100.0, orbit.EpochGameTime);
            Assert.AreEqual(CelestialBodyId.Planet, orbit.ParentBody);
        }

        [Test]
        public void Orbit_RejectsNegativeEccentricity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Orbit(
                1.0, -0.1, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
        }

        [Test]
        public void MeanMotion_MatchesSqrtMuOverACubed_ForEarthOrbit()
        {
            var orbit = new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet);
            var expected = System.Math.Sqrt(EarthMu / System.Math.Pow(7_000_000.0, 3));
            Assert.AreEqual(expected, orbit.MeanMotion(EarthMu), 1e-15);
        }

        [Test]
        public void Period_TwoPiOverMeanMotion()
        {
            var orbit = new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet);
            Assert.AreEqual(2.0 * System.Math.PI / orbit.MeanMotion(EarthMu),
                orbit.Period(EarthMu), 1e-12);
        }

        [Test]
        public void Vessel_DefaultsToOnRails()
        {
            var vessel = new Vessel(VesselId.New(),
                new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            Assert.IsTrue(vessel.OnRails, "Constitution Art. 3: on-rails is the default.");
        }
    }
}