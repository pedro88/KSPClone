using NUnit.Framework;
using KSPClone.Persistence;
using KSPClone.SimCore;

namespace KSPClone.Persistence.Tests
{
    /// <summary>
    /// Integration tests for <see cref="WorldRepository"/> against the
    /// local Postgres instance (greenu_test database). The tests are
    /// skipped if the connection cannot be established.
    ///
    /// Connection string: same as the local dev container on port 5433.
    /// The container's user is greenu with password greenu.
    /// </summary>
    public sealed class WorldRepositoryTests
    {
        private const string ConnectionString =
            "Host=localhost;Port=5433;Username=greenu;Password=greenu;Database=greenu_test";

        private static bool _connectionAvailable;

        [OneTimeSetUp]
        public void ProbeConnection()
        {
            try
            {
                var repo = new WorldRepository(ConnectionString);
                repo.Migrate();
                _connectionAvailable = true;
            }
            catch
            {
                _connectionAvailable = false;
            }
        }

        [SetUp]
        public void Reset()
        {
            if (!_connectionAvailable) return;
            new WorldRepository(ConnectionString).Truncate();
        }

        [Test]
        public void UpsertVessel_RoundTrips_AllOrbitalElements()
        {
            if (!_connectionAvailable) Assert.Ignore("Postgres not reachable on localhost:5433.");

            var repo = new WorldRepository(ConnectionString);
            var orbit = new Orbit(
                semiMajorAxis: 7_000_000.5,
                eccentricity: 0.123456789012345,
                inclination: 0.5,
                longitudeOfAscendingNode: 1.2345,
                argumentOfPeriapsis: 2.3456,
                meanAnomalyAtEpoch: 0.111111111,
                epochGameTime: 100.0,
                parentBody: CelestialBodyId.Planet);
            var vessel = new Vessel(VesselId.New(), orbit) { VesselClockSeconds = 105.0 };

            repo.UpsertVessel(vessel);
            var loaded = new List<(VesselId, Orbit, double, bool)>();
            foreach (var row in repo.LoadVessels()) loaded.Add(row);
            Assert.AreEqual(1, loaded.Count);
            var (id, orb, vc, onRails) = loaded[0];
            Assert.AreEqual(vessel.Id.Value, id.Value);
            Assert.AreEqual(orbit.SemiMajorAxis, orb.SemiMajorAxis, 1e-12);
            Assert.AreEqual(orbit.Eccentricity, orb.Eccentricity, 1e-15);
            Assert.AreEqual(orbit.Inclination, orb.Inclination, 1e-15);
            Assert.AreEqual(orbit.LongitudeOfAscendingNode, orb.LongitudeOfAscendingNode, 1e-15);
            Assert.AreEqual(orbit.ArgumentOfPeriapsis, orb.ArgumentOfPeriapsis, 1e-15);
            Assert.AreEqual(orbit.MeanAnomalyAtEpoch, orb.MeanAnomalyAtEpoch, 1e-15);
            Assert.AreEqual(orbit.EpochGameTime, orb.EpochGameTime, 1e-15);
            Assert.AreEqual(vc, 105.0, 1e-12);
            Assert.IsTrue(onRails);
        }

        [Test]
        public void UpsertVessel_OnSecondCall_UpdatesSameRow()
        {
            if (!_connectionAvailable) Assert.Ignore("Postgres not reachable on localhost:5433.");

            var repo = new WorldRepository(ConnectionString);
            var vessel = new Vessel(VesselId.New(),
                new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            repo.UpsertVessel(vessel);
            vessel.VesselClockSeconds = 999.0;
            repo.UpsertVessel(vessel);

            int count = 0;
            double lastVc = 0.0;
            foreach (var row in repo.LoadVessels()) { count++; lastVc = row.Item3; }
            Assert.AreEqual(1, count, "Upsert must not duplicate the row.");
            Assert.AreEqual(999.0, lastVc, 1e-12);
        }

        [Test]
        public void UpsertClock_RoundTrips_GameTime_AndWarpRate()
        {
            if (!_connectionAvailable) Assert.Ignore("Postgres not reachable on localhost:5433.");

            var repo = new WorldRepository(ConnectionString);
            repo.UpsertClock(gameTime: 12345.6789, warpRate: 100.0);
            var c = repo.LoadClock();
            Assert.IsTrue(c.HasValue);
            Assert.AreEqual(12345.6789, c!.Value.gameTime, 1e-12);
            Assert.AreEqual(100.0, c.Value.warpRate, 1e-12);
        }

        [Test]
        public void LoadVessels_OnEmptyTable_ReturnsNothing()
        {
            if (!_connectionAvailable) Assert.Ignore("Postgres not reachable on localhost:5433.");

            var repo = new WorldRepository(ConnectionString);
            int count = 0;
            foreach (var _ in repo.LoadVessels()) count++;
            Assert.AreEqual(0, count);
        }
    }
}