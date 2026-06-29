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

        // --- M1 composition (ADR-0014): the no-op stepper proves the tick
        //     order and the promotion/demotion wiring without PhysX. ---

        [Test]
        public void PlayerLoad_PromotesSeedVessel_ToActivePhysicsInABubble_WithinOneTick()
        {
            var sim = NewSim();
            // Mark the seed vessel as crewed so the demotion pass leaves it active.
            sim.SetOccupancyLookup(_ => true);

            PromotionEvent? promoted = null;
            sim.Promotion.VesselPromoted += e => promoted = e;

            sim.Promotion.RequestPlayerLoad(WorldSeed.SeedVesselId);
            sim.Advance(1.0 / 60.0); // one fixed tick

            var v = sim.World.Vessels[WorldSeed.SeedVesselId];
            Assert.AreEqual(VesselState.ActivePhysics, v.State, "Player-load must promote the vessel.");
            Assert.IsNotNull(v.BubbleId, "A promoted vessel lives in a bubble.");
            Assert.IsTrue(sim.Bubbles.TryGet(v.BubbleId!.Value, out var bubble));
            Assert.IsTrue(bubble.Contains(v.Id));
            Assert.IsNotNull(promoted, "VesselPromoted must fire.");
            Assert.AreEqual(PromotionReason.PlayerLoad, promoted!.Value.Reason);
        }

        [Test]
        public void UnattendedWarpSafeVessel_PromotesThenDemotes_InTheSameTick()
        {
            var sim = NewSim();
            // No occupancy override → vessel is unattended; with no thrust it is
            // warp-safe, so the demotion pass returns it to on-rails the same tick.
            bool didPromote = false, didDemote = false;
            sim.Promotion.VesselPromoted += _ => didPromote = true;
            sim.Demotion.VesselDemoted += _ => didDemote = true;

            sim.Promotion.RequestPlayerLoad(WorldSeed.SeedVesselId);
            sim.Advance(1.0 / 60.0);

            Assert.IsTrue(didPromote, "Promotion still runs (step 2).");
            Assert.IsTrue(didDemote, "An unattended warp-safe vessel demotes the same tick (step 5).");
            var v = sim.World.Vessels[WorldSeed.SeedVesselId];
            Assert.AreEqual(VesselState.OnRails, v.State);
            Assert.IsNull(v.BubbleId);
            Assert.AreEqual(0, sim.Bubbles.Count, "The emptied bubble is collected.");
        }
    }
}
