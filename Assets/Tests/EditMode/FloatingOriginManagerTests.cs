using NUnit.Framework;
using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class FloatingOriginManagerTests
    {
        private static Vessel MakeActive(Vector3d worldPos, BubbleId? bubbleId = null)
        {
            return new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                BubbleId = bubbleId,
                CachedWorldPosition = worldPos,
                CachedWorldVelocity = Vector3d.Zero,
                CachedLocalPosition = worldPos, // default: local frame == world frame (no offset yet)
                CachedLocalVelocity = Vector3d.Zero
            };
        }

        [Test]
        public void RebaseThreshold_DefaultsTo1024m_AsADR()
        {
            var mgr = new FloatingOriginManager();
            Assert.AreEqual(1024.0, mgr.RebaseThresholdMeters);
        }

        [Test]
        public void Rebase_BelowThreshold_NoShift()
        {
            var origin = Vector3d.Zero;
            var bubble = new PhysicsBubble(BubbleId.New(), origin);
            bubble.Add(VesselId.New());
            var v = MakeActive(new Vector3d(500, 0, 0));
            var mgr = new FloatingOriginManager();

            var shifted = mgr.RebaseIfDrifted(bubble, new List<Vessel> { v });

            Assert.IsFalse(shifted);
            Assert.AreEqual(origin, bubble.GlobalOrigin);
        }

        [Test]
        public void Rebase_AboveThreshold_ShiftsOriginToCentroid()
        {
            var origin = Vector3d.Zero;
            var bubble = new PhysicsBubble(BubbleId.New(), origin);
            var id = VesselId.New();
            bubble.Add(id);
            var v = MakeActive(new Vector3d(5000, 0, 0)); // 5 km > 1024 m

            var mgr = new FloatingOriginManager();
            var shifted = mgr.RebaseIfDrifted(bubble, new List<Vessel> { v });

            Assert.IsTrue(shifted);
            Assert.AreEqual(new Vector3d(5000, 0, 0), bubble.GlobalOrigin);
        }

        [Test]
        public void Rebase_EmitsEvent_WithAffectedVessels()
        {
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var a = VesselId.New(); bubble.Add(a);
            var b = VesselId.New(); bubble.Add(b);
            var va = MakeActive(new Vector3d(2000, 0, 0));
            va.BubbleId = bubble.Id;
            var vb = MakeActive(new Vector3d(4000, 0, 0));
            vb.BubbleId = bubble.Id;

            var mgr = new FloatingOriginManager();
            FloatingOriginShiftedEvent? captured = null;
            mgr.OriginShifted += e => captured = e;

            mgr.RebaseIfDrifted(bubble, new List<Vessel> { va, vb });

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(bubble.Id, captured.Value.BubbleId);
            Assert.AreEqual(new Vector3d(3000, 0, 0), captured.Value.WorldDelta); // centroid
            Assert.AreEqual(2, captured.Value.AffectedVessels.Count);
        }

        [Test]
        public void Rebase_EmptyBubble_DoesNothing()
        {
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var mgr = new FloatingOriginManager();
            Assert.IsFalse(mgr.RebaseIfDrifted(bubble, new List<Vessel>()));
        }

        [Test]
        public void Rebase_VesselWithNoCachedLocal_IsSkipped()
        {
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var id = VesselId.New();
            bubble.Add(id);
            var v = new Vessel(id,
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                BubbleId = bubble.Id,
                CachedWorldPosition = new Vector3d(5000, 0, 0)
                // CachedLocalPosition left null
            };

            var mgr = new FloatingOriginManager();
            Assert.IsFalse(mgr.RebaseIfDrifted(bubble, new List<Vessel> { v }));
        }

        [Test]
        public void RebaseToCentroid_OnFreshBubble_ShiftsOrigin()
        {
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var id = VesselId.New();
            bubble.Add(id);
            var v = MakeActive(new Vector3d(700, 0, 0));

            var mgr = new FloatingOriginManager();
            mgr.RebaseToCentroid(bubble, new List<Vessel> { v });

            Assert.AreEqual(new Vector3d(700, 0, 0), bubble.GlobalOrigin);
        }

        [Test]
        public void MergeInto_KeepsLargerBubble_RebasesIncomerFromAuthoritativeDoubles()
        {
            // keep has 2 members; absorb has 1 → keep survives.
            var keep = new PhysicsBubble(BubbleId.New(), new Vector3d(1e9, 0, 0));
            var absorb = new PhysicsBubble(BubbleId.New(), new Vector3d(0, 0, 0));
            var idKeep1 = VesselId.New(); keep.Add(idKeep1);
            var idKeep2 = VesselId.New(); keep.Add(idKeep2);
            var idAbsorb = VesselId.New(); absorb.Add(idAbsorb);

            var vKeep1 = MakeActive(new Vector3d(1e9 + 100, 0, 0), keep.Id);
            var vKeep2 = MakeActive(new Vector3d(1e9 - 100, 0, 0), keep.Id);
            var vAbsorb = MakeActive(new Vector3d(0, 0, 0), absorb.Id);

            var mgr = new FloatingOriginManager();
            var result = mgr.MergeInto(keep, absorb, new List<Vessel> { vKeep1, vKeep2, vAbsorb });

            Assert.AreSame(keep, result, "Larger bubble (keep) must survive the merge.");
            Assert.AreEqual(3, keep.MemberCount);
            Assert.IsTrue(keep.Contains(idAbsorb));
            Assert.IsFalse(absorb.Contains(idAbsorb), "absorb must no longer own the incoming vessel.");
            // Incomer's local position is now (worldGlobal - keepOrigin) = (0 - 1e9) = (-1e9, 0, 0).
            Assert.AreEqual(new Vector3d(-1e9, 0, 0), vAbsorb.CachedLocalPosition);
            Assert.AreEqual(keep.Id, vAbsorb.BubbleId);
        }

        [Test]
        public void MergeInto_AbsorbLargerThanKeep_SwapsRoles()
        {
            var keep = new PhysicsBubble(BubbleId.New(), new Vector3d(0, 0, 0));
            keep.Add(VesselId.New());
            var absorb = new PhysicsBubble(BubbleId.New(), new Vector3d(100, 0, 0));
            absorb.Add(VesselId.New());
            absorb.Add(VesselId.New());
            var vKeep = MakeActive(Vector3d.Zero, keep.Id);
            var vAbsorb1 = MakeActive(new Vector3d(100, 0, 0), absorb.Id);
            var vAbsorb2 = MakeActive(new Vector3d(120, 0, 0), absorb.Id);

            var mgr = new FloatingOriginManager();
            var result = mgr.MergeInto(keep, absorb, new List<Vessel> { vKeep, vAbsorb1, vAbsorb2 });

            Assert.AreSame(absorb, result, "When absorb is larger, it survives and keep gets absorbed.");
            Assert.AreEqual(3, absorb.MemberCount);
        }

        [Test]
        public void Rebase_PreservesAuthoritativeWorldPosition()
        {
            // The rebase only moves GlobalOrigin; the vessel's authoritative world
            // position (computed upstream) is unaffected. ADR-0012 §6 invariant.
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var id = VesselId.New();
            bubble.Add(id);
            var v = MakeActive(new Vector3d(3000, 0, 0));

            var mgr = new FloatingOriginManager();
            mgr.RebaseIfDrifted(bubble, new List<Vessel> { v });

            Assert.AreEqual(new Vector3d(3000, 0, 0), v.CachedWorldPosition!.Value);
            Assert.AreEqual(Vector3d.Zero, v.CachedWorldVelocity);
        }

        [Test]
        public void CustomThreshold_Honoured()
        {
            var bubble = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            bubble.Add(VesselId.New());
            var v = MakeActive(new Vector3d(500, 0, 0));

            // Tight threshold (200 m) — 500 m should trigger a rebase.
            var mgr = new FloatingOriginManager(200.0);
            Assert.IsTrue(mgr.RebaseIfDrifted(bubble, new List<Vessel> { v }));
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}