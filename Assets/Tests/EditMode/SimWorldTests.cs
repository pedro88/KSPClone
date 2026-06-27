using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class SimWorldTests
    {
        [Test]
        public void SimWorld_Constructs_WithMasterClock()
        {
            using var assertion = new NoUnityEngineAssertion();
            var world = new SimWorld();

            Assert.IsNotNull(world.Clock);
            Assert.AreEqual(0.0, world.Clock.GameTimeSeconds, 1e-9);
            Assert.AreEqual(1.0, world.Clock.Rate, 1e-9);
        }

        [Test]
        public void SimWorld_Tick_AdvancesMasterClock()
        {
            var world = new SimWorld();
            world.Tick(1.0 / 60.0);

            Assert.AreEqual(1.0 / 60.0, world.Clock.GameTimeSeconds, 1e-9);
        }

        [Test]
        public void SimWorld_RegisterVessel_PreservesOnRailsDefault()
        {
            var vessel = new Vessel(VesselId.New(),
                new Orbit(7_000_000.0, 0.0, 0, 0, 0, 0, 0, CelestialBodyId.Planet));
            var world = new SimWorld();

            world.RegisterVessel(vessel);

            Assert.IsTrue(vessel.OnRails, "Constitution Art. 3: on-rails is the default.");
            Assert.AreEqual(1, world.Vessels.Count);
        }
    }

    internal sealed class NoUnityEngineAssertion : System.IDisposable
    {
        public NoUnityEngineAssertion()
        {
            var simCoreAsm = typeof(SimWorld).Assembly;
            var unityEngineAsm = simCoreAsm.GetReferencedAssemblies();
            foreach (var refName in unityEngineAsm)
            {
                Assert.AreNotEqual(
                    "UnityEngine",
                    refName.Name,
                    $"SimCore must not reference UnityEngine (referenced: {refName.Name})");
            }
        }

        public void Dispose() { }
    }
}