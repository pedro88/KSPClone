#nullable enable annotations

using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// In-process integration test of the assembled M0 spine: seed → connect →
    /// handshake → solo warp vote → on-rails warp → global auto-limit halts the
    /// clock exactly on the next SOI crossing → vessel re-parents. No transport,
    /// no Postgres (those layers are exercised separately).
    /// </summary>
    public sealed class ServerSimulationTests
    {
        private static ServerSimulation NewSim()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            return new ServerSimulation(world);
        }

        [Test]
        public void Connect_ReturnsHandshake_ListingSeedVessel_AndGameTime()
        {
            var sim = NewSim();
            var (session, handshake) = sim.Connect();

            Assert.AreEqual(1, sim.Connections.ConnectedCount);
            Assert.IsTrue(sim.Connections.Contains(session.Id));
            Assert.AreEqual(1, handshake.Vessels.Count);
            Assert.AreEqual(WorldSeed.SeedVesselId, handshake.Vessels[0].Id);
            Assert.AreEqual(0.0, handshake.GameTimeSeconds, 1e-9);
        }

        [Test]
        public void SeededWorld_HasUpcomingSoiCrossing_ForAutoLimit()
        {
            var sim = NewSim();
            var earliest = sim.Pois.EarliestAfter(0.0);
            Assert.IsTrue(earliest.HasValue, "Seed vessel must have an SOI crossing for the warp to stop on.");
            Assert.AreEqual(CelestialBodyId.Moon, earliest!.Value.ToBody);
        }

        [Test]
        public void SoloOnRailsWarp_RunsThenAutoLimitsExactlyAtTheSoiCrossing()
        {
            var sim = NewSim();
            var (session, _) = sim.Connect();

            double? committedAt = null;
            sim.WarpCommitted += t => committedAt = t;
            Vessel? reParented = null;
            sim.VesselReParented += v => reParented = v;

            Assert.IsTrue(sim.RequestWarp(new WarpRequest(session.Id, 1000.0, WarpKind.OnRails)));
            Assert.AreEqual(WarpState.Active, sim.Warp.State);
            Assert.AreEqual(1000.0, sim.World.Clock.Rate);

            var poiTime = sim.Pois.EarliestAfter(0.0)!.Value.GameTime;

            // Feed wall-time (sub-clamp chunks) until the auto-limit halts the warp.
            for (int i = 0; i < 5000 && sim.Warp.State == WarpState.Active; i++)
                sim.Advance(0.25);

            Assert.AreEqual(WarpState.Idle, sim.Warp.State, "Auto-limit must halt the warp.");
            Assert.AreEqual(1.0, sim.World.Clock.Rate, "Rate returns to baseline after auto-limit.");
            Assert.IsNotNull(committedAt, "WarpCommitted must fire at the endpoint.");
            Assert.AreEqual(poiTime, committedAt!.Value, 1e-9,
                "The warp commits exactly on the SOI-crossing POI (never past).");
            // ClampTo lands game-time on the POI at the instant the auto-limit fires;
            // baseline time then legitimately continues for the remainder of that real
            // Advance chunk, so the clock ends at the POI or marginally past it.
            var drift = sim.World.Clock.GameTimeSeconds - poiTime;
            Assert.GreaterOrEqual(drift, 0.0, "The warp never overshoots the POI.");
            Assert.Less(drift, 0.26, "Post-halt baseline drift is bounded by one real Advance.");

            // The crossing itself applied: the vessel re-parented to the Moon.
            Assert.IsNotNull(reParented, "The due SOI crossing must re-parent the vessel.");
            Assert.AreEqual(CelestialBodyId.Moon, sim.World.Vessels[WorldSeed.SeedVesselId].Orbit.ParentBody);
        }

        [Test]
        public void ConnectMidWarp_HaltsToBaseline()
        {
            var sim = NewSim();
            var (a, _) = sim.Connect();
            Assert.IsTrue(sim.RequestWarp(new WarpRequest(a.Id, 1000.0, WarpKind.OnRails)));
            Assert.AreEqual(WarpState.Active, sim.Warp.State);

            // A second player connecting never consented → warp halts (TIME-6).
            sim.Connect();
            Assert.AreEqual(WarpState.Idle, sim.Warp.State);
            Assert.AreEqual(1.0, sim.World.Clock.Rate);
        }
    }
}
