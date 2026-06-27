using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class WorldHandshakeTests
    {
        private const double EarthMu = 3.986004418e14;

        [Test]
        public void Handshake_CarriesCurrentGameTime_AndEveryVessel()
        {
            var reg = new BodyRegistry(new[]
            {
                new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root),
            });
            var world = new SimWorld(reg);
            var orbit = new Orbit(7e6, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet);
            world.RegisterVessel(new Vessel(VesselId.New(), orbit));
            world.RegisterVessel(new Vessel(VesselId.New(), orbit));

            var msg = new WorldHandshakeBuilder(world).Build();
            Assert.AreEqual(0.0, msg.GameTimeSeconds);
            Assert.AreEqual(2, msg.Vessels.Count);
            foreach (var v in msg.Vessels)
                Assert.IsTrue(v.OnRails);
        }

        [Test]
        public void ClientWorldModel_AppliesHandshake_AndExposesVessels()
        {
            var reg = new BodyRegistry(new[]
            {
                new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root),
            });
            var world = new SimWorld(reg);
            var v1 = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var v2 = new Vessel(VesselId.New(), new Orbit(8e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            world.RegisterVessel(v1);
            world.RegisterVessel(v2);

            var client = new ClientWorldModel();
            client.ApplyHandshake(new WorldHandshakeBuilder(world).Build());

            Assert.AreEqual(2, client.Vessels.Count);
            Assert.IsTrue(client.Vessels.ContainsKey(v1.Id));
            Assert.IsTrue(client.Vessels.ContainsKey(v2.Id));
        }

        [Test]
        public void Handshake_GameTime_IsWithinOneTick_OfServerAtBuildTime()
        {
            var reg = new BodyRegistry(new[]
            {
                new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root),
            });
            var world = new SimWorld(reg);
            var scheduler = new SimScheduler(world);
            scheduler.Advance(SimScheduler.FixedDt); // game-time = 1/60 s
            var serverNow = world.Clock.GameTimeSeconds;
            var msg = new WorldHandshakeBuilder(world).Build();
            Assert.AreEqual(serverNow, msg.GameTimeSeconds, 1e-12);
        }
    }
}