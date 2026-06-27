using System;
using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class BodyRegistryTests
    {
        private static BodyRegistry MakeEarthMoonSystem()
        {
            // Real-world-ish values (m, m³/s²). μ_Earth = 3.986e14, SOI Earth = ~0.924e9.
            var earth = new CelestialBody(
                CelestialBodyId.Planet, "Earth",
                gravParameterMu: 3.986004418e14,
                soiRadius: 924_000_000.0,
                parentId: CelestialBodyId.Root);
            var moon = new CelestialBody(
                CelestialBodyId.Moon, "Moon",
                gravParameterMu: 4.9048695e12,
                soiRadius: 66_100_000.0,
                parentId: CelestialBodyId.Planet);
            return new BodyRegistry(new[] { earth, moon });
        }

        [Test]
        public void Registry_HasTwoBodies_WithParentChainToRoot()
        {
            var reg = MakeEarthMoonSystem();
            Assert.AreEqual(2, reg.Bodies.Count);
            Assert.IsTrue(reg.TryGet(CelestialBodyId.Planet, out _));
            Assert.IsTrue(reg.TryGet(CelestialBodyId.Moon, out _));
        }

        [Test]
        public void Registry_Rejects_NonPositiveMuOrSoi()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialBody(
                CelestialBodyId.Root, "Bad", gravParameterMu: 0.0, soiRadius: 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CelestialBody(
                CelestialBodyId.Root, "Bad", gravParameterMu: 1.0, soiRadius: -1.0));
        }

        [Test]
        public void RootBody_IsFixedAtOrigin_ForAllTime()
        {
            var reg = MakeEarthMoonSystem();
            Assert.AreEqual(Vector3d.Zero, reg.WorldPositionOf(CelestialBodyId.Root, 0.0));
            Assert.AreEqual(Vector3d.Zero, reg.WorldPositionOf(CelestialBodyId.Root, 1e12));
        }

        [Test]
        public void ParentChild_Body_WorldPositionEqualsParent_ForStaticTree()
        {
            var reg = MakeEarthMoonSystem();
            var earthPos = reg.WorldPositionOf(CelestialBodyId.Planet, 0.0);
            var moonPos = reg.WorldPositionOf(CelestialBodyId.Moon, 0.0);
            Assert.AreEqual(earthPos, moonPos, "Without a parent-frame orbit (T07) Moon sits at Earth's world position.");
        }

        [Test]
        public void AncestorsOf_Moon_StartsWithEarth_ThenRoot()
        {
            var reg = MakeEarthMoonSystem();
            var ancestors = new List<CelestialBodyId>(reg.AncestorsOf(CelestialBodyId.Moon));
            Assert.AreEqual(2, ancestors.Count);
            Assert.AreEqual(CelestialBodyId.Planet, ancestors[0]);
            Assert.AreEqual(CelestialBodyId.Root, ancestors[1]);
        }
    }
}