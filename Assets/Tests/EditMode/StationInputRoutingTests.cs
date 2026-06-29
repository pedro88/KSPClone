#nullable enable annotations

using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Per-station input routing (M2-T05, CREW-1, Art. 6): an input is applied
    /// only if the sender occupies the station that owns the targeted system;
    /// cross-station inputs are refused. Headless against ServerSimulation.
    /// </summary>
    public sealed class StationInputRoutingTests
    {
        private static ServerSimulation NewSim()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            return new ServerSimulation(world);
        }

        [Test]
        public void PilotOccupant_ThrottleApplied_StagingRefused()
        {
            var sim = NewSim();
            var pilot = PlayerId.New();
            sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot);

            // Throttle is a Pilot-owned system → applied.
            Assert.IsTrue(sim.SubmitStationInput(pilot, WorldSeed.SeedVesselId, ControllableSystem.Throttle, 0.4));
            Assert.AreEqual(0.4, sim.World.Vessels[WorldSeed.SeedVesselId].ThrottleCommand, 1e-9);

            // Staging is Engineer-owned; the Pilot occupant does not own it → refused.
            Assert.IsFalse(sim.SubmitStationInput(pilot, WorldSeed.SeedVesselId, ControllableSystem.Staging));
            Assert.AreEqual(1, sim.RejectedStationInputs);
        }

        [Test]
        public void NonOccupant_AttitudeRefused()
        {
            var sim = NewSim();
            var pilot = PlayerId.New();
            var stranger = PlayerId.New();
            sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot);

            Assert.IsFalse(sim.SubmitStationInput(stranger, WorldSeed.SeedVesselId, ControllableSystem.Attitude));
            Assert.AreEqual(1, sim.RejectedStationInputs);
        }

        [Test]
        public void DisjointStations_ActConcurrently_WithoutContention()
        {
            // Pilot + Engineer occupied by different players; each drives only
            // its own systems in the same tick batch (CREW-1, T06 in spirit).
            var sim = NewSim();
            var pilot = PlayerId.New();
            var engineer = PlayerId.New();
            sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot);
            sim.OccupyStation(engineer, WorldSeed.SeedVesselId, Station.Engineer);

            Assert.IsTrue(sim.SubmitStationInput(pilot, WorldSeed.SeedVesselId, ControllableSystem.Throttle, 0.7));
            Assert.IsTrue(sim.SubmitStationInput(engineer, WorldSeed.SeedVesselId, ControllableSystem.Staging));

            Assert.AreEqual(0.7, sim.World.Vessels[WorldSeed.SeedVesselId].ThrottleCommand, 1e-9);
            Assert.AreEqual(0, sim.RejectedStationInputs, "Disjoint stations never contend.");
        }
    }
}
