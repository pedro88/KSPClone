using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class WarpPolicyTests
    {
        private const double EarthMu = 3.986004418e14;

        private static BodyRegistry EarthOnly()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            return new BodyRegistry(new[] { earth });
        }

        [Test]
        public void ClassifyMultiplier_LowIsPhysics_HighIsOnRails_MiddleRejected()
        {
            Assert.AreEqual(WarpKind.Physics, WarpPolicy.ClassifyMultiplier(2.0));
            Assert.AreEqual(WarpKind.Physics, WarpPolicy.ClassifyMultiplier(WarpPolicy.PhysicsWarpMaxMultiplier));
            Assert.AreEqual(WarpKind.OnRails, WarpPolicy.ClassifyMultiplier(WarpPolicy.OnRailsWarpMinMultiplier));
            Assert.AreEqual(WarpKind.OnRails, WarpPolicy.ClassifyMultiplier(100_000.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => WarpPolicy.ClassifyMultiplier(10.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => WarpPolicy.ClassifyMultiplier(500.0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => WarpPolicy.ClassifyMultiplier(1.0));
        }

        [Test]
        public void AllWarpSafe_DefaultsTrue_ForOnRailsVessels()
        {
            var a = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var b = new Vessel(VesselId.New(), new Orbit(10e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            Assert.IsTrue(WarpPolicy.AllWarpSafe(new[] { a, b }));
        }

        [Test]
        public void AllWarpSafe_False_IfAnyVesselIsActivePhysics()
        {
            var onRails = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var active  = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet)) { OnRails = false };
            Assert.IsFalse(WarpPolicy.AllWarpSafe(new[] { onRails, active }));
        }

        [Test]
        public void OnRailsWarpAt1000x_AdvancesClockWithoutDriftingOrbit()
        {
            var reg = EarthOnly();
            var world = new SimWorld(reg);
            var vessel = new Vessel(VesselId.New(),
                new Orbit(10_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            world.RegisterVessel(vessel);

            var conns = new ConnectionRegistry();
            var session = conns.AddNew();
            var fsm = new WarpStateMachine(world.Clock, conns);

            // Request x1000 on-rails warp; solo, so it goes Active immediately.
            var warpStartGameTime = world.Clock.GameTimeSeconds;
            Assert.IsTrue(fsm.RequestWarp(new WarpRequest(session.Id, 1000.0, WarpKind.OnRails)));
            Assert.AreEqual(WarpState.Active, fsm.State);
            Assert.AreEqual(1000.0, world.Clock.Rate);

            // Advance the scheduler by 1 s of wall-time, in FixedDt chunks so the
            // spiral-of-death clamp never trims it (a single Advance(1.0) would).
            var scheduler = new SimScheduler(world);
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);

            var endGameTime = world.Clock.GameTimeSeconds;
            Assert.AreEqual(1000.0, endGameTime - warpStartGameTime, 1e-9,
                "1 s of wall time at Rate=1000 must advance GameTime by exactly 1000 s.");

            // The vessel's cached state must equal the analytic Kepler
            // evaluation at the new game-time (Constitution Art. 3).
            Assert.IsTrue(vessel.CachedWorldPosition.HasValue);
            var (directPos, _) = KeplerPropagator.StateAt(vessel.Orbit, endGameTime, reg);
            Assert.AreEqual(0.0, (vessel.CachedWorldPosition.Value - directPos).Length, 1e-3,
                "On-rails warp must not drift the vessel off its analytic orbit.");
        }
    }
}