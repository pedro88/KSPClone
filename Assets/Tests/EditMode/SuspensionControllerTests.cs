using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class SuspensionControllerTests
    {
        private static (SimWorld world, Vessel vessel) MakeActiveVessel(double masterClock = 0.0)
        {
            var world = new SimWorld();
            world.Clock.Advance(masterClock);
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                VesselClockSeconds = masterClock,
                CachedWorldPosition = new Vector3d(7_100_000.0, 0, 0),
                CachedWorldVelocity = new Vector3d(0, 7546, 0),
                CachedLocalPosition = new Vector3d(7_100_000.0, 0, 0),
                CachedLocalVelocity = new Vector3d(0, 7546, 0)
            };
            world.RegisterVessel(v);
            return (world, v);
        }

        private static (BubbleRegistry registry, BubbleId bubbleId) AttachBubble(Vessel vessel)
        {
            var registry = new BubbleRegistry();
            var bubble = registry.Create(vessel.CachedLocalPosition!.Value);
            bubble.Add(vessel.Id);
            vessel.BubbleId = bubble.Id;
            return (registry, bubble.Id);
        }

        [Test]
        public void ThrustingVessel_IsSuspended_WhenLastPlayerLeaves()
        {
            var (world, vessel) = MakeActiveVessel();
            vessel.ThrustActive = true;
            var (registry, _) = AttachBubble(vessel);

            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            var suspended = controller.RunSuspensionPass(occupancyLookup: null);

            Assert.AreEqual(1, suspended.Count);
            Assert.AreEqual(VesselState.Suspended, vessel.State);
            Assert.IsNull(vessel.BubbleId);
            Assert.IsTrue(store.TryGet(vessel.Id, out _));
        }

        [Test]
        public void WarpSafeVessel_IsNotSuspended()
        {
            var (world, vessel) = MakeActiveVessel();
            vessel.ThrustActive = false;
            var (registry, _) = AttachBubble(vessel);

            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            var suspended = controller.RunSuspensionPass(occupancyLookup: null);

            Assert.AreEqual(0, suspended.Count);
            Assert.AreEqual(VesselState.ActivePhysics, vessel.State);
        }

        [Test]
        public void OccupiedVessel_IsNotSuspended()
        {
            var (world, vessel) = MakeActiveVessel();
            vessel.ThrustActive = true;
            var (registry, _) = AttachBubble(vessel);

            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            var suspended = controller.RunSuspensionPass(occupancyLookup: id => id.Equals(vessel.Id));

            Assert.AreEqual(0, suspended.Count);
        }

        [Test]
        public void Suspension_PausesVesselClock_MasterClockContinuesToAdvance()
        {
            var (world, vessel) = MakeActiveVessel(masterClock: 100.0);
            vessel.ThrustActive = true;
            var (registry, _) = AttachBubble(vessel);

            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            controller.Suspend(vessel.Id);

            Assert.AreEqual(100.0, vessel.VesselClockSeconds, 1e-9,
                "Vessel clock freezes at the suspend point.");
            // Master clock advances freely — vessel clock does not.
            world.Clock.Advance(60.0);
            Assert.AreEqual(100.0, vessel.VesselClockSeconds, 1e-9,
                "Suspended vessel's clock must NOT advance when the master clock moves (no retro-sim).");
        }

        [Test]
        public void Resume_RestoresVesselState_FromSnapshot()
        {
            var (world, vessel) = MakeActiveVessel(masterClock: 50.0);
            vessel.ThrustActive = true;
            var (registry, bid) = AttachBubble(vessel);
            var vesselClockAtSuspend = vessel.VesselClockSeconds;
            var localPos = vessel.CachedLocalPosition!.Value;

            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            controller.Suspend(vessel.Id);

            world.Clock.Advance(3600.0); // 1 hour of master time elapses
            Assert.IsTrue(controller.Resume(vessel.Id));

            Assert.AreEqual(VesselState.ActivePhysics, vessel.State);
            Assert.AreEqual(vesselClockAtSuspend, vessel.VesselClockSeconds, 1e-9,
                "Resumed vessel clock resumes from the frozen value (vessel clock may lag master clock).");
            Assert.AreEqual(localPos, vessel.CachedLocalPosition!.Value);
            Assert.IsNotNull(vessel.BubbleId);
        }

        [Test]
        public void Resume_IntoMissingBubble_CreatesFreshBubble()
        {
            var (world, vessel) = MakeActiveVessel();
            vessel.ThrustActive = true;
            var (registry, _) = AttachBubble(vessel);
            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());
            controller.Suspend(vessel.Id);

            // Destroy the original bubble — registry-level remove simulates
            // the bubble having been GC'd while the vessel was suspended.
            registry.CollectEmpty();

            Assert.IsTrue(controller.Resume(vessel.Id),
                "Resume must spawn a fresh bubble when the original is gone.");
            Assert.IsTrue(vessel.BubbleId.HasValue);
            Assert.IsTrue(registry.TryGet(vessel.BubbleId!.Value, out _));
        }

        [Test]
        public void Resume_UnknownVessel_ReturnsFalse()
        {
            var world = new SimWorld();
            var registry = new BubbleRegistry();
            var store = new SnapshotStore();
            var controller = new SuspensionController(world, registry, store, new WarpSafeEvaluator());

            Assert.IsFalse(controller.Resume(VesselId.New()));
        }

        [Test]
        public void Suspension_EmitsEvent_WithSuspendSnapshot()
        {
            var (world, vessel) = MakeActiveVessel();
            vessel.ThrustActive = true;
            var (registry, _) = AttachBubble(vessel);

            SuspendedVesselState? captured = null;
            var controller = new SuspensionController(world, registry, new SnapshotStore(), new WarpSafeEvaluator());
            controller.VesselSuspended += s => captured = s;

            controller.Suspend(vessel.Id);

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(vessel.Id, captured.Value.VesselId);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}