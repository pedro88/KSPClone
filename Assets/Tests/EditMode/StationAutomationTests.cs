#nullable enable annotations

using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// Automation fallback (M2-T07/T08, CREW-4): unoccupied stations are driven
    /// by automation; occupied ones are sourced from the human and never
    /// overwritten. Pilot SAS damps angular rate. Logic-level (the convergence
    /// under PhysX is a PlayMode test).
    /// </summary>
    public sealed class StationAutomationTests
    {
        private static Vessel ActiveVesselWithSpin(Vector3d angularVelocity)
        {
            return new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = Vector3d.Zero,
                CachedWorldVelocity = Vector3d.Zero,
                CachedAngularVelocity = angularVelocity,
            };
        }

        [Test]
        public void PilotSas_EmitsAttitudeCommand_OpposingAngularRate()
        {
            var v = ActiveVesselWithSpin(new Vector3d(0.0, 1.0, 0.0));
            new PilotSasAutomation(dampingGain: 2.0).Drive(v, 1.0 / 60.0);

            Assert.AreEqual(-2.0, v.AttitudeCommand.Y, 1e-9, "SAS opposes the measured spin.");
            Assert.AreEqual(0.0, v.AttitudeCommand.X, 1e-9);
            Assert.AreEqual(0.0, v.ThrottleCommand, 1e-9, "SAS never touches throttle.");
        }

        [Test]
        public void StationDriver_DrivesEmptyPilot_ButNotOccupiedPilot()
        {
            var world = new SimWorld();
            var v = ActiveVesselWithSpin(new Vector3d(0.0, 1.0, 0.0));
            world.RegisterVessel(v);
            var controls = new ControlRegistry();
            var driver = new StationDriver();

            // Empty Pilot → automation drives it.
            driver.Tick(world.Vessels.Values, controls, 1.0 / 60.0);
            Assert.Less(v.AttitudeCommand.Y, 0.0, "Empty Pilot is driven by SAS.");

            // Occupy Pilot and clear the command → automation must not touch it.
            controls.Occupy(PlayerId.New(), v.Id, Station.Pilot);
            v.AttitudeCommand = Vector3d.Zero;
            driver.Tick(world.Vessels.Values, controls, 1.0 / 60.0);
            Assert.AreEqual(0.0, v.AttitudeCommand.Y, 1e-9, "Occupied Pilot is sourced from the human, not automation.");
        }

        [Test]
        public void StationDriver_IgnoresNonActiveVessels()
        {
            var world = new SimWorld();
            var v = ActiveVesselWithSpin(new Vector3d(0.0, 1.0, 0.0));
            v.State = VesselState.OnRails; // not active → no per-tick commands
            world.RegisterVessel(v);

            new StationDriver().Tick(world.Vessels.Values, new ControlRegistry(), 1.0 / 60.0);
            Assert.AreEqual(0.0, v.AttitudeCommand.Y, 1e-9, "On-rails vessels are not driven.");
        }
    }
}
