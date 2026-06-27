using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SoiTransitionTests
    {
        private const double EarthMu = 3.986004418e14;
        private const double MoonMu  = 4.9048695e12;

        private static BodyRegistry EarthMoonSystem()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            var moon = new CelestialBody(CelestialBodyId.Moon, "Moon", MoonMu, 66_100_000.0, CelestialBodyId.Planet,
                new Orbit(384_400_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            return new BodyRegistry(new[] { earth, moon });
        }

        [Test]
        public void StateVectorToOrbit_RoundTrips_Kepler()
        {
            var reg = EarthMoonSystem();
            var orbit = new Orbit(10_000_000.0, 0.3, 0.5, 1.0, 2.0, 0.7, 0.0, CelestialBodyId.Planet);
            var (pos, vel) = KeplerPropagator.StateAt(orbit, 0.0, reg);
            var back = StateVectorToOrbit.Convert(pos, vel, EarthMu, 0.0, CelestialBodyId.Planet);

            Assert.AreEqual(orbit.SemiMajorAxis, back.SemiMajorAxis, 1e-3);
            Assert.AreEqual(orbit.Eccentricity, back.Eccentricity, 1e-9);
            Assert.AreEqual(orbit.Inclination, back.Inclination, 1e-9);
            Assert.AreEqual(orbit.LongitudeOfAscendingNode, back.LongitudeOfAscendingNode, 1e-9);
            Assert.AreEqual(orbit.ArgumentOfPeriapsis, back.ArgumentOfPeriapsis, 1e-9);
            Assert.AreEqual(orbit.MeanAnomalyAtEpoch, back.MeanAnomalyAtEpoch, 1e-9);
        }

        [Test]
        public void SoiTransition_Reparents_And_StateRemainsContinuousInWorldFrame()
        {
            var reg = EarthMoonSystem();
            var world = new SimWorld(reg);

            // A vessel with apoapsis just past the Moon's SOI.
            var peri = 6_671_000.0;     // Earth + 300 km
            var apo  = 384_400_000.0 - 66_100_000.0 + 1_000_000.0; // grazes Moon SOI from inside
            var a = 0.5 * (peri + apo);
            var e = (apo - peri) / (apo + peri);

            var vessel = new Vessel(VesselId.New(),
                new Orbit(a, e, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            world.RegisterVessel(vessel);

            // Find the entry POI and apply at the master-clock boundary.
            var T = vessel.Orbit.Period(EarthMu);
            var poi = SoiScanner.ScanNext(vessel, reg, T / 2.0 - 600.0, T);
            Assert.IsNotNull(poi);

            // World-frame state just before and just after the crossing.
            var (_, _, worldPosBefore, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, poi!.Value.GameTime - 1.0, reg);
            var poiReg = new PoiRegistry();
            poiReg.Add(poi.Value);
            var transition = new SoiTransition(world, reg, poiReg);

            // Run the scheduler forward to the crossing time so the
            // master clock catches up, then apply.
            var scheduler = new SimScheduler(world);
            var simSteps = (int)Math.Ceiling((poi.Value.GameTime - world.Clock.GameTimeSeconds) / SimScheduler.FixedDt);
            for (int i = 0; i < simSteps; i++) scheduler.Advance(SimScheduler.FixedDt);

            transition.ApplyDue(world.Clock.GameTimeSeconds);

            Assert.AreEqual(CelestialBodyId.Moon, vessel.Orbit.ParentBody,
                "After the crossing the vessel's parent body must be the Moon.");

            var (_, _, worldPosAfter, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, poi.Value.GameTime + 1.0, reg);
            var jump = (worldPosAfter - worldPosBefore).Length;
            Assert.Less(jump, 1000.0,
                $"World-frame state must be continuous across the SOI switch within 1 km; got {jump:R} m.");
        }

        [Test]
        public void SoiTransition_RemovesAppliedPoi_FromRegistry()
        {
            var reg = EarthMoonSystem();
            var world = new SimWorld(reg);
            var vessel = new Vessel(VesselId.New(),
                new Orbit(10_000_000.0, 0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            world.RegisterVessel(vessel);

            var poiReg = new PoiRegistry();
            var poi = new Poi(PoiType.SoiCrossing, 100.0, vessel.Id, CelestialBodyId.Planet, CelestialBodyId.Moon);
            poiReg.Add(poi);

            var transition = new SoiTransition(world, reg, poiReg);
            transition.ApplyDue(200.0);
            Assert.AreEqual(0, poiReg.All.Count);
        }
    }
}