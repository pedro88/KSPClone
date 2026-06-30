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
    }
}