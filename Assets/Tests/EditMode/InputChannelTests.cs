using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class InputChannelTests
    {
        private static Vessel MakeActiveVessel(SimWorld world)
        {
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = Vector3d.Zero,
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(v);
            return v;
        }

        [Test]
        public void Submit_AppliesThrottleAndAttitude_ToVessel()
        {
            var world = new SimWorld();
            var vessel = MakeActiveVessel(world);
            var channel = new InputChannel(world);

            var ok = channel.Submit(new PilotInputMessage(vessel.Id, 1L, 0.7, 0.1, -0.2, 0.05));

            Assert.IsTrue(ok);
            Assert.AreEqual(0.7, vessel.ThrottleCommand, 1e-9);
            Assert.AreEqual(new Vector3d(0.1, -0.2, 0.05), vessel.AttitudeCommand);
        }

        [Test]
        public void Submit_ThrottleAboveOne_IsClampedToOne()
        {
            var world = new SimWorld();
            var vessel = MakeActiveVessel(world);
            var channel = new InputChannel(world);

            channel.Submit(new PilotInputMessage(vessel.Id, 1L, 5.0, 0, 0, 0));

            Assert.AreEqual(1.0, vessel.ThrottleCommand, 1e-9);
        }

        [Test]
        public void Submit_ThrottleBelowZero_IsClampedToZero()
        {
            var world = new SimWorld();
            var vessel = MakeActiveVessel(world);
            var channel = new InputChannel(world);

            channel.Submit(new PilotInputMessage(vessel.Id, 1L, -1.0, 0, 0, 0));

            Assert.AreEqual(0.0, vessel.ThrottleCommand, 1e-9);
        }

        [Test]
        public void Submit_UnknownVessel_IsRejected()
        {
            var world = new SimWorld();
            var channel = new InputChannel(world);

            var ok = channel.Submit(new PilotInputMessage(VesselId.New(), 1L, 0.5, 0, 0, 0));

            Assert.IsFalse(ok);
            Assert.AreEqual(1, channel.RejectedInputs);
        }

        [Test]
        public void Submit_BuffersInputs_ForPredictionReplay()
        {
            var world = new SimWorld();
            var vessel = MakeActiveVessel(world);
            var channel = new InputChannel(world);

            channel.Submit(new PilotInputMessage(vessel.Id, 1L, 0.5, 0, 0, 0));
            channel.Submit(new PilotInputMessage(vessel.Id, 2L, 0.6, 0, 0, 0));
            channel.Submit(new PilotInputMessage(vessel.Id, 3L, 0.7, 0, 0, 0));

            Assert.IsTrue(channel.PendingInputs.TryGetValue(vessel.Id, out var q));
            Assert.AreEqual(3, q.Count);
            Assert.AreEqual(1L, q.Peek().ClientTick);
        }

        [Test]
        public void DiscardAcked_DropsInputsAtOrBeforeAckedTick()
        {
            var world = new SimWorld();
            var vessel = MakeActiveVessel(world);
            var channel = new InputChannel(world);

            channel.Submit(new PilotInputMessage(vessel.Id, 10L, 0.5, 0, 0, 0));
            channel.Submit(new PilotInputMessage(vessel.Id, 11L, 0.5, 0, 0, 0));
            channel.Submit(new PilotInputMessage(vessel.Id, 12L, 0.5, 0, 0, 0));

            channel.DiscardAcked(vessel.Id, 11L);

            var q = channel.PendingInputs[vessel.Id];
            Assert.AreEqual(1, q.Count);
            Assert.AreEqual(12L, q.Peek().ClientTick);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}