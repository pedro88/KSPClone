using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class DockingSystemTests
    {
        private static (SimWorld world, Vessel vessel) MakeActiveVessel(Vector3d worldPos, Vector3d worldVel)
        {
            var world = new SimWorld();
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = worldPos,
                CachedWorldVelocity = worldVel,
                CachedLocalPosition = worldPos,
                CachedLocalVelocity = worldVel
            };
            world.RegisterVessel(v);
            return (world, v);
        }

        [Test]
        public void PortsInTolerance_LatchesExactlyOnce()
        {
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (world2, b) = MakeActiveVessel(new Vector3d(0.05, 0, 0), new Vector3d(-0.01, 0, 0));
            // Put both in the same world + bubble.
            foreach (var v in new[] { a, b }) world.RegisterVessel(v);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var ports = new VesselPortRegistry();
            ports.Set(a.Id, new[] { new DockingPort("a_port", Vector3d.Zero, new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b_port", Vector3d.Zero, new Vector3d(-1, 0, 0)) });

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;

            sys.RunLatchPass();
            sys.RunLatchPass();
            sys.RunLatchPass();

            Assert.AreEqual(1, events, "Latch must fire exactly once per vessel pair.");
        }

        [Test]
        public void PortOffsets_BringDistantVesselCentersIntoCapture()
        {
            // Vessel bodies are 4 m apart — far outside the 10 cm capture
            // distance — but each carries a port on its facing side so the
            // ports themselves meet. The latch must test port frames, not
            // vessel centers (M1-T17 step 1).
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (_, b) = MakeActiveVessel(new Vector3d(4.0, 0, 0), Vector3d.Zero);
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var ports = new VesselPortRegistry();
            // a's port juts +2 m toward b; b's port juts −1.96 m toward a → 4 cm gap.
            ports.Set(a.Id, new[] { new DockingPort("a", new Vector3d(2, 0, 0), new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b", new Vector3d(-1.96, 0, 0), new Vector3d(-1, 0, 0)) });

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;
            sys.RunLatchPass();

            Assert.AreEqual(1, events, "Ports meet via their LocalPosition offsets even though vessel centers are 4 m apart.");
        }

        [Test]
        public void CoincidentCenters_PortOffsetsApart_NoLatch()
        {
            // Inverse of the above: vessel centers coincide, but the ports
            // point away from each other and are 4 m apart → no latch.
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (_, b) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var ports = new VesselPortRegistry();
            ports.Set(a.Id, new[] { new DockingPort("a", new Vector3d(2, 0, 0), new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b", new Vector3d(-2, 0, 0), new Vector3d(-1, 0, 0)) });

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;
            sys.RunLatchPass();

            Assert.AreEqual(0, events, "Ports 4 m apart must not latch even when vessel centers coincide.");
        }

        [Test]
        public void OutsideCaptureDistance_NoLatch()
        {
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (_, b) = MakeActiveVessel(new Vector3d(5.0, 0, 0), Vector3d.Zero); // 5 m > 10 cm default
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var ports = new VesselPortRegistry();
            ports.Set(a.Id, new[] { new DockingPort("a_port", Vector3d.Zero, new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b_port", Vector3d.Zero, new Vector3d(-1, 0, 0)) });

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;
            sys.RunLatchPass();

            Assert.AreEqual(0, events);
        }

        [Test]
        public void MisalignedPorts_NoLatch()
        {
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (_, b) = MakeActiveVessel(new Vector3d(0.05, 0, 0), Vector3d.Zero);
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var ports = new VesselPortRegistry();
            ports.Set(a.Id, new[] { new DockingPort("a", Vector3d.Zero, new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b", Vector3d.Zero, new Vector3d(0, 1, 0)) }); // perpendicular

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;
            sys.RunLatchPass();

            Assert.AreEqual(0, events, "Perpendicular ports must not latch.");
        }

        [Test]
        public void VesselsInDifferentBubbles_AreNotConsidered()
        {
            var (world, a) = MakeActiveVessel(Vector3d.Zero, Vector3d.Zero);
            var (_, b) = MakeActiveVessel(new Vector3d(0.05, 0, 0), Vector3d.Zero);
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubbleA = registry.Create(Vector3d.Zero);
            var bubbleB = registry.Create(Vector3d.Zero);
            bubbleA.Add(a.Id); a.BubbleId = bubbleA.Id;
            bubbleB.Add(b.Id); b.BubbleId = bubbleB.Id;

            var ports = new VesselPortRegistry();
            ports.Set(a.Id, new[] { new DockingPort("a", Vector3d.Zero, new Vector3d(1, 0, 0)) });
            ports.Set(b.Id, new[] { new DockingPort("b", Vector3d.Zero, new Vector3d(-1, 0, 0)) });

            int events = 0;
            var sys = new DockingSystem(world, ports);
            sys.DockLatched += _ => events++;
            sys.RunLatchPass();

            Assert.AreEqual(0, events, "Vessels in different bubbles cannot dock (no shared authority).");
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }

    [TestFixture]
    public sealed class DockingMergerTests
    {
        private static (SimWorld world, Vessel vessel, VesselMassRegistry masses) MakeVesselWithMass(
            VesselId id, Vector3d worldPos, Vector3d worldVel, double mass)
        {
            var world = new SimWorld();
            var masses = new VesselMassRegistry();
            var v = new Vessel(id,
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = worldPos,
                CachedWorldVelocity = worldVel,
                CachedLocalPosition = worldPos,
                CachedLocalVelocity = worldVel
            };
            world.RegisterVessel(v);
            masses.Set(id, new RigidVesselMass(mass, mass * 1.0, mass * 1.0, mass * 1.0));
            return (world, v, masses);
        }

        [Test]
        public void Merge_ConservesTotalMass_AndTotalLinearMomentum()
        {
            var (world, a, masses) = MakeVesselWithMass(VesselId.New(), Vector3d.Zero,
                new Vector3d(10, 0, 0), 1000.0);
            var bId = VesselId.New();
            var bMass = new RigidVesselMass(2000.0, 2000.0, 2000.0, 2000.0);
            masses.Set(bId, bMass);
            var b = new Vessel(bId, new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = new Vector3d(0.05, 0, 0),
                CachedWorldVelocity = new Vector3d(-5, 0, 0)
            };
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var merger = new DockingMerger(world, registry, masses);
            DockMergedEvent? captured = null;
            merger.VesselsMerged += e => captured = e;

            Assert.IsTrue(merger.Merge(a.Id, b.Id));

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(3000.0, captured.Value.CombinedMassKg, 1e-9);
            // v_combined = (m_a·v_a + m_b·v_b) / (m_a + m_b) = (1000·10 + 2000·-5)/3000 = 0
            Assert.AreEqual(0.0, captured.Value.CombinedVelocity.X, 1e-9);
            // b is heavier (2000 ≥ 1000) → b survives, lighter a is absorbed.
            Assert.IsFalse(world.Vessels.ContainsKey(a.Id), "lighter vessel a is absorbed");
            Assert.IsTrue(world.Vessels.ContainsKey(b.Id), "heavier vessel b survives");
        }

        [Test]
        public void Merge_ConservesAngularMomentum_FromOrbitalAndSpin()
        {
            // Two equal vessels straddling the COM with equal-and-opposite
            // tangential velocities carry net angular momentum about the join
            // even though linear momentum cancels (M1-T18 step 2). The merged
            // body must spin so that I_combined·ω = total L.
            var (world, a, masses) = MakeVesselWithMass(
                VesselId.New(), new Vector3d(-0.05, 0, 0), new Vector3d(0, 10, 0), 1000.0);
            var bId = VesselId.New();
            masses.Set(bId, new RigidVesselMass(1000.0, 1000.0, 1000.0, 1000.0));
            var b = new Vessel(bId, new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = new Vector3d(0.05, 0, 0),
                CachedWorldVelocity = new Vector3d(0, -10, 0)
            };
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var merger = new DockingMerger(world, registry, masses);
            DockMergedEvent? captured = null;
            merger.VesselsMerged += e => captured = e;

            Assert.IsTrue(merger.Merge(a.Id, bId));

            // Linear momentum cancels.
            Assert.AreEqual(0.0, captured!.Value.CombinedVelocity.Y, 1e-9);
            // L_z = m·(r_a×v_a + r_b×v_b)_z = 1000·(-0.5) + 1000·(-0.5) = -1000.
            // I_z(combined) = 2000 → ω_z = -0.5 rad/s.
            Assert.AreEqual(-0.5, captured.Value.CombinedAngularVelocity.Z, 1e-9,
                "Merged body must spin to conserve angular momentum.");
            Assert.AreEqual(0.0, captured.Value.CombinedAngularVelocity.X, 1e-9);
            Assert.AreEqual(0.0, captured.Value.CombinedAngularVelocity.Y, 1e-9);
        }

        [Test]
        public void Merge_HeavierVesselSurvives()
        {
            // Light vessel merges into heavy one — heavy's id survives.
            var (world, light, masses) = MakeVesselWithMass(VesselId.New(), Vector3d.Zero, Vector3d.Zero, 100.0);
            var heavyId = VesselId.New();
            masses.Set(heavyId, new RigidVesselMass(5000.0, 5000.0, 5000.0, 5000.0));
            var heavy = new Vessel(heavyId, new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = new Vector3d(0.05, 0, 0),
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(heavy);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(light.Id); light.BubbleId = bubble.Id;
            bubble.Add(heavy.Id); heavy.BubbleId = bubble.Id;

            var merger = new DockingMerger(world, registry, masses);
            DockMergedEvent? captured = null;
            merger.VesselsMerged += e => captured = e;

            Assert.IsTrue(merger.Merge(light.Id, heavyId));

            Assert.AreEqual(heavyId, captured!.Value.SurvivingVesselId);
            Assert.AreEqual(light.Id, captured.Value.AbsorbedVesselId);
        }

        [Test]
        public void Merge_DifferentBubbles_Rejected()
        {
            var (world, a, masses) = MakeVesselWithMass(VesselId.New(), Vector3d.Zero, Vector3d.Zero, 1000.0);
            var bId = VesselId.New();
            masses.Set(bId, new RigidVesselMass(1000.0, 1000.0, 1000.0, 1000.0));
            var b = new Vessel(bId, new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = new Vector3d(0.05, 0, 0),
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubbleA = registry.Create(Vector3d.Zero);
            var bubbleB = registry.Create(Vector3d.Zero);
            bubbleA.Add(a.Id); a.BubbleId = bubbleA.Id;
            bubbleB.Add(b.Id); b.BubbleId = bubbleB.Id;

            var merger = new DockingMerger(world, registry, masses);
            Assert.IsFalse(merger.Merge(a.Id, bId));
        }

        [Test]
        public void Merge_NoAuthorityHandoff_VesselsAlreadySharedOneBubble()
        {
            // Regression check (M1-T19): both vessels were already in one bubble
            // before the latch. The merge operates entirely within that single
            // server-authoritative scene — no authority-handoff message is sent.
            var (world, a, masses) = MakeVesselWithMass(VesselId.New(), Vector3d.Zero, Vector3d.Zero, 1000.0);
            var bId = VesselId.New();
            masses.Set(bId, new RigidVesselMass(1000.0, 1000.0, 1000.0, 1000.0));
            var b = new Vessel(bId, new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                CachedWorldPosition = new Vector3d(0.05, 0, 0),
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(b);
            var registry = new BubbleRegistry();
            var bubble = registry.Create(Vector3d.Zero);
            bubble.Add(a.Id); a.BubbleId = bubble.Id;
            bubble.Add(b.Id); b.BubbleId = bubble.Id;

            var beforeA = a.CachedWorldPosition;
            var beforeB = b.CachedWorldPosition;

            var merger = new DockingMerger(world, registry, masses);
            merger.Merge(a.Id, bId);

            // No state jump on the survivor's side at the merge boundary.
            Assert.AreEqual(beforeA, a.CachedWorldPosition,
                "Surviving vessel's position must not jump at the merge boundary (no authority handoff).");
            // Registry still has exactly one bubble; one vessel was absorbed.
            Assert.AreEqual(1, registry.Count);
            Assert.AreEqual(1, world.Vessels.Count);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}