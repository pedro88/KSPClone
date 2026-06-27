using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class OnRailsSyncTests
    {
        private const double EarthMu = 3.986004418e14;

        private static BodyRegistry EarthMoon()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            var moon  = new CelestialBody(CelestialBodyId.Moon,   "Moon",  4.9048695e12, 66_100_000.0, CelestialBodyId.Planet);
            return new BodyRegistry(new[] { earth, moon });
        }

        private static Orbit SeedOrbit()
        {
            return new Orbit(
                semiMajorAxis: 6_871_000.0,
                eccentricity: 0.0,
                inclination: 0.0,
                longitudeOfAscendingNode: 0.0,
                argumentOfPeriapsis: 0.0,
                meanAnomalyAtEpoch: 0.0,
                epochGameTime: 0.0,
                parentBody: CelestialBodyId.Planet);
        }

        [Test]
        public void OnRailsVessel_VesselClockEqualsMasterClock_AfterTick()
        {
            var world = new SimWorld(EarthMoon());
            var vessel = new Vessel(VesselId.New(), SeedOrbit());
            world.RegisterVessel(vessel);

            var scheduler = new SimScheduler(world);
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);

            Assert.AreEqual(world.Clock.GameTimeSeconds, vessel.VesselClockSeconds, 1e-12,
                "On-rails vessel clock must equal the master clock (Constitution Art. 4).");
        }

        [Test]
        public void OnRailsVessel_CachedState_MatchesKeplerDirectCall()
        {
            var world = new SimWorld(EarthMoon());
            var vessel = new Vessel(VesselId.New(), SeedOrbit());
            world.RegisterVessel(vessel);

            var scheduler = new SimScheduler(world);
            scheduler.Advance(123.0);

            var (directPos, directVel) = KeplerPropagator.StateAt(vessel.Orbit, 123.0, EarthMoon());
            Assert.IsTrue(vessel.CachedWorldPosition.HasValue);
            Assert.IsTrue(vessel.CachedWorldVelocity.HasValue);
            Assert.AreEqual(0.0, (vessel.CachedWorldPosition.Value - directPos).Length, 1e-6);
            Assert.AreEqual(0.0, (vessel.CachedWorldVelocity.Value - directVel).Length, 1e-9);
        }

        [Test]
        public void OnRailsSync_RunsWithZeroClients_UniverseLivesWhenEmpty()
        {
            var world = new SimWorld(EarthMoon());
            var vessel = new Vessel(VesselId.New(), SeedOrbit());
            world.RegisterVessel(vessel);

            var scheduler = new SimScheduler(world);
            // Advance 1 game-day without anyone connected.
            for (int i = 0; i < 86400; i++) scheduler.Advance(1.0);

            Assert.AreEqual(86400.0, world.Clock.GameTimeSeconds, 1.0);
            Assert.AreEqual(86400.0, vessel.VesselClockSeconds, 1.0);
            Assert.IsTrue(vessel.CachedWorldPosition.HasValue,
                "Universe must stay synced: cache populated even with zero players (Art. 4).");
        }

        [Test]
        public void PhysicsVessel_IsNotSynced_ByRailsLoop()
        {
            var world = new SimWorld(EarthMoon());
            var vessel = new Vessel(VesselId.New(), SeedOrbit()) { OnRails = false };
            world.RegisterVessel(vessel);

            var scheduler = new SimScheduler(world);
            scheduler.Advance(10.0);

            Assert.AreEqual(0.0, vessel.VesselClockSeconds,
                "An active-physics vessel is driven by the physics bubble, not the on-rails sync (M1).");
            Assert.IsFalse(vessel.CachedWorldPosition.HasValue);
        }
    }
}