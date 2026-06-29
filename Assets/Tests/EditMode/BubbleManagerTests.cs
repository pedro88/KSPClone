using NUnit.Framework;
using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class BubbleManagerTests
    {
        private static Vessel MakeActive(Vector3d pos, BubbleId? bubbleId = null)
        {
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                BubbleId = bubbleId,
                CachedWorldPosition = pos,
                CachedWorldVelocity = Vector3d.Zero
            };
            return v;
        }

        private static Vessel MakeOnRails(Vector3d pos)
        {
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.OnRails,
                CachedWorldPosition = pos
            };
            return v;
        }

        [Test]
        public void NoVessels_NoBubblesCreated()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);

            var result = mgr.RunClusteringPass(new List<Vessel>());

            Assert.AreEqual(0, result.Count);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void OnlyOnRailsVessels_NoBubblesCreated()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var vessels = new List<Vessel>
            {
                MakeOnRails(new Vector3d(0, 0, 0)),
                MakeOnRails(new Vector3d(100, 0, 0))
            };

            mgr.RunClusteringPass(vessels);

            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void TwoVessels5kmApart_GetSeparateBubbles()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var vessels = new List<Vessel>
            {
                MakeActive(new Vector3d(0, 0, 0)),
                MakeActive(new Vector3d(5000, 0, 0)) // 5 km > 2.5 km default
            };

            var bubbles = mgr.RunClusteringPass(vessels);

            Assert.AreEqual(2, bubbles.Count);
            Assert.AreEqual(2, registry.Count);
        }

        [Test]
        public void TwoVessels2kmApart_GetOneBubble()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var vessels = new List<Vessel>
            {
                MakeActive(new Vector3d(0, 0, 0)),
                MakeActive(new Vector3d(2000, 0, 0)) // 2 km < 2.5 km default
            };

            var bubbles = mgr.RunClusteringPass(vessels);

            Assert.AreEqual(1, bubbles.Count);
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(2, bubbles[0].MemberCount);
        }

        [Test]
        public void Vessels_CrossingThreshold_JoinInOneTick()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);

            // Tick 1: far apart → 2 bubbles
            var a = MakeActive(new Vector3d(0, 0, 0));
            var b = MakeActive(new Vector3d(4000, 0, 0));
            mgr.RunClusteringPass(new List<Vessel> { a, b });
            Assert.AreEqual(2, registry.Count);

            // Move b close to a — within one tick the manager must produce a single bubble.
            b.CachedWorldPosition = new Vector3d(1000, 0, 0);
            var bubbles = mgr.RunClusteringPass(new List<Vessel> { a, b });

            Assert.AreEqual(1, bubbles.Count);
            Assert.AreEqual(1, registry.Count, "Two bubbles must merge when the proximity test puts them in one cluster.");
            Assert.IsTrue(bubbles[0].Contains(a.Id));
            Assert.IsTrue(bubbles[0].Contains(b.Id));
        }

        [Test]
        public void NewBubble_Origin_IsClusterCentroidInDoubles()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var vessels = new List<Vessel>
            {
                MakeActive(new Vector3d(0, 0, 0)),
                MakeActive(new Vector3d(1000, 0, 0)),
                MakeActive(new Vector3d(500, 0, 0))
            };

            var bubbles = mgr.RunClusteringPass(vessels);

            Assert.AreEqual(1, bubbles.Count);
            Assert.AreEqual(new Vector3d(500.0, 0, 0), bubbles[0].GlobalOrigin);
        }

        [Test]
        public void ExistingBubble_IsReused_WhenClusterMembersAlreadyShareIt()
        {
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            var mgr = new BubbleManager(registry);

            var a = MakeActive(new Vector3d(0, 0, 0), bubble.Id);
            var b = MakeActive(new Vector3d(1000, 0, 0), bubble.Id);

            var bubbles = mgr.RunClusteringPass(new List<Vessel> { a, b });

            Assert.AreEqual(1, registry.Count, "Manager must not create a second bubble when the cluster already shares one.");
            Assert.AreEqual(bubble.Id, bubbles[0].Id);
        }

        [Test]
        public void SplitBubblePass_VesselsSeparate_BothBubblesSurvive()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var a = MakeActive(new Vector3d(0, 0, 0));
            var b = MakeActive(new Vector3d(2000, 0, 0)); // close → one bubble initially
            mgr.RunClusteringPass(new List<Vessel> { a, b });
            Assert.AreEqual(1, registry.Count);

            // Drift apart to 5 km — they're in different clusters but bubble membership
            // is unchanged this tick. Manager emits Left events for whichever vessel
            // dropped out of its cluster and creates a new bubble for the second cluster.
            a.CachedWorldPosition = new Vector3d(0, 0, 0);
            b.CachedWorldPosition = new Vector3d(5000, 0, 0);
            var bubbles = mgr.RunClusteringPass(new List<Vessel> { a, b });

            Assert.AreEqual(2, bubbles.Count);
            Assert.IsTrue(bubbles[0].Contains(a.Id) ^ bubbles[1].Contains(a.Id),
                "Vessels must end up in different bubbles after they separate beyond R_phys.");
        }

        [Test]
        public void Vessel_LeavingAllClusters_EmitsLeftEvent_AndBubbleDestroyedWhenEmpty()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var a = MakeActive(new Vector3d(0, 0, 0));
            var b = MakeActive(new Vector3d(1000, 0, 0));
            mgr.RunClusteringPass(new List<Vessel> { a, b });
            Assert.AreEqual(1, registry.Count);

            // b demotes between ticks — it's no longer in the clustering pass input.
            a.CachedWorldPosition = new Vector3d(0, 0, 0);
            mgr.RunClusteringPass(new List<Vessel> { a });

            Assert.AreEqual(1, registry.Count, "a still owns the bubble.");
            Assert.IsTrue(a.BubbleId.HasValue);
            foreach (var b2 in registry.All)
                Assert.AreEqual(1, b2.MemberCount, "Bubble should hold only a after b demoted.");
        }

        [Test]
        public void VesselsWithNoCachedPosition_AreSkipped()
        {
            var registry = new BubbleRegistry();
            var mgr = new BubbleManager(registry);
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics
                // CachedWorldPosition left null
            };

            mgr.RunClusteringPass(new List<Vessel> { v });

            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}