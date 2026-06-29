using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class StructuralFailureTests
    {
        private static (SimWorld world, Vessel vessel) MakeActiveVessel()
        {
            var world = new SimWorld();
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = Vector3d.Zero,
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(v);
            return (world, v);
        }

        [Test]
        public void BelowThreshold_NoBreak()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("lower", 1000.0, 500.0) });
            var sys = new StructuralFailureSystem(world, joints);

            StructuralFailureEvent? captured = null;
            sys.JointBroken += e => captured = e;

            Assert.IsFalse(sys.ReportLoad(v.Id, "lower", 800.0, 400.0));
            Assert.IsFalse(captured.HasValue);
        }

        [Test]
        public void ForceExceedsThreshold_BreakEmitsExactlyOnce()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("lower", 1000.0, 500.0) });
            var sys = new StructuralFailureSystem(world, joints);

            int events = 0;
            sys.JointBroken += _ => events++;

            Assert.IsTrue(sys.ReportLoad(v.Id, "lower", 1500.0, 100.0));
            Assert.IsFalse(sys.ReportLoad(v.Id, "lower", 9999.0, 9999.0),
                "Joint must be removed after breaking — second report has nothing to break.");
            Assert.AreEqual(1, events, "Exactly one event must fire per joint per vessel lifetime.");
        }

        [Test]
        public void TorqueExceedsThreshold_BreakEmits()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("rot", 5000.0, 200.0) });
            var sys = new StructuralFailureSystem(world, joints);

            int events = 0;
            sys.JointBroken += _ => events++;

            Assert.IsTrue(sys.ReportLoad(v.Id, "rot", 100.0, 250.0));
            Assert.AreEqual(1, events);
        }

        [Test]
        public void Break_AssignsNewVesselId_InEvent()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("boom", 100.0, 50.0) });
            var sys = new StructuralFailureSystem(world, joints);

            StructuralFailureEvent? captured = null;
            sys.JointBroken += e => captured = e;

            sys.ReportLoad(v.Id, "boom", 200.0, 80.0);

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(v.Id, captured.Value.VesselId);
            Assert.AreEqual("boom", captured.Value.JointName);
            Assert.AreEqual(200.0, captured.Value.BreakingForceNewtons, 1e-9);
            Assert.AreEqual(80.0, captured.Value.BreakingTorqueNm, 1e-9);
            Assert.AreNotEqual(v.Id, captured.Value.NewVesselId, "New vessel id must differ from the broken vessel id.");
        }

        [Test]
        public void ReportLoad_UnknownJoint_IsNoop()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("lower", 100.0, 50.0) });
            var sys = new StructuralFailureSystem(world, joints);

            Assert.IsFalse(sys.ReportLoad(v.Id, "nonexistent", 9999, 9999));
        }

        [Test]
        public void ZeroBreakThreshold_ForceNeverBreaks()
        {
            var (world, v) = MakeActiveVessel();
            var joints = new VesselJointRegistry();
            joints.Set(v.Id, new[] { new StructuralJoint("inf", 0.0, 0.0) });
            var sys = new StructuralFailureSystem(world, joints);

            Assert.IsFalse(sys.ReportLoad(v.Id, "inf", 99999, 99999),
                "BreakForceNewtons == 0 means no break threshold (joint never fails by force).");
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}