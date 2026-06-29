#nullable enable annotations

using NUnit.Framework;
using KSPClone.SimCore;
using KSPClone.Net;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Station occupancy + pilot-input authority (M1-T22, ADR-0016, NET-1,
    /// Art. 6). Headless: drives ServerSimulation directly with the no-op
    /// stepper, plus a WireCodec round-trip for the OccupyStation command.
    /// </summary>
    public sealed class ControlAuthorityTests
    {
        private static ServerSimulation NewSim()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            return new ServerSimulation(world);
        }

        [Test]
        public void OccupyingPilot_PromotesVessel_AndKeepsItActiveWhileCrewed()
        {
            var sim = NewSim();
            var a = PlayerId.New();

            Assert.IsTrue(sim.OccupyStation(a, WorldSeed.SeedVesselId, Station.Pilot));
            sim.Advance(1.0 / 60.0);

            var v = sim.World.Vessels[WorldSeed.SeedVesselId];
            Assert.AreEqual(VesselState.ActivePhysics, v.State, "Occupying Pilot promotes the vessel.");
            Assert.IsNotNull(v.BubbleId);
            // Stays active across further ticks because it remains occupied.
            sim.Advance(1.0 / 60.0);
            Assert.AreEqual(VesselState.ActivePhysics, sim.World.Vessels[WorldSeed.SeedVesselId].State);
        }

        [Test]
        public void PilotInput_AppliedFromOccupant_RejectedFromOthers()
        {
            var sim = NewSim();
            var pilot = PlayerId.New();
            var intruder = PlayerId.New();
            sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot);

            var input = new PilotInputMessage(WorldSeed.SeedVesselId, clientTick: 1,
                throttle: 0.5, pitchRate: 0.0, yawRate: 0.0, rollRate: 0.0);

            Assert.IsTrue(sim.SubmitPilotInput(pilot, input), "Occupant's input is applied.");
            Assert.AreEqual(0.5, sim.World.Vessels[WorldSeed.SeedVesselId].ThrottleCommand, 1e-9);

            var spoof = new PilotInputMessage(WorldSeed.SeedVesselId, clientTick: 2,
                throttle: 1.0, pitchRate: 0.0, yawRate: 0.0, rollRate: 0.0);
            Assert.IsFalse(sim.SubmitPilotInput(intruder, spoof), "Non-occupant's input is rejected.");
            Assert.AreEqual(1, sim.RejectedPilotInputs);
            Assert.AreEqual(0.5, sim.World.Vessels[WorldSeed.SeedVesselId].ThrottleCommand, 1e-9,
                "Rejected input must not mutate the vessel.");
        }

        [Test]
        public void SameStation_CannotBeOccupiedByTwoPlayers()
        {
            var sim = NewSim();
            var a = PlayerId.New();
            var b = PlayerId.New();
            Assert.IsTrue(sim.OccupyStation(a, WorldSeed.SeedVesselId, Station.Pilot));
            Assert.IsFalse(sim.OccupyStation(b, WorldSeed.SeedVesselId, Station.Pilot),
                "Control is partitioned, never contended (Art. 6).");
            Assert.AreEqual(a, sim.Controls.Owner(WorldSeed.SeedVesselId, Station.Pilot));
        }

        [Test]
        public void Disconnect_VacatesStation_LeavingVesselUnattended_ThenItDemotes()
        {
            var sim = NewSim();
            var a = PlayerId.New();
            // Connect so Disconnect has a session to remove, mirroring the live path.
            var (session, _) = sim.Connect();
            sim.OccupyStation(session.Id, WorldSeed.SeedVesselId, Station.Pilot);
            sim.Advance(1.0 / 60.0);
            Assert.AreEqual(VesselState.ActivePhysics, sim.World.Vessels[WorldSeed.SeedVesselId].State);

            sim.Disconnect(session.Id);
            sim.Advance(1.0 / 60.0);

            Assert.IsFalse(sim.Controls.IsOccupied(WorldSeed.SeedVesselId));
            Assert.AreEqual(VesselState.OnRails, sim.World.Vessels[WorldSeed.SeedVesselId].State,
                "Unattended + warp-safe demotes (SUSP-2).");
        }

        [Test]
        public void OccupyStationCommand_RoundTripsOnTheWire()
        {
            var cmd = ClientCommand.OccupyStation(WorldSeed.SeedVesselId, Station.Navigator);
            var decoded = WireCodec.DecodeClientCommand(WireCodec.EncodeClientCommand(cmd));

            Assert.AreEqual(ClientCommandType.OccupyStation, decoded.Type);
            Assert.AreEqual(WorldSeed.SeedVesselId, decoded.VesselId);
            Assert.AreEqual(Station.Navigator, decoded.Station);
        }
    }
}
