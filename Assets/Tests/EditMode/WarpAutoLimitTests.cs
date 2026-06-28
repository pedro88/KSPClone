#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class WarpAutoLimitTests
    {
        [Test]
        public void Arm_WithoutPoI_EndTimeIsNull_AndFiringIsSafe()
        {
            var world = new SimWorld();
            var pois = new PoiRegistry();
            var conns = new ConnectionRegistry();
            var fsm = new WarpStateMachine(world.Clock, conns);
            var al = new WarpAutoLimit(world, pois, fsm);

            al.Arm();
            Assert.IsNull(al.EndGameTime);
            al.Tick(); // no-op
            Assert.IsFalse(al.HasFired);
        }

        [Test]
        public void Warp_HaltsExactlyAtPoi_NeverPast_AndFiresCommitted()
        {
            var world = new SimWorld();
            var pois = new PoiRegistry();
            var conns = new ConnectionRegistry();
            var session = conns.AddNew();
            var fsm = new WarpStateMachine(world.Clock, conns);
            var al = new WarpAutoLimit(world, pois, fsm);

            const double poiTime = 1000.0;
            pois.Add(new Poi(PoiType.SoiCrossing, poiTime, VesselId.New(),
                CelestialBodyId.Planet, CelestialBodyId.Moon));

            // Solo request at x100 → goes Active immediately.
            Assert.IsTrue(fsm.RequestWarp(new WarpRequest(session.Id, 100.0, WarpKind.OnRails)));
            al.Arm();
            Assert.AreEqual(poiTime, al.EndGameTime);

            // Run the scheduler: at Rate=100, GameTime advances 100× wall.
            var scheduler = new SimScheduler(world);
            // 10 s of wall time → GameTime goes from 0 to ~1000.
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
            al.Tick();

            // If we overshot, the auto-limit must have snapped us back.
            Assert.IsTrue(al.HasFired, "Auto-limit must have fired by now.");
            Assert.AreEqual(WarpState.Idle, fsm.State);
            Assert.AreEqual(1.0, world.Clock.Rate);
            Assert.AreEqual(poiTime, world.Clock.GameTimeSeconds, SimScheduler.FixedDt,
                $"Clock must halt at the POI time exactly (never past); got {world.Clock.GameTimeSeconds}.");
        }

        [Test]
        public void WarpCommitted_Event_Fires_ExactlyOnce()
        {
            var world = new SimWorld();
            var pois = new PoiRegistry();
            pois.Add(new Poi(PoiType.SoiCrossing, 500.0, VesselId.New(), CelestialBodyId.Planet, CelestialBodyId.Moon));
            var conns = new ConnectionRegistry();
            var session = conns.AddNew();
            var fsm = new WarpStateMachine(world.Clock, conns);
            var al = new WarpAutoLimit(world, pois, fsm);

            int commits = 0;
            double? committedAt = null;
            al.WarpCommitted += t => { commits++; committedAt = t; };

            fsm.RequestWarp(new WarpRequest(session.Id, 100.0, WarpKind.OnRails));
            al.Arm();

            var scheduler = new SimScheduler(world);
            for (int i = 0; i < 60; i++) scheduler.Advance(SimScheduler.FixedDt);
            al.Tick();
            al.Tick(); // a second tick must not re-fire
            al.Tick();

            Assert.AreEqual(1, commits);
            Assert.AreEqual(500.0, committedAt);
        }

        [Test]
        public void AutoLimit_IsNoOp_WhenWarpNotActive()
        {
            var world = new SimWorld();
            var pois = new PoiRegistry();
            var conns = new ConnectionRegistry();
            var fsm = new WarpStateMachine(world.Clock, conns);
            var al = new WarpAutoLimit(world, pois, fsm);

            al.Arm();
            al.Tick();
            Assert.AreEqual(WarpState.Idle, fsm.State);
            Assert.IsFalse(al.HasFired);
        }
    }
}