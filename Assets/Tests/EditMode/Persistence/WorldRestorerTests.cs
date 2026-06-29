#nullable enable annotations

using NUnit.Framework;
using KSPClone.Persistence;
using KSPClone.SimCore;

namespace KSPClone.Persistence.Tests
{
    public sealed class WorldRestorerTests
    {
        private const string ConnectionString =
            "Host=localhost;Port=5433;Username=greenu;Password=greenu;Database=greenu_test";

        private static BodyRegistry EarthMoon()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", 3.986e14, 924_000_000.0, CelestialBodyId.Root);
            var moon  = new CelestialBody(CelestialBodyId.Moon, "Moon", 4.904e12, 66_100_000.0, CelestialBodyId.Planet);
            return new BodyRegistry(new[] { earth, moon });
        }

        [Test]
        public void Restore_FromEmpty_SeedsAndPersists()
        {
            bool dbUp;
            try { new WorldRepository(ConnectionString).Migrate(); dbUp = true; }
            catch { dbUp = false; }
            if (!dbUp) Assert.Ignore("Postgres not reachable.");

            new WorldRepository(ConnectionString).Truncate();

            var restorer = new WorldRestorer(new WorldRepository(ConnectionString));
            var bodies = EarthMoon();
            var world = restorer.RestoreOrSeed(bodies, w =>
            {
                w.RegisterVessel(new Vessel(VesselId.New(),
                    new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet)));
            });

            Assert.AreEqual(1, world.Vessels.Count);

            // A subsequent restart should find this seeded state.
            var again = restorer.RestoreOrSeed(bodies, _ =>
                Assert.Fail("Bootstrap must not run on a non-empty DB."));
            Assert.AreEqual(1, again.Vessels.Count);
        }

        [Test]
        public void Restore_ResumesClock_AndAllVessels()
        {
            bool dbUp;
            try { new WorldRepository(ConnectionString).Migrate(); dbUp = true; }
            catch { dbUp = false; }
            if (!dbUp) Assert.Ignore("Postgres not reachable.");

            var repo = new WorldRepository(ConnectionString);
            repo.Truncate();

            // Persist a known state.
            repo.UpsertClock(gameTime: 12345.6789, warpRate: 1.0);
            var v1 = new Vessel(VesselId.New(),
                new Orbit(7_000_000.5, 0.123456789012345, 0.5, 1.2345, 2.3456, 0.111111111, 100.0, CelestialBodyId.Planet))
            { VesselClockSeconds = 12350.0 };
            var v2 = new Vessel(VesselId.New(),
                new Orbit(10e6, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Moon))
            { VesselClockSeconds = 12345.0 };
            repo.UpsertVessel(v1);
            repo.UpsertVessel(v2);

            var bodies = EarthMoon();
            var restorer = new WorldRestorer(repo);
            var world = restorer.RestoreOrSeed(bodies, _ => Assert.Fail("Should not seed — DB is non-empty."));

            Assert.AreEqual(12345.6789, world.Clock.GameTimeSeconds, 1e-9,
                "Clock game-time must resume from the last persisted value.");
            Assert.AreEqual(1.0, world.Clock.Rate, "Rate always resumes at 1.0.");
            Assert.AreEqual(2, world.Vessels.Count);

            // Find the vessels by their orbital signature.
            Vessel? loaded1 = null, loaded2 = null;
            foreach (var v in world.Vessels.Values)
            {
                if (v.Orbit.ParentBody == CelestialBodyId.Planet) loaded1 = v;
                if (v.Orbit.ParentBody == CelestialBodyId.Moon) loaded2 = v;
            }
            Assert.IsNotNull(loaded1);
            Assert.IsNotNull(loaded2);
            Assert.AreEqual(v1.Orbit.SemiMajorAxis, loaded1!.Orbit.SemiMajorAxis, 1e-12);
            Assert.AreEqual(v1.Orbit.Eccentricity, loaded1.Orbit.Eccentricity, 1e-15);
            Assert.AreEqual(12350.0, loaded1.VesselClockSeconds, 1e-12);
            Assert.AreEqual(10e6, loaded2!.Orbit.SemiMajorAxis, 1e-12);
        }
    }
}