#nullable enable annotations

using System;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SoiTransitionTests
    {
        private const double EarthMu = 3.986004418e14;
        private const double MoonMu  = 4.9048695e12;

        private const double EarthRadius     = 6_371_000.0;
        private const double MoonOrbitRadius = 384_400_000.0;
        private const double MoonSoiRadius   = 66_100_000.0;

        private static BodyRegistry EarthMoonSystem()
        {
            var earth = new CelestialBody(CelestialBodyId.Planet, "Earth", EarthMu, 924_000_000.0, CelestialBodyId.Root);
            var moon = new CelestialBody(CelestialBodyId.Moon, "Moon", MoonMu, MoonSoiRadius, CelestialBodyId.Planet,
                new Orbit(MoonOrbitRadius, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            return new BodyRegistry(new[] { earth, moon });
        }

        /// <summary>
        /// Earth orbit whose apoapsis meets the Moon just inside its SOI at
        /// <c>encounterTime</c> — a bound (low-speed, near-apoapsis) lunar
        /// encounter representable by M0's elliptic-only propagator.
        /// </summary>
        private static (Orbit orbit, double encounterTime) MoonInterceptOrbit()
        {
            const double encounterTime = 200_000.0;
            var moonMeanMotion = Math.Sqrt(EarthMu / (MoonOrbitRadius * MoonOrbitRadius * MoonOrbitRadius));
            var moonAngle = moonMeanMotion * encounterTime;          // Moon circular, M0 = 0
            var peri = EarthRadius + 300_000.0;
            var apo  = MoonOrbitRadius - MoonSoiRadius * 0.5;
            var a = 0.5 * (peri + apo);
            var e = (apo - peri) / (apo + peri);
            var meanMotion = Math.Sqrt(EarthMu / (a * a * a));
            var argp = KeplerPropagator.WrapTwoPi(moonAngle - Math.PI);
            var m0   = KeplerPropagator.WrapTwoPi(Math.PI - meanMotion * encounterTime);
            return (new Orbit(a, e, 0.0, 0.0, argp, m0, 0.0, CelestialBodyId.Planet), encounterTime);
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

            var (orbit, encounterTime) = MoonInterceptOrbit();
            var vessel = new Vessel(VesselId.New(), orbit);
            world.RegisterVessel(vessel);

            var poi = SoiScanner.ScanNext(vessel, reg, 0.0, encounterTime);
            Assert.IsNotNull(poi);

            // World-frame position from the OLD orbit at the exact crossing time.
            var (_, _, worldPosBefore, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, poi!.Value.GameTime, reg);

            var poiReg = new PoiRegistry();
            poiReg.Add(poi.Value);
            var transition = new SoiTransition(world, reg, poiReg);
            transition.ApplyDue(poi.Value.GameTime + 1.0);

            Assert.AreEqual(CelestialBodyId.Moon, vessel.Orbit.ParentBody,
                "After the crossing the vessel's parent body must be the Moon.");

            // World-frame position from the NEW orbit at the same instant must
            // match: the re-parent transform is position-continuous (M0 keeps
            // bodies' velocity out of scope, so continuity is checked at the
            // crossing instant, not across the Moon's own motion).
            var (_, _, worldPosAfter, _) = KeplerPropagator.WorldFrameStateAt(vessel.Orbit, poi.Value.GameTime, reg);
            var jump = (worldPosAfter - worldPosBefore).Length;
            Assert.Less(jump, 1.0,
                $"World-frame position must be continuous across the SOI switch; got {jump:R} m.");
        }

        [Test]
        public void SoiTransition_RemovesAppliedPoi_FromRegistry()
        {
            var reg = EarthMoonSystem();
            var world = new SimWorld(reg);
            var (orbit, encounterTime) = MoonInterceptOrbit();
            var vessel = new Vessel(VesselId.New(), orbit);
            world.RegisterVessel(vessel);

            var poiReg = new PoiRegistry();
            // POI at apoapsis, where the vessel sits inside the Moon SOI at low
            // speed — a bound capture the M0 converter can re-express.
            poiReg.Add(new Poi(PoiType.SoiCrossing, encounterTime, vessel.Id, CelestialBodyId.Planet, CelestialBodyId.Moon));

            var transition = new SoiTransition(world, reg, poiReg);
            transition.ApplyDue(encounterTime + 1.0);
            Assert.AreEqual(0, poiReg.All.Count);
        }
    }
}