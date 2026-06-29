#nullable enable annotations

using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Disconnect / reconnect crew lifecycle (M2-T10/T11, CREW-5/CREW-2):
    /// disconnect vacates every station the player held with no reserved seat;
    /// a reconnecting player gets no precedence and must re-issue an occupy,
    /// losing the race if someone took the seat meanwhile.
    /// </summary>
    public sealed class CrewLifecycleTests
    {
        private static ServerSimulation NewSim()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            return new ServerSimulation(world);
        }

        [Test]
        public void Disconnect_VacatesStation_LeavingNoReservedSeat()
        {
            var sim = NewSim();
            var (session, _) = sim.Connect();
            sim.OccupyStation(session.Id, WorldSeed.SeedVesselId, Station.Pilot);
            Assert.AreEqual(session.Id, sim.Controls.Owner(WorldSeed.SeedVesselId, Station.Pilot));

            sim.Disconnect(session.Id);

            Assert.IsNull(sim.Controls.Owner(WorldSeed.SeedVesselId, Station.Pilot),
                "Disconnect vacates the station (it then falls to automation via StationDriver).");
            Assert.IsFalse(sim.Controls.IsOccupied(WorldSeed.SeedVesselId), "No reserved seat remains.");
        }

        [Test]
        public void Reconnect_HasNoPriority_MustReissue_AndLosesRaceForTakenSeat()
        {
            var sim = NewSim();
            var first = PlayerId.New();
            var other = PlayerId.New();

            sim.OccupyStation(first, WorldSeed.SeedVesselId, Station.Pilot);
            sim.Controls.Vacate(first); // model a disconnect freeing the seat
            Assert.IsNull(sim.Controls.Owner(WorldSeed.SeedVesselId, Station.Pilot));

            // Another player takes Pilot before the first reconnects.
            Assert.IsTrue(sim.OccupyStation(other, WorldSeed.SeedVesselId, Station.Pilot));

            // Reconnecting player gets no precedence: Pilot is refused...
            Assert.IsFalse(sim.OccupyStation(first, WorldSeed.SeedVesselId, Station.Pilot),
                "No seat reservation — the reconnecting player loses the race.");
            // ...but any free station is grantable via a fresh occupy.
            Assert.IsTrue(sim.OccupyStation(first, WorldSeed.SeedVesselId, Station.Navigator));
            Assert.AreEqual(first, sim.Controls.Owner(WorldSeed.SeedVesselId, Station.Navigator));
        }
    }
}
