using NUnit.Framework;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// The M0/seed celestial hierarchy after adding the Sun as the root
    /// gravitational anchor: Sun (root, fixed) ← Earth (orbits Sun) ← Moon
    /// (orbits Earth). The vessel stays parented to Earth — its orbital
    /// elements are Earth-local, so the new Sun tier must not break the
    /// existing Earth/Moon behaviour (ORBIT-3 / SOI re-parenting).
    /// </summary>
    public sealed class WorldSeedTests
    {
        [Test]
        public void CreateBodies_HasSunEarthMoon_WithExpectedHierarchy()
        {
            var reg = WorldSeed.CreateBodies();

            Assert.IsTrue(reg.TryGet(CelestialBodyId.Sun, out var sun));
            Assert.IsTrue(reg.TryGet(CelestialBodyId.Planet, out var earth));
            Assert.IsTrue(reg.TryGet(CelestialBodyId.Moon, out var moon));

            Assert.AreEqual(CelestialBodyId.Root, sun.ParentId, "Sun's parent is the Root sentinel.");
            Assert.AreEqual(CelestialBodyId.Sun, earth.ParentId, "Earth now orbits the Sun, not Root.");
            Assert.AreEqual(CelestialBodyId.Planet, moon.ParentId, "Moon's parent is unchanged.");

            Assert.IsNotNull(earth.OrbitAroundParent, "Earth carries an orbit around the Sun.");
            Assert.IsNotNull(moon.OrbitAroundParent, "Moon carries an orbit around Earth.");
            Assert.IsNull(sun.OrbitAroundParent, "Sun has no parent-frame orbit (fixed at origin).");
        }

        [Test]
        public void Sun_IsFixedAtOrigin_ForAllTime()
        {
            var reg = WorldSeed.CreateBodies();
            Assert.AreEqual(Vector3d.Zero, reg.WorldPositionOf(CelestialBodyId.Sun, 0.0));
            Assert.AreEqual(Vector3d.Zero, reg.WorldPositionOf(CelestialBodyId.Sun, 1e12));
        }

        [Test]
        public void Earth_WorldPosition_IsOneAu_FromSun()
        {
            var reg = WorldSeed.CreateBodies();
            var earthPos = reg.WorldPositionOf(CelestialBodyId.Planet, 0.0);
            Assert.AreEqual(WorldSeed.EarthOrbitRadius, earthPos.Length, 1e-6);
        }

        [Test]
        public void Moon_WorldPosition_IsEarthPlusMoonOrbit_AtEpoch()
        {
            // At t=0 Earth is at (+EarthOrbitRadius, 0, 0) in the Sun frame and
            // Moon is at (+MoonOrbitRadius, 0, 0) in the Earth frame, so the
            // Moon's world position is Earth + MoonOrbitRadius on +x.
            var reg = WorldSeed.CreateBodies();
            var earthPos = reg.WorldPositionOf(CelestialBodyId.Planet, 0.0);
            var moonPos  = reg.WorldPositionOf(CelestialBodyId.Moon, 0.0);
            Assert.AreEqual(WorldSeed.EarthOrbitRadius + WorldSeed.MoonOrbitRadius, moonPos.X, 1e-6);
            Assert.AreEqual(0.0, moonPos.Y, 1e-6);
            Assert.AreEqual(0.0, moonPos.Z, 1e-6);
            // The Moon's offset from Earth is exactly one MoonOrbitRadius along +x.
            var offset = moonPos - earthPos;
            Assert.AreEqual(WorldSeed.MoonOrbitRadius, offset.Length, 1e-6);
        }

        [Test]
        public void SeedVessel_StillParentedToEarth()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            var vessel = world.Vessels[WorldSeed.SeedVesselId];
            Assert.AreEqual(CelestialBodyId.Planet, vessel.Orbit.ParentBody,
                "Vessel orbits Earth; the new Sun tier must not change that.");
        }

        // Regression: parent-frame and world-frame Kepler state must differ by
        // exactly Earth's world position once Earth carries an orbit around
        // the Sun. Before M2-T12 they coincided (Earth static at origin); this
        // test catches any future refactor that re-introduces the footgun by
        // routing callers to StateAt for a world-frame query (ADR-0017).
        [Test]
        public void StateAt_AndWorldFrameStateAt_DifferByEarthWorldPosition()
        {
            var reg = WorldSeed.CreateBodies();
            var vessel = WorldSeed.CreateVessel();
            var t = 1.0e6; // arbitrary non-zero game-time

            var (parentFramePos, _) = KeplerPropagator.StateAt(vessel.Orbit, t, reg);
            var (_, _, worldPos, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, t, reg);
            var earthWorldPos = reg.WorldPositionOf(CelestialBodyId.Planet, t);

            var expectedOffset = earthWorldPos;
            var actualOffset = worldPos - parentFramePos;

            // The two should agree exactly (closed-form identity, not a
            // tolerance check). A failure here means someone broke the
            // frame relationship again.
            Assert.AreEqual(0.0, (actualOffset - expectedOffset).Length, 1e-9,
                "worldPos == parentFramePos + parentWorldPos (ADR-0017).");
            // Sanity: they must NOT coincide anymore (Earth is no longer
            // at origin).
            Assert.Greater(actualOffset.Length, 1.0,
                "Earth orbits the Sun: the two frames must differ at non-zero t.");
        }

        // --- Surface launch (M2.5-T02, PHYS-7, ADR-0018) ---

        // The surface seed must place the craft at Earth's +Y pole, one pad
        // half-height above the surface, in the parent (Earth) frame at epoch.
        [Test]
        public void SurfaceVessel_SpawnsAtEarthNorthPole_InParentFrame()
        {
            var reg = WorldSeed.CreateBodies();
            var vessel = WorldSeed.CreateSurfaceVessel();

            var (parentFramePos, _) = KeplerPropagator.StateAt(vessel.Orbit, 0.0, reg);

            Assert.AreEqual(0.0, parentFramePos.X, 1e-3, "no +X component at the pole");
            Assert.AreEqual(WorldSeed.SurfaceSpawnRadius, parentFramePos.Y, 1e-6, "up along +Y at surface radius");
            Assert.AreEqual(0.0, parentFramePos.Z, 1e-3, "no +Z component (equatorial plane is world-XY)");
            Assert.AreEqual(WorldSeed.SurfaceSpawnRadius, parentFramePos.Length, 1e-6);
        }

        // Radial-up at the spawn point must equal world +Y so the untumbled
        // client presentation (+Y = up) holds without a surface frame (ADR-0018 §3).
        [Test]
        public void SurfaceVessel_RadialUp_IsWorldPlusY()
        {
            var reg = WorldSeed.CreateBodies();
            var vessel = WorldSeed.CreateSurfaceVessel();

            var (_, _, worldPos, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, 0.0, reg);
            var earthPos = reg.WorldPositionOf(CelestialBodyId.Planet, 0.0);
            var radial = worldPos - earthPos;

            Assert.AreEqual(WorldSeed.SurfaceSpawnRadius, radial.Length, 1e-6,
                "craft sits one surface-spawn-radius from Earth's centre");
            var up = radial * (1.0 / radial.Length);
            Assert.AreEqual(0.0, up.X, 1e-9);
            Assert.AreEqual(1.0, up.Y, 1e-9, "radial-up is world +Y");
            Assert.AreEqual(0.0, up.Z, 1e-9);
        }

        // Surface radius must actually clear Earth's surface (be above the
        // ground), by exactly one pad half-height — the capsule starts in contact.
        [Test]
        public void SurfaceSpawnRadius_ClearsEarthSurface_ByPadHalfHeight()
        {
            Assert.AreEqual(WorldSeed.EarthRadius + WorldSeed.PadHalfHeight,
                WorldSeed.SurfaceSpawnRadius, 1e-9);
            Assert.Greater(WorldSeed.SurfaceSpawnRadius, WorldSeed.EarthRadius);
        }
    }
}