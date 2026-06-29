using NUnit.Framework;
using System.Collections.Generic;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class PromotionControllerTests
    {
        private static Vessel MakeOnRails(Vector3d worldPos)
        {
            return new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet))
            {
                State = VesselState.OnRails,
                CachedWorldPosition = worldPos,
                CachedWorldVelocity = Vector3d.Zero
            };
        }

        [Test]
        public void PlayerLoad_PromotesOnRailsVessel_ToActivePhysics()
        {
            var world = new SimWorld();
            var vessel = MakeOnRails(Vector3d.Zero);
            world.RegisterVessel(vessel);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            promo.RequestPlayerLoad(vessel.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.AreEqual(VesselState.ActivePhysics, vessel.State);
            Assert.IsTrue(vessel.BubbleId.HasValue);
            Assert.AreEqual(1, registry.Count);
        }

        [Test]
        public void Proximity_ToActiveVessel_PromotesOnRails()
        {
            var world = new SimWorld();
            var a = MakeOnRails(new Vector3d(0, 0, 0));
            var b = MakeOnRails(new Vector3d(1000, 0, 0));
            world.RegisterVessel(a);
            world.RegisterVessel(b);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            // Pre-promote a via player load so it's active this tick.
            promo.RequestPlayerLoad(a.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);
            Assert.AreEqual(VesselState.ActivePhysics, a.State);

            // b is within R_phys (1000 m < 2500 m default) and active vessel a exists.
            // Second pass: clustering runs implicitly via promotion scan.
            promo.RequestPlayerLoad(VesselId.New()); // no-op; clear stale state
            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.AreEqual(VesselState.ActivePhysics, b.State,
                "b is within R_phys of a (active), promotion by proximity should fire.");
            Assert.AreEqual(b.BubbleId, a.BubbleId, "Both vessels share the same bubble after proximity promotion.");
        }

        [Test]
        public void Promotion_AssignsBubble_AtVesselWorldPosition_WhenNoBubbleNearby()
        {
            var world = new SimWorld();
            var vessel = MakeOnRails(new Vector3d(1e9, 0, 0));
            world.RegisterVessel(vessel);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            promo.RequestPlayerLoad(vessel.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.AreEqual(VesselState.ActivePhysics, vessel.State);
            Assert.IsTrue(vessel.BubbleId.HasValue);
            registry.TryGet(vessel.BubbleId!.Value, out var bubble);
            Assert.IsNotNull(bubble);
            Assert.AreEqual(new Vector3d(1e9, 0, 0), bubble.GlobalOrigin);
        }

        [Test]
        public void Promotion_ReusesNearbyBubble_WhenWithinRangeRadius()
        {
            var world = new SimWorld();
            var a = MakeOnRails(new Vector3d(0, 0, 0));
            var b = MakeOnRails(new Vector3d(100, 0, 0));
            world.RegisterVessel(a);
            world.RegisterVessel(b);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            promo.RequestPlayerLoad(a.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);
            Assert.AreEqual(1, registry.Count);

            promo.RequestPlayerLoad(b.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);
            Assert.AreEqual(1, registry.Count, "b reuses a's bubble (within R_phys).");
            Assert.AreEqual(a.BubbleId, b.BubbleId);
        }

        [Test]
        public void Promotion_NoTrigger_VesselStaysOnRails()
        {
            var world = new SimWorld();
            var vessel = MakeOnRails(Vector3d.Zero);
            world.RegisterVessel(vessel);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.AreEqual(VesselState.OnRails, vessel.State);
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void Promotion_EmitsEvent_WithSeedState()
        {
            var world = new SimWorld();
            var vessel = MakeOnRails(new Vector3d(123, 456, 789));
            world.RegisterVessel(vessel);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            PromotionEvent? captured = null;
            promo.VesselPromoted += e => captured = e;
            promo.RequestPlayerLoad(vessel.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.IsTrue(captured.HasValue);
            Assert.AreEqual(vessel.Id, captured.Value.VesselId);
            Assert.AreEqual(PromotionReason.PlayerLoad, captured.Value.Reason);
            Assert.IsTrue(captured.Value.WorldPosition.LengthSquared > 0);
        }

        [Test]
        public void Promotion_HyperbolicOrbit_NotPromoted()
        {
            var world = new SimWorld();
            var v = new Vessel(VesselId.New(),
                new Orbit(-1.0, 1.5, 0, 0, 0, 0, 0, CelestialBodyId.Planet)) // hyperbolic
            {
                State = VesselState.OnRails,
                CachedWorldPosition = Vector3d.Zero,
                CachedWorldVelocity = Vector3d.Zero
            };
            world.RegisterVessel(v);

            var registry = new BubbleRegistry();
            var bubbles = new BubbleManager(registry);
            var promo = new PromotionController(world, bubbles, registry);

            promo.RequestPlayerLoad(v.Id);
            promo.RunPass(world.Clock.GameTimeSeconds);

            Assert.AreEqual(VesselState.OnRails, v.State, "Hyperbolic orbits are deferred (M1 propagator is elliptic-only).");
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}