using NUnit.Framework;
using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class DemotionControllerTests
    {
        // After M2-T12 the Sun–Earth–Moon system has Earth orbiting the Sun at
        // 1 AU (was static at origin). Tests here used to anchor the vessel at
        // the world origin in Earth SOI; they now anchor on Earth's *current*
        // world position so the fitter still sees a sensible parent-frame
        // state vector.
        private static (SimWorld world, BodyRegistry bodies, Vector3d earthWorldPos) MakeWorld()
        {
            var bodies = WorldSeed.CreateBodies();
            var world = new SimWorld(bodies);
            var earthWorldPos = bodies.WorldPositionOf(CelestialBodyId.Planet, 0.0);
            return (world, bodies, earthWorldPos);
        }

        private static Vessel MakeActive(SimWorld world, Vector3d worldPos, Vector3d worldVel, BubbleId? bubbleId = null)
        {
            var v = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.ActivePhysics,
                BubbleId = bubbleId,
                CachedWorldPosition = worldPos,
                CachedWorldVelocity = worldVel,
                CachedLocalPosition = worldPos,
                CachedLocalVelocity = worldVel
            };
            world.RegisterVessel(v);
            return v;
        }

        [Test]
        public void UnattendedWarpSafeVessel_DemotesToOnRails()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var warpSafe = new WarpSafeEvaluator();
            var demo = new DemotionController(world, registry, warpSafe);

            demo.RunPass();

            Assert.AreEqual(VesselState.OnRails, v.State);
            Assert.IsNull(v.BubbleId);
            Assert.AreEqual(world.Clock.GameTimeSeconds, v.VesselClockSeconds, 1e-9);
        }

        [Test]
        public void ThrustingVessel_IsNotDemoted()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));
            v.ThrustActive = true;

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var warpSafe = new WarpSafeEvaluator();
            var demo = new DemotionController(world, registry, warpSafe);

            demo.RunPass();

            Assert.AreEqual(VesselState.ActivePhysics, v.State, "Active thrust must block demotion (PHYS-3 warp-safe rule).");
        }

        [Test]
        public void OccupiedVessel_IsNotDemoted()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var warpSafe = new WarpSafeEvaluator();
            var demo = new DemotionController(world, registry, warpSafe, occupancyLookup: id => id.Equals(v.Id));

            demo.RunPass();

            Assert.AreEqual(VesselState.ActivePhysics, v.State, "Occupancy must block demotion.");
        }

        [Test]
        public void Demotion_EmptyBubble_IsGarbageCollected()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var demo = new DemotionController(world, registry, new WarpSafeEvaluator());
            demo.RunPass();

            Assert.AreEqual(0, registry.Count, "Last vessel demoted → bubble must be collected (ADR-0012 §2).");
        }

        [Test]
        public void Demotion_EmitsEvent_WithNewOrbit()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            DemotionEvent? captured = null;
            var demo = new DemotionController(world, registry, new WarpSafeEvaluator());
            demo.VesselDemoted += e => captured = e;

            demo.RunPass();

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(v.Id, captured.Value.VesselId);
            Assert.AreEqual(DemotionReason.UnattendedWarpSafe, captured.Value.Reason);
            Assert.AreEqual(bubble.Id, captured.Value.PreviousBubbleId);
        }

        [Test]
        public void Demotion_NewOrbit_AnalyticPosition_IsContinuousWithLastRigidState()
        {
            var (world, _, earthPos) = MakeWorld();
            // Circular-ish orbit in Earth's parent frame: a = 7_000_000 m,
            // μ = Earth's GM (planet GM, not Sun). Vessel is positioned at
            // earthPos + r along +x and given the circular speed for that radius.
            var r = 7_000_000.0;
            var planet = world.Bodies!.Get(CelestialBodyId.Planet);
            var mu = planet.GravParameterMu;
            var vCirc = System.Math.Sqrt(mu / r);
            var worldPos = earthPos + new Vector3d(r, 0, 0);
            var worldVel = new Vector3d(0, vCirc, 0);
            var v = MakeActive(world, worldPos, worldVel);

            var registry = new BubbleRegistry();
            var bubble = registry.Create(worldPos);
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var demo = new DemotionController(world, registry, new WarpSafeEvaluator());
            demo.RunPass();

            // Continuity check: the new orbit's analytic *world* position at the
            // master clock should match the last rigid world state. Now that
            // Earth itself orbits the Sun, the fitter still produces a parent-
            // frame orbit; the world-frame position is Earth.WorldPosition +
            // relative. We check in world frame directly.
            var t = world.Clock.GameTimeSeconds;
            var (_, _, analyticWorldPos, analyticWorldVel) =
                KeplerPropagator.WorldFrameStateAt(v.Orbit, t, world.Bodies);

            var dp = analyticWorldPos - worldPos;
            var dv = analyticWorldVel - worldVel;
            Assert.Less(dp.Length, 0.01, $"Position continuity violated: |Δp|={dp.Length} m");
            Assert.Less(dv.Length, 0.01, $"Velocity continuity violated: |Δv|={dv.Length} m/s");
        }

        [Test]
        public void ForceDemote_BypassesWarpSafety()
        {
            var (world, _, earthPos) = MakeWorld();
            var v = MakeActive(world, earthPos + new Vector3d(7_100_000.0, 0, 0), new Vector3d(0, 7546, 0));
            v.ThrustActive = true; // would normally block demotion

            var registry = new BubbleRegistry();
            var bubble = registry.Create(earthPos + new Vector3d(7_100_000.0, 0, 0));
            bubble.Add(v.Id);
            v.BubbleId = bubble.Id;

            var demo = new DemotionController(world, registry, new WarpSafeEvaluator());

            Assert.IsTrue(demo.ForceDemote(v.Id));
            Assert.AreEqual(VesselState.OnRails, v.State);
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}