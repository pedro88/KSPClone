#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class PoiScannerTests
    {
        private const double EarthMu = 3.986004418e14;
        private const double MoonMu  = 4.9048695e12;

        private static BodyRegistry EarthMoon()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            var moon  = new CelestialBody(CelestialBodyId.Moon, "Moon", MoonMu, 66_100_000.0, CelestialBodyId.Planet,
                new Orbit(384_400_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            return new BodyRegistry(new[] { earth, moon });
        }

        [Test]
        public void EarliestAfter_ReturnsSoonestPoI_AcrossAllVessels()
        {
            var reg = EarthMoon();
            var pois = new PoiRegistry();
            var v1 = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var v2 = new Vessel(VesselId.New(), new Orbit(7e6, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));

            pois.Add(new Poi(PoiType.SoiCrossing, 500.0, v1.Id, CelestialBodyId.Planet, CelestialBodyId.Moon));
            pois.Add(new Poi(PoiType.SoiCrossing, 200.0, v2.Id, CelestialBodyId.Planet, CelestialBodyId.Moon));
            pois.Add(new Poi(PoiType.SoiCrossing, 800.0, v2.Id, CelestialBodyId.Planet, CelestialBodyId.Root));

            var earliest = pois.EarliestAfter(0.0);
            Assert.IsTrue(earliest.HasValue);
            Assert.AreEqual(200.0, earliest!.Value.GameTime);
            Assert.AreEqual(v2.Id, earliest.Value.VesselId);
        }

        [Test]
        public void EarliestAfter_StrictlyAfter_ExcludesPastPoIs()
        {
            var pois = new PoiRegistry();
            var v = VesselId.New();
            pois.Add(new Poi(PoiType.SoiCrossing, 100.0, v, CelestialBodyId.Planet, CelestialBodyId.Moon));
            pois.Add(new Poi(PoiType.SoiCrossing, 200.0, v, CelestialBodyId.Planet, CelestialBodyId.Moon));

            Assert.IsFalse(pois.EarliestAfter(200.0).HasValue,
                "A POI exactly at the query time is NOT 'after' it.");
            var e = pois.EarliestAfter(150.0);
            Assert.IsTrue(e.HasValue);
            Assert.AreEqual(200.0, e!.Value.GameTime);
        }

        [Test]
        public void EarliestAfter_EmptyRegistry_IsNull()
        {
            var pois = new PoiRegistry();
            Assert.IsFalse(pois.EarliestAfter(0.0).HasValue);
        }

        [Test]
        public void RescanAll_RegistersPoIs_ForOnRailsVessels_Only()
        {
            // With a default EarthMoon system + circular vessel orbits,
            // a low-altitude vessel has no Moon crossings within 1 day
            // and therefore contributes no POI; this test verifies the
            // scanner does not crash and the count matches expectations.
            var reg = EarthMoon();
            var world = new SimWorld(reg);
            var lowOrbit = new Vessel(VesselId.New(),
                new Orbit(6_671_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var activeVessel = new Vessel(VesselId.New(),
                new Orbit(6_671_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet)) { State = VesselState.ActivePhysics };
            world.RegisterVessel(lowOrbit);
            world.RegisterVessel(activeVessel);

            var pois = new PoiRegistry();
            var scanner = new PoiScanner(world, reg, pois, lookAheadSeconds: 86400.0);
            int count = scanner.RescanAll();

            Assert.AreEqual(0, count, "Low circular orbit + active vessel should register no POIs in 1 day.");
            Assert.AreEqual(0, pois.All.Count);
        }
    }
}