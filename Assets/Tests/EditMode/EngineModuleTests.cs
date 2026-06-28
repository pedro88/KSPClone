using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class EngineModuleTests
    {
        [Test]
        public void MassFlow_EqualsFOverIspG0()
        {
            var e = new EngineModule("Merlin", 845_000.0, 282.0, Vector3d.Zero, new Vector3d(0, 1, 0), 1000.0);
            // ṁ = 845000 / (282 * 9.80665) ≈ 305.5 kg/s
            Assert.AreEqual(845_000.0 / (282.0 * EngineModule.G0), e.MassFlowAtFullThrottle(), 1e-9);
        }

        [Test]
        public void EffectiveThrust_ZeroThrottle_IsZero()
        {
            var e = new EngineModule("Test", 1000.0, 200.0, Vector3d.Zero, new Vector3d(0, 1, 0), 500.0);
            Assert.AreEqual(0.0, e.EffectiveThrust(0.0));
        }

        [Test]
        public void EffectiveThrust_EmptyPropellant_IsZero()
        {
            var e = new EngineModule("Test", 1000.0, 200.0, Vector3d.Zero, new Vector3d(0, 1, 0), 0.0);
            Assert.AreEqual(0.0, e.EffectiveThrust(1.0));
        }

        [Test]
        public void EffectiveThrust_HalfThrottle_IsHalfMagnitude()
        {
            var e = new EngineModule("Test", 1000.0, 200.0, Vector3d.Zero, new Vector3d(0, 1, 0), 500.0);
            Assert.AreEqual(500.0, e.EffectiveThrust(0.5));
        }
    }

    [TestFixture]
    public sealed class VesselEngineRegistryTests
    {
        private static EngineModule MakeEngine(double thrust, double isp, double propellant)
            => new("Test", thrust, isp, Vector3d.Zero, new Vector3d(0, 1, 0), propellant);

        [Test]
        public void ConsumePropellant_ReducesMass_AtTsiolkovskyRate()
        {
            var vesselId = VesselId.New();
            var massReg = new VesselMassRegistry();
            massReg.Set(vesselId, new RigidVesselMass(10_000.0, 1000.0, 1000.0, 1000.0));

            var engines = new VesselEngineRegistry();
            engines.Set(vesselId, new[] { MakeEngine(50_000.0, 300.0, 5_000.0) });

            // Burn 100 s at full throttle: ṁ = 50000/(300*9.80665) ≈ 16.99 kg/s
            // After 100 s: m1 ≈ 10_000 - 1699 = 8301 kg
            // Δv (Tsiolkovsky) = 300 * 9.80665 * ln(10000/8301) ≈ 549.8 m/s
            double dt = 1.0 / 60.0;
            double dv = 0.0;
            double initialMass = massReg.Get(vesselId)!.MassKg;
            for (int i = 0; i < 6000; i++) // 100 s
            {
                dv += engines.ConsumePropellant(vesselId, 1.0, dt, massReg);
            }

            var finalMass = massReg.Get(vesselId)!.MassKg;
            var burnedMass = initialMass - finalMass;
            Assert.AreEqual(0.0, engines.EnginesFor(vesselId)![0].PropellantMassKg, 1.0,
                "Propellant should be exhausted after 100 s burn.");
            Assert.Greater(burnedMass, 1500.0, "Burned mass should be ~1700 kg.");
            Assert.Less(burnedMass, 1900.0);

            // Δv must be within 1% of the closed-form Tsiolkovsky value.
            var dvExpected = 300.0 * EngineModule.G0 * System.Math.Log(initialMass / finalMass);
            Assert.AreEqual(dvExpected, dv, dvExpected * 0.01);
        }

        [Test]
        public void ConsumePropellant_EmptyRegistry_ReturnsZero()
        {
            var vesselId = VesselId.New();
            var massReg = new VesselMassRegistry();
            massReg.Set(vesselId, new RigidVesselMass(1000.0, 100.0, 100.0, 100.0));
            var engines = new VesselEngineRegistry();
            Assert.AreEqual(0.0, engines.ConsumePropellant(vesselId, 1.0, 1.0, massReg));
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}